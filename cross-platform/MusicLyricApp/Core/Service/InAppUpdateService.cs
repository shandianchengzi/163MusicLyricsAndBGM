using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicLyricApp.Models;

namespace MusicLyricApp.Core.Service;

public sealed record UpdateFile(string Path, string Sha256);
public sealed record UpdateArchive(string AssetName, string Sha256, string? BaseVersion = null, string[]? Files = null);
public sealed record UpdateManifest(string Version, string EngineVersion, UpdateFile[] Files, UpdateArchive Package, UpdateArchive? Delta = null);
public sealed record UpdatePlan(string TargetDirectory, string StageDirectory, UpdateManifest Manifest, string[] Files, int ParentId, long ParentStartTicks, bool Restart = true);
public sealed record UpdateProgress(string Message, double Percent);

public sealed class InAppUpdateService
{
    private readonly string _installationDirectory;
    private readonly HttpClient? _client;

    public InAppUpdateService(string? installationDirectory = null, HttpClient? client = null)
    {
        _installationDirectory = installationDirectory ?? AppContext.BaseDirectory;
        _client = client;
    }

    public const string EngineVersion = "vocal-separate-0.0.4-headless-v1";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static string SafePath(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relative) || normalized.StartsWith('/') || normalized.Contains(':') ||
            normalized.Split('/').Any(p => p is ".." or "." or "") ||
            normalized.Split('/')[0].Equals("engine", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("MusicLyricAppSetting", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新文件路径无效：" + relative);
        var target = Path.GetFullPath(Path.Combine(root, normalized));
        if (!target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新路径超出程序目录。");
        for (var parent = Path.GetDirectoryName(target); parent != null &&
            !string.Equals(parent, Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
             parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("更新目录不能包含链接。");
        return target;
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void VerifyFile(string path, string expected)
    {
        if (expected?.Length != 64 || !HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新文件 SHA256 校验失败：" + Path.GetFileName(path));
    }

    public static void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest.Files == null || manifest.Files.Length == 0 || manifest.Package == null ||
            UpdateCheckService.ParseVersion(manifest.Version) == new Version(0, 0, 0, 0))
            throw new InvalidDataException("更新清单不完整。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            SafePath(Path.GetTempPath(), file.Path);
            if (file.Path.Equals("update-manifest.json", StringComparison.OrdinalIgnoreCase) ||
                !names.Add(file.Path.Replace('\\', '/')) || file.Sha256?.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("更新清单包含重复或无效文件。");
        }
        if (manifest.EngineVersion != EngineVersion)
            throw new InvalidOperationException("此版本需要更新分离引擎，请通过发布页面下载完整包。");
    }

    public static bool CanUseDelta(UpdateManifest latest, string directory, string currentVersion)
    {
        var delta = latest.Delta;
        if (delta?.Files == null || delta.BaseVersion != currentVersion) return false;
        var changed = delta.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return latest.Files.Where(f => !changed.Contains(f.Path)).All(f =>
            File.Exists(SafePath(directory, f.Path)) && HashFile(SafePath(directory, f.Path)).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static Asset FindAsset(GitHubInfo release, string name)
    {
        var asset = release.Assets?.FirstOrDefault(a => a.Name == name && a.State == "uploaded")
            ?? throw new InvalidOperationException("发布包尚未就绪：" + name);
        if (asset.BrowserDownloadUrl == null || !asset.BrowserDownloadUrl.AbsoluteUri.StartsWith(
                UpdateCheckService.RepositoryUrl + "/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("更新下载地址不属于当前项目。");
        return asset;
    }

    public async Task<string> PrepareAsync(GitHubInfo release, IProgress<UpdateProgress> progress, CancellationToken token)
    {
        var install = _installationDirectory;
        if (!File.Exists(Path.Combine(install, "engine", "vocal-engine.exe")))
            throw new InvalidOperationException("未找到本地分离引擎，请先安装完整版本。");
        // Fail before downloading or exiting when the installation directory is read-only.
        var probe = Path.Combine(install, ".update-probe-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { }
        File.Delete(probe);
        var workspace = Path.Combine(Path.GetTempPath(), "MusicLyricsAndBGM-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            using var owned = _client == null ? NetworkClientFactory.CreateHttpClient(600) : null;
            var client = _client ?? owned!;
            var manifestAsset = FindAsset(release, "update-manifest.json");
            var text = await client.GetStringAsync(manifestAsset.BrowserDownloadUrl, token);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(text, JsonOptions)
                ?? throw new InvalidDataException("更新清单无效。");
            ValidateManifest(manifest);
            if (manifest.Version != release.TagName) throw new InvalidDataException("版本与更新清单不匹配。");
            var archive = CanUseDelta(manifest, install, UpdateCheckService.CurrentVersion) ? manifest.Delta! : manifest.Package;
            var asset = FindAsset(release, archive.AssetName);
            var zip = Path.Combine(workspace, "update.zip");
            using (var response = await client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength ?? asset.Size;
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(zip);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    total += read;
                    progress.Report(new UpdateProgress($"下载{(archive.BaseVersion == null ? "程序更新包" : "差分包")}：{total / 1048576.0:F1} / {length / 1048576.0:F1} MB", length > 0 ? 95.0 * total / length : 0));
                }
            }
            VerifyFile(zip, archive.Sha256);
            var stage = Path.Combine(workspace, "stage");
            Directory.CreateDirectory(stage);
            var selected = (archive.Files ?? manifest.Files.Select(f => f.Path).ToArray()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expected = manifest.Files.Where(f => selected.Contains(f.Path)).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            if (expected.Count != selected.Count) throw new InvalidDataException("差分文件清单无效。");
            using (var package = ZipFile.OpenRead(zip))
            {
                var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in package.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith('/')) continue;
                    var name = entry.FullName.Replace('\\', '/');
                    var target = SafePath(stage, name);
                    if (!expected.TryGetValue(name, out var file) || !extracted.Add(name))
                        throw new InvalidDataException("更新包包含清单之外的文件。");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                    VerifyFile(target, file.Sha256);
                }
                if (extracted.Count != selected.Count) throw new InvalidDataException("更新包缺少程序文件。");
            }
            progress.Report(new UpdateProgress("校验通过，正在准备重启……", 98));
            var helper = Path.Combine(workspace, "helper");
            foreach (var file in Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(install, file).Replace('\\', '/');
                if (relative.Split('/')[0].Equals("engine", StringComparison.OrdinalIgnoreCase) ||
                    relative.Contains("MusicLyricAppSetting", StringComparison.OrdinalIgnoreCase) ||
                    !new[] { ".dll", ".exe", ".json", ".config" }.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                var target = SafePath(helper, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            token.ThrowIfCancellationRequested();
            using var parent = Process.GetCurrentProcess();
            var plan = new UpdatePlan(install, stage, manifest, selected.ToArray(), parent.Id, parent.StartTime.ToUniversalTime().Ticks);
            var planPath = Path.Combine(workspace, "plan.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), token);
            return planPath;
        }
        catch { Directory.Delete(workspace, true); throw; }
    }

    public static void CleanupOldUpdates()
    {
        var root = Path.Combine(Path.GetTempPath(), "MusicLyricsAndBGM-update");
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (Directory.GetLastWriteTimeUtc(directory) > DateTime.UtcNow.AddDays(-1)) continue;
                try { Directory.Delete(directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void StartInstaller(string planPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("应用内安装更新目前支持 Windows x64。");
        var executable = Path.Combine(Path.GetDirectoryName(planPath)!, "helper", "MusicLyricApp.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(planPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动更新安装程序。");
    }
}
