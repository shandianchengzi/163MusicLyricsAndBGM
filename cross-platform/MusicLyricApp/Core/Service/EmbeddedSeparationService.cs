using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MusicLyricApp.Core.Service;

/// <summary>Runs the bundled vocal-separate worker without a server or browser.</summary>
public sealed class EmbeddedSeparationService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _engineDirectory;

    public EmbeddedSeparationService(string? engineDirectory = null)
    {
        _engineDirectory = engineDirectory ?? Path.Combine(AppContext.BaseDirectory, "engine");
    }

    public async Task SeparateAsync(string source, string destination, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        string? workspace = null;
        string? partial = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var executable = Path.Combine(_engineDirectory, "vocal-engine.exe");
            if (!File.Exists(executable) || !File.Exists(Path.Combine(_engineDirectory, "pretrained_models", "2stems", "model.meta")))
                throw new FileNotFoundException("内置分离引擎不完整，请重新下载并完整解压含引擎的 Windows 发布包。");
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("内置引擎目前支持 Windows x64，其他系统可使用高级选项连接分离服务。");
            workspace = Path.Combine(Path.GetTempPath(), "MusicLyricsAndBGM", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = _engineDirectory,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            start.ArgumentList.Add(Path.GetFullPath(source));
            start.ArgumentList.Add(workspace);
            // Keep CPU-only computers usable without a CUDA install; limit parallel memory pressure.
            start.Environment["TF_CPP_MIN_LOG_LEVEL"] = "2";
            start.Environment["TF_NUM_INTRAOP_THREADS"] = Math.Min(4, Environment.ProcessorCount).ToString();
            start.Environment["TF_NUM_INTEROP_THREADS"] = "1";
            start.Environment["OMP_NUM_THREADS"] = Math.Min(4, Environment.ProcessorCount).ToString();
            using var process = new Process { StartInfo = start };
            var log = new StringBuilder();
            void Record(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (log)
                {
                    log.AppendLine(e.Data);
                    if (log.Length > 8192) log.Remove(0, log.Length - 8192);
                }
            }
            process.OutputDataReceived += Record;
            process.ErrorDataReceived += Record;
            process.Start();
            IDisposable? job = null;
            try
            {
                job = WindowsProcessJob.Attach(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(token);
                process.WaitForExit(); // Drain redirected output callbacks before reading the log.
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("内置引擎分离失败：" + log);
            }
            finally
            {
                // Cancellation/closing stops TensorFlow and its FFmpeg children before cleaning up.
                if (job != null) job.Dispose();
                else if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            token.ThrowIfCancellationRequested();
            var result = Path.Combine(workspace, "accompaniment.wav");
            await using (var input = File.OpenRead(result))
            {
                var header = new byte[12];
                await input.ReadExactlyAsync(header, token);
                if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" || Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
                    throw new InvalidDataException("内置引擎未生成有效的 WAV 伴奏。");
                input.Position = 0;
                partial = destination + "." + Guid.NewGuid().ToString("N") + ".part";
                await using var output = File.Create(partial);
                await input.CopyToAsync(output, token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination, true);
        }
        finally
        {
            try
            {
                if (partial != null && File.Exists(partial)) File.Delete(partial);
                if (workspace != null && Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
            }
            finally { Gate.Release(); }
        }
    }
}
