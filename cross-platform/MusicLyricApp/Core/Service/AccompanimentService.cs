using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicLyricApp.Core.Service;

/// <summary>Adapter for jianchang512/vocal-separate's multipart /api endpoint.</summary>
public sealed class AccompanimentService
{
    public static Uri ValidateEndpoint(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("请输入有效的 HTTP/HTTPS 服务地址。");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public async Task SeparateAsync(HttpClient client, Uri endpoint, Stream input,
        string extension, string destination, CancellationToken token)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("2stems"), "model");
        // Upstream caches by filename. Unique names prevent stale audio and collisions.
        form.Add(new StreamContent(input), "file", Guid.NewGuid().ToString("N") + extension);
        using var response = await client.PostAsync(new Uri(endpoint, "api"), form, token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var data = json.RootElement;
        if (!data.TryGetProperty("code", out var code) || code.GetInt32() != 0)
            throw new InvalidOperationException(data.TryGetProperty("msg", out var msg)
                ? msg.GetString() : "分离服务返回无效响应。");
        var path = data.GetProperty("data").EnumerateArray().Select(x => x.GetString())
            .FirstOrDefault(x => Uri.TryCreate(endpoint, x, out var uri) &&
                uri.AbsolutePath.EndsWith("/accompaniment.wav", StringComparison.OrdinalIgnoreCase));
        if (path == null) throw new InvalidOperationException("服务未返回 accompaniment.wav 伴奏文件。");
        // Upstream may advertise localhost even when a remote endpoint was configured.
        var returned = new Uri(endpoint, path);
        var download = new Uri(endpoint, returned.PathAndQuery);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var result = await client.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, token);
            result.EnsureSuccessStatusCode();
            await using (var output = File.Create(temporary))
                await result.Content.CopyToAsync(output, token);
            // Do not save an HTML/JSON error page under a WAV extension.
            var header = new byte[12];
            await using (var check = File.OpenRead(temporary))
            {
                if (await check.ReadAsync(header, token) != 12 ||
                    System.Text.Encoding.ASCII.GetString(header, 0, 4) != "RIFF" ||
                    System.Text.Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
                    throw new InvalidDataException("分离服务返回的文件不是有效 WAV。");
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
