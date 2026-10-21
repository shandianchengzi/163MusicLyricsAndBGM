using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicLyricApp;
using MusicLyricApp.Core.Utils;
using MusicLyricApp.Models;
using MusicLyricApp.Views;

[assembly: AvaloniaTestApplication(typeof(MusicLyricAppTest.Views.TestAppBuilder))]

namespace MusicLyricAppTest.Views;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class DarkThemeTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    public void LegacyThemeSettingsCannotOverrideDarkPalette(int legacyMode)
    {
        var bean = ("{\"Config\":{\"ThemeMode\":" + legacyMode + ",\"SingerSeparator\":\" / \"}}")
            .ToEntity<SettingBean>();
        Assert.Equal(ThemeModeEnum.DARK, bean.Config.ThemeMode);
        Assert.Equal(" / ", bean.Config.SingerSeparator);
        var saved = bean.ToJson().ToEntity<SettingBean>();
        Assert.Equal(ThemeModeEnum.DARK, saved.Config.ThemeMode);
    }

    [AvaloniaFact]
    public void ReopeningSettingsAndSwitchingTabsKeepsDarkReadablePanels()
    {
        var bean = new SettingBean();
        for (var pass = 0; pass < 2; pass++)
        {
            var window = new SettingWindow(bean);
            window.Show();
            try
            {
                Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
                Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                for (var i = 0; i < tabs.ItemCount; i++)
                {
                    tabs.SelectedIndex = i;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    SaveFrame(window, $"settings-tab-{i}.png");
                    var cards = window.GetVisualDescendants().OfType<Border>()
                        .Where(b => b.Padding == new Thickness(12) && b.Background is ISolidColorBrush).ToArray();
                    Assert.NotEmpty(cards);
                    foreach (var card in cards)
                    {
                        var background = ((ISolidColorBrush)card.Background!).Color;
                        Assert.True(background.R < 80 && background.G < 80 && background.B < 80,
                            $"Settings tab {i} contains a light card: {background}");
                    }
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "主题模式");
                    Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
                }
                tabs.SelectedIndex = 0;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var label = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "中文处理");
                var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
                Assert.True(foreground.R > 180 && foreground.G > 180 && foreground.B > 180,
                    $"Dark settings have unreadable text: {foreground}");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void AccompanimentDialogInheritsDarkPalette()
    {
        var window = new AccompanimentWindow("", "测试歌曲");
        window.Show();
        try
        {
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            SaveFrame(window, "accompaniment.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UpdateDialogUsesTheSameDarkPalette()
    {
        var window = new UpdateWindow(new MusicLyricApp.Core.Service.UpdateCheckResult(true,
            new GitHubInfo { TagName = "v7.3.0-bgm.5", Body = "程序更新：保留分离引擎、模型与设置。" }));
        window.Show();
        try
        {
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            SaveFrame(window, "update.png");
        }
        finally { window.Close(); }
    }

    private static void SaveFrame(Window window, string filename)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ui-screenshots");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, filename));
    }
}
