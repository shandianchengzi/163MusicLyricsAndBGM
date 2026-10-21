using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MusicLyricApp.Core.Service;

namespace MusicLyricApp.Views;

public sealed class AccompanimentWindow : Window
{
    private readonly CheckBox _external = new() { Content = "连接外部分离服务（高级）" };
    private readonly TextBox _endpoint = new() { Text = "http://127.0.0.1:9999", IsVisible = false };
    private readonly TextBlock _status = new() { Text = "请选择本地歌曲，或下载当前搜索到的歌曲。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _local = new() { Content = "导入音频并提取伴奏" };
    private readonly Button _online = new() { Content = "下载当前歌曲并提取伴奏" };
    private readonly Button _cancel = new() { Content = "取消", IsEnabled = false };
    private readonly string _link;
    private readonly string _name;
    private CancellationTokenSource? _operation;

    public AccompanimentWindow(string? link, string? name)
    {
        _link = link ?? "";
        _name = name ?? "歌曲";
        Title = "伴奏提取";
        Width = 550; Height = 370; MinWidth = 450; MinHeight = 340;
        _online.IsEnabled = Uri.TryCreate(_link, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https");
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "已内置分离引擎与 2stems 模型，选择音频即可提取 WAV 伴奏。\n内置模式在本机处理，无需启动其他程序或联网下载模型。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _external, _endpoint,
                new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, Children = { _local, _online, _cancel } },
                _status
            }
        };
        _external.IsCheckedChanged += (_, _) => _endpoint.IsVisible = _external.IsChecked == true;
        _local.Click += async (_, _) => await RunAsync(false);
        _online.Click += async (_, _) => await RunAsync(true);
        _cancel.Click += (_, _) => _operation?.Cancel();
        Closed += (_, _) => _operation?.Cancel();
    }

    private async Task RunAsync(bool online)
    {
        if (_operation != null) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        _local.IsEnabled = _online.IsEnabled = _endpoint.IsEnabled = _external.IsEnabled = false;
        _cancel.IsEnabled = true;
        string? temporary = null;
        try
        {
            var useExternal = _external.IsChecked == true;
            var endpoint = useExternal ? AccompanimentService.ValidateEndpoint(_endpoint.Text ?? "") : null;
            string source;
            if (!online)
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "选择要分离的音频", AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("音频") { Patterns = new[] { "*.mp3", "*.wav", "*.flac", "*.aac", "*.m4a" } } }
                });
                if (files.Count == 0) return;
                source = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("请选择本地音频文件。");
            }
            else source = "";
            if (useExternal && !online &&
                (string.Equals(Path.GetExtension(source), ".aac", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetExtension(source), ".m4a", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("AAC/M4A 请使用内置分离引擎：取消勾选“连接外部分离服务”后重试。");
            var suggested = online ? _name : Path.GetFileNameWithoutExtension(source);
            foreach (var c in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(c, '_');
            var save = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存伴奏", SuggestedFileName = suggested + "_伴奏.wav", DefaultExtension = "wav",
                FileTypeChoices = new[] { new FilePickerFileType("WAV 伴奏") { Patterns = new[] { "*.wav" } } },
                ShowOverwritePrompt = true
            });
            if (save == null) return;
            var destination = save.TryGetLocalPath() ?? throw new InvalidOperationException("请选择本地保存位置。");
            cancellation.CancelAfter(TimeSpan.FromMinutes(60));
            var token = cancellation.Token;
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
            if (online)
            {
                _status.Text = "正在下载歌曲……";
                // MP3 is the platform's normal public song-link format. Reject preview/error HTML.
                temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
                using var response = await client.GetAsync(_link, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                var mime = response.Content.Headers.ContentType?.MediaType ?? "";
                if (mime.Contains("text/") || mime.Contains("json"))
                    throw new InvalidDataException("歌曲直链未返回音频，请改用本地导入。");
                await using (var file = File.Create(temporary)) await response.Content.CopyToAsync(file, token);
                source = temporary;
            }
            _status.Text = "正在分离并保存伴奏……（长歌曲可能需要数分钟）";
            if (useExternal)
            {
                await using var input = File.OpenRead(source);
                await new AccompanimentService().SeparateAsync(client, endpoint!, input,
                    Path.GetExtension(source).ToLowerInvariant(), destination, token);
            }
            else
            {
                await new EmbeddedSeparationService().SeparateAsync(source, destination, token);
            }
            _status.Text = "伴奏已保存：" + destination;
        }
        catch (OperationCanceledException) { _status.Text = "操作已取消或超时。内置分离进程已停止；外部服务的任务可能继续运行。"; }
        catch (Exception ex) { _status.Text = "提取失败：" + ex.Message + "\n请确保发布包已完整解压（含 engine 文件夹）；歌曲下载失败时可改用本地导入。"; }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            _operation = null;
            _local.IsEnabled = _endpoint.IsEnabled = _external.IsEnabled = true;
            _online.IsEnabled = Uri.TryCreate(_link, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
            _cancel.IsEnabled = false;
        }
    }
}
