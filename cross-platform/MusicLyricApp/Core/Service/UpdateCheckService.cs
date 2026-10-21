using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MusicLyricApp.Models;
using Newtonsoft.Json;

namespace MusicLyricApp.Core.Service;

public partial class UpdateCheckService
{
    public const string RepositoryUrl = "https://github.com/shandianchengzi/163MusicLyricsAndBGM";
    public const string ReleasePageUrl = RepositoryUrl + "/releases/latest";
    public const string LatestReleaseApi = "https://api.github.com/repos/shandianchengzi/163MusicLyricsAndBGM/releases/latest";
    public const string BuildVersion = "v7.3.0-bgm.4";

    public static string CurrentVersion
    {
        get
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "app-version.txt");
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : BuildVersion;
        }
    }

    public async Task<UpdateCheckResult> CheckLatestVersionAsync(CancellationToken token = default,
        HttpClient? client = null, string? currentVersion = null)
    {
        using var owned = client == null ? NetworkClientFactory.CreateHttpClient(30) : null;
        client ??= owned!;
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("MusicLyricsAndBGM/" + CurrentVersion.TrimStart('v'));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var info = JsonConvert.DeserializeObject<GitHubInfo>(await response.Content.ReadAsStringAsync(token))
            ?? throw new InvalidOperationException("无法读取更新信息。");
        return new UpdateCheckResult(!info.Draft && !info.Prerelease &&
            ParseVersion(info.TagName) > ParseVersion(currentVersion ?? CurrentVersion), info);
    }

    public static Version ParseVersion(string? text)
    {
        var match = VersionRegex().Match(text ?? "");
        if (!match.Success) return new Version(0, 0, 0, 0);
        int Part(int n) => int.TryParse(match.Groups[n].Value, out var value) ? value : 0;
        return new Version(Part(1), Part(2), Part(3), Part(4));
    }

    [GeneratedRegex(@"v?(\d+)\.(\d+)(?:\.(\d+))?(?:-bgm\.(\d+))?")]
    private static partial Regex VersionRegex();
}

public record UpdateCheckResult(bool HasUpdate, GitHubInfo Info);
