using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using MusicLyricApp.Core.Service;

namespace MusicLyricApp.Views;

public sealed class UpdateWindow : Window
{
    private static UpdateWindow? _instance;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _notes = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 170 };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100 };
    private readonly Button _install = new() { Content = "下载更新并重启", IsEnabled = false };
    private readonly Button _cancel = new() { Content = "取消" };
    private CancellationTokenSource? _operation;
    private UpdateCheckResult? _result;

    public static void Open(UpdateCheckResult? result = null)
    {
        if (_instance != null) { _instance.Activate(); return; }
        _instance = new UpdateWindow(result);
        _instance.Closed += (_, _) => _instance = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            _instance.Show(owner);
        else _instance.Show();
    }

    public UpdateWindow(UpdateCheckResult? result = null)
    {
        Title = "检查更新"; Width = 600; Height = 430; MinWidth = 480; MinHeight = 350;
        _result = result;
        var releases = new Button { Content = "发布页面" };
        releases.Click += (_, _) => Process.Start(new ProcessStartInfo(UpdateCheckService.ReleasePageUrl) { UseShellExecute = true });
        Content = new Grid
        {
            Margin = new Thickness(20), RowSpacing = 12, RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            Children =
            {
                new TextBlock { Text = "当前版本：" + UpdateCheckService.CurrentVersion, FontSize = 18 },
                At(_status, 1), At(_notes, 2), At(_progress, 3),
                At(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _install, _cancel, releases } }, 4)
            }
        };
        Opened += async (_, _) => await CheckAsync();
        _install.Click += async (_, _) => await InstallAsync();
        _cancel.Click += (_, _) => { if (_operation != null) _operation.Cancel(); else Close(); };
        Closed += (_, _) => _operation?.Cancel();
    }

    private static T At<T>(T control, int row) where T : Control { Grid.SetRow(control, row); return control; }

    private async Task CheckAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        _status.Text = "正在检查当前项目的最新版本……";
        _progress.IsIndeterminate = true;
        try
        {
            _result ??= await new UpdateCheckService().CheckLatestVersionAsync(cancellation.Token);
            _notes.Text = _result.Info.Body;
            _status.Text = _result.HasUpdate
                ? "发现新版本：" + _result.Info.TagName + "。仅下载程序更新包，保留现有引擎、模型和设置。"
                : "当前已是最新版本。";
            _install.IsEnabled = _result.HasUpdate;
        }
        catch (OperationCanceledException) { _status.Text = "检查已取消。"; }
        catch (Exception error) { _status.Text = "检查失败：" + error.Message; }
        finally { _progress.IsIndeterminate = false; _progress.IsVisible = false; _operation = null; }
    }

    private async Task InstallAsync()
    {
        if (_result == null || _operation != null) return;
        if (!OperatingSystem.IsWindows()) { _status.Text = "应用内安装更新目前支持 Windows x64，其他系统请使用发布页面。"; return; }
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        _install.IsEnabled = false;
        _progress.IsVisible = true;
        string? planPath = null;
        try
        {
            var progress = new Progress<UpdateProgress>(p => { _status.Text = p.Message; _progress.Value = p.Percent; });
            planPath = await new InAppUpdateService().PrepareAsync(_result.Info, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not MainWindow main) throw new InvalidOperationException("无法重启主程序。");
            // Save current settings before starting a helper that waits for this process to exit.
            foreach (var window in desktop.Windows)
                if (window.DataContext is MusicLyricApp.ViewModels.SettingViewModel setting) setting.OnClosing();
            if (main.DataContext is MusicLyricApp.ViewModels.MainWindowViewModel vm) vm.SaveConfig();
            InAppUpdateService.StartInstaller(planPath);
            planPath = null; // The helper owns this workspace from now on.
            main.PrepareForUpdateExit();
            desktop.Shutdown();
        }
        catch (OperationCanceledException) { _status.Text = "更新下载已取消，原程序未改动。"; }
        catch (Exception error) { _status.Text = "更新失败：" + error.Message; }
        finally
        {
            if (planPath != null) Directory.Delete(Path.GetDirectoryName(planPath)!, true);
            _operation = null;
            _install.IsEnabled = true;
        }
    }
}
