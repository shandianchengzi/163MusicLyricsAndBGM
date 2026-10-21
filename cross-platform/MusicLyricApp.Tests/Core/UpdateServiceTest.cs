using System.Net;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using MusicLyricApp.Core.Service;

namespace MusicLyricAppTest.Core;

public class UpdateServiceTest
{
    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public Uri? Requested;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"tag_name\":\"v7.3.0-bgm.5\",\"draft\":false,\"prerelease\":false}") });
        }
    }

    [Fact]
    public async Task ChecksOwnRepositoryAndDetectsBgmRevisionUpdates()
    {
        var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        var result = await new UpdateCheckService().CheckLatestVersionAsync(client: client, currentVersion: "v7.3.0-bgm.4");
        Assert.True(result.HasUpdate);
        Assert.Equal(UpdateCheckService.LatestReleaseApi, handler.Requested!.AbsoluteUri);
        Assert.True(UpdateCheckService.ParseVersion("v7.3.0-bgm.10") > UpdateCheckService.ParseVersion("v7.3.0-bgm.9"));
        Assert.True(UpdateCheckService.ParseVersion("v7.3.1-bgm.1") > UpdateCheckService.ParseVersion("v7.3.0-bgm.99"));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("engine/model.bin")]
    [InlineData("Engine/vocal-engine.exe")]
    [InlineData("C:/Windows/file.dll")]
    [InlineData("app.dll:stream")]
    [InlineData("MusicLyricAppSetting.json")]
    public void RejectsPathsOutsideProgramUpdateScope(string name) =>
        Assert.Throws<InvalidDataException>(() => InAppUpdateService.SafePath(Path.GetTempPath(), name));

    private static (UpdatePlan Plan, string Root) Fixture(bool blockSecondFile = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "update-test-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "app");
        var stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(target); Directory.CreateDirectory(stage);
        Directory.CreateDirectory(Path.Combine(target, "engine"));
        File.WriteAllText(Path.Combine(target, "engine/model.bin"), "model-original");
        File.WriteAllText(Path.Combine(target, "MusicLyricAppSetting.json"), "settings-original");
        File.WriteAllText(Path.Combine(target, "app.dll"), "old-app");
        File.WriteAllText(Path.Combine(target, "runtime.dll"), "runtime-original");
        File.WriteAllText(Path.Combine(stage, "app.dll"), "new-app");
        var entries = new List<UpdateFile> { new("app.dll", InAppUpdateService.HashFile(Path.Combine(stage, "app.dll"))),
            new("runtime.dll", InAppUpdateService.HashFile(Path.Combine(target, "runtime.dll"))) };
        var files = new List<string> { "app.dll" };
        if (blockSecondFile)
        {
            Directory.CreateDirectory(Path.Combine(stage, "blocked"));
            File.WriteAllText(Path.Combine(stage, "blocked/second.dll"), "new-second");
            File.WriteAllText(Path.Combine(target, "blocked"), "a-file-blocks-directory");
            entries.Add(new UpdateFile("blocked/second.dll", InAppUpdateService.HashFile(Path.Combine(stage, "blocked/second.dll"))));
            files.Add("blocked/second.dll");
        }
        var manifest = new UpdateManifest("v7.3.0-bgm.5", InAppUpdateService.EngineVersion, entries.ToArray(),
            new UpdateArchive("update.zip", new string('a', 64)),
            new UpdateArchive("delta.zip", new string('b', 64), "v7.3.0-bgm.4", files.ToArray()));
        return (new UpdatePlan(target, stage, manifest, files.ToArray(), 0, 0), root);
    }

    [Fact]
    public void AppliesDeltaAndPreservesModelsAndSettings()
    {
        var (plan, root) = Fixture();
        try
        {
            Assert.True(InAppUpdateService.CanUseDelta(plan.Manifest, plan.TargetDirectory, "v7.3.0-bgm.4"));
            UpdateInstaller.ApplyFiles(plan);
            Assert.Equal("new-app", File.ReadAllText(Path.Combine(plan.TargetDirectory, "app.dll")));
            Assert.Equal("runtime-original", File.ReadAllText(Path.Combine(plan.TargetDirectory, "runtime.dll")));
            Assert.Equal("model-original", File.ReadAllText(Path.Combine(plan.TargetDirectory, "engine/model.bin")));
            Assert.Equal("settings-original", File.ReadAllText(Path.Combine(plan.TargetDirectory, "MusicLyricAppSetting.json")));
            Assert.True(File.Exists(Path.Combine(plan.TargetDirectory, "update-manifest.json")));
        }
        finally { Directory.Delete(root, true); }
    }


    [Fact]
    public async Task TemporaryUpdaterEntryPointInstallsWithoutOpeningTheUi()
    {
        var (plan, root) = Fixture();
        try
        {
            var file = Path.Combine(root, "plan.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(plan with { Restart = false }, InAppUpdateService.JsonOptions));
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(typeof(MusicLyricApp.App).Assembly.Location);
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(file);
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            Assert.True(process.ExitCode == 0, await errors + await output);
            Assert.Equal("new-app", File.ReadAllText(Path.Combine(plan.TargetDirectory, "app.dll")));
            Assert.False(File.Exists(Path.Combine(root, "update-error.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DamagedBaseFallsBackToProgramPackage()
    {
        var (plan, root) = Fixture();
        try
        {
            File.WriteAllText(Path.Combine(plan.TargetDirectory, "runtime.dll"), "corrupted");
            Assert.False(InAppUpdateService.CanUseDelta(plan.Manifest, plan.TargetDirectory, "v7.3.0-bgm.4"));
            Assert.Throws<InvalidDataException>(() => UpdateInstaller.ApplyFiles(plan));
            Assert.Equal("old-app", File.ReadAllText(Path.Combine(plan.TargetDirectory, "app.dll")));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class WaitingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 100;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(Timeout.Infinite, token); return 0; }
    }

    private sealed class DownloadHandler(UpdateManifest manifest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("update-manifest.json")
                    ? new StringContent(JsonSerializer.Serialize(manifest, InAppUpdateService.JsonOptions))
                    : new StreamContent(new WaitingStream())
            });
    }

    [Fact]
    public async Task CancellingDownloadKeepsInstallationUntouched()
    {
        var (plan, root) = Fixture();
        try
        {
            File.WriteAllText(Path.Combine(plan.TargetDirectory, "engine/vocal-engine.exe"), "engine-original");
            var manifest = plan.Manifest with { Delta = null };
            using var client = new HttpClient(new DownloadHandler(manifest));
            var url = UpdateCheckService.RepositoryUrl + "/releases/download/" + manifest.Version + "/";
            var info = new MusicLyricApp.Models.GitHubInfo
            {
                TagName = manifest.Version,
                Assets = new()
                {
                    new() { Name = "update-manifest.json", State = "uploaded", BrowserDownloadUrl = new Uri(url + "update-manifest.json") },
                    new() { Name = "update.zip", State = "uploaded", BrowserDownloadUrl = new Uri(url + "update.zip"), Size = 100 }
                }
            };
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new InAppUpdateService(plan.TargetDirectory, client).PrepareAsync(info, new Progress<UpdateProgress>(), cancellation.Token));
            Assert.Equal("old-app", File.ReadAllText(Path.Combine(plan.TargetDirectory, "app.dll")));
            Assert.Equal("model-original", File.ReadAllText(Path.Combine(plan.TargetDirectory, "engine/model.bin")));
            Assert.Equal("settings-original", File.ReadAllText(Path.Combine(plan.TargetDirectory, "MusicLyricAppSetting.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BadDownloadDoesNotTouchInstalledProgram()
    {
        var (plan, root) = Fixture();
        try
        {
            File.WriteAllText(Path.Combine(plan.StageDirectory, "app.dll"), "tampered");
            Assert.Throws<InvalidDataException>(() => UpdateInstaller.ApplyFiles(plan));
            Assert.Equal("old-app", File.ReadAllText(Path.Combine(plan.TargetDirectory, "app.dll")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReplacementFailureRollsBackAlreadyUpdatedFiles()
    {
        var (plan, root) = Fixture(true);
        try
        {
            Assert.ThrowsAny<IOException>(() => UpdateInstaller.ApplyFiles(plan));
            Assert.Equal("old-app", File.ReadAllText(Path.Combine(plan.TargetDirectory, "app.dll")));
            Assert.False(File.Exists(Path.Combine(plan.TargetDirectory, "update-manifest.json")));
        }
        finally { Directory.Delete(root, true); }
    }
}
