using System.Diagnostics;
using System.Text;
using MusicLyricApp.Core.Service;

namespace MusicLyricAppTest.Core;

public sealed class BundledEngineFactAttribute : FactAttribute
{
    public BundledEngineFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BGM_TEST_ENGINE")))
            Skip = "Real-model integration test requires the packaged Windows engine.";
    }
}

public class EmbeddedSeparationServiceTest
{
    [Fact]
    public async Task MissingEngineGivesActionableError()
    {
        var service = new EmbeddedSeparationService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => service.SeparateAsync("input.mp3", "output.wav", default));
        Assert.Contains("完整解压", error.Message);
    }

    [Fact]
    public async Task PreCancelledTaskDoesNotTouchDestination()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var target = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(target, "existing");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EmbeddedSeparationService()
                .SeparateAsync("input.mp3", target, cancellation.Token));
            Assert.Equal("existing", await File.ReadAllTextAsync(target));
        }
        finally { File.Delete(target); }
    }

    [BundledEngineFact]
    public async Task BundledEngineSeparatesRealMp3WithUnicodePathsAndNoExternalPython()
    {
        var engine = Environment.GetEnvironmentVariable("BGM_TEST_ENGINE")!;
        var directory = Path.Combine(Path.GetTempPath(), "伴奏集成测试 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "测试 歌曲.mp3");
            var target = Path.Combine(directory, "测试 伴奏.wav");
            var start = new ProcessStartInfo(Path.Combine(engine, "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-ac", "2", source })
                start.ArgumentList.Add(argument);
            using (var ffmpeg = Process.Start(start)!)
            {
                await ffmpeg.WaitForExitAsync();
                Assert.Equal(0, ffmpeg.ExitCode);
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            await new EmbeddedSeparationService(engine).SeparateAsync(source, target, timeout.Token);
            var wave = await File.ReadAllBytesAsync(target);
            Assert.True(wave.Length > 100000, "Expected a full-length stereo WAV, not an empty/header-only file.");
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
            Assert.True(File.Exists(source));
            Assert.Empty(Directory.GetFiles(directory, "*.part"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [BundledEngineFact]
    public async Task CancelDuringStartupTerminatesWorkerAndPreservesDestination()
    {
        var target = Path.GetTempFileName();
        var source = Path.ChangeExtension(target, ".wav");
        try
        {
            using (var writer = new BinaryWriter(File.Create(source)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 44100 * 4 * 3);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)2); writer.Write(44100);
                writer.Write(44100 * 4); writer.Write((short)4); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(44100 * 4 * 3);
                writer.Write(new byte[44100 * 4 * 3]);
            }
            await File.WriteAllTextAsync(target, "existing");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new EmbeddedSeparationService(Environment.GetEnvironmentVariable("BGM_TEST_ENGINE"))
                    .SeparateAsync(source, target, cancellation.Token));
            Assert.Equal("existing", await File.ReadAllTextAsync(target));
            var workers = Process.GetProcessesByName("vocal-engine");
            try { Assert.Empty(workers); }
            finally { foreach (var worker in workers) worker.Dispose(); }
        }
        finally { File.Delete(target); File.Delete(source); }
    }
}
