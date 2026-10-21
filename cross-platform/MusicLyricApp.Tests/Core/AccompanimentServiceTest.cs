using System.Net;
using System.Text;
using MusicLyricApp.Core.Service;

namespace MusicLyricAppTest.Core;

public class AccompanimentServiceTest
{
    private sealed class Handler(bool fail = false) : HttpMessageHandler
    {
        public string? Upload;
        public Uri? Download;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post)
            {
                Upload = await request.Content!.ReadAsStringAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(fail ? "{\"code\":1,\"msg\":\"missing model\"}" :
                        "{\"code\":0,\"data\":[\"http://localhost:9999/static/files/abc/vocals.wav\",\"http://localhost:9999/static/files/abc/accompaniment.wav\"]}")
                };
            }
            Download = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("RIFFxxxxWAVEdata")) };
        }
    }

    [Fact]
    public async Task UploadsTwoStemsAndSavesOnlyAccompanimentFromConfiguredHost()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        using var input = new MemoryStream(new byte[] { 1, 2, 3 });
        var target = Path.GetTempFileName();
        try
        {
            await new AccompanimentService().SeparateAsync(client, new Uri("http://192.168.1.2:9999/"), input, ".mp3", target, default);
            Assert.Contains("2stems", handler.Upload);
            Assert.Contains(".mp3", handler.Upload);
            Assert.Equal("http://192.168.1.2:9999/static/files/abc/accompaniment.wav", handler.Download!.AbsoluteUri);
            Assert.Equal("RIFFxxxxWAVEdata", await File.ReadAllTextAsync(target));
        }
        finally { File.Delete(target); }
    }

    [Fact]
    public async Task ServiceFailurePreservesExistingDestination()
    {
        using var client = new HttpClient(new Handler(true));
        using var input = new MemoryStream();
        var target = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(target, "existing");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new AccompanimentService()
                .SeparateAsync(client, new Uri("http://localhost:9999/"), input, ".mp3", target, default));
            Assert.Contains("missing model", error.Message);
            Assert.Equal("existing", await File.ReadAllTextAsync(target));
        }
        finally { File.Delete(target); }
    }

    [Theory]
    [InlineData("file:///tmp/audio")]
    [InlineData("invalid")]
    public void RejectsInvalidServiceAddress(string address) =>
        Assert.Throws<ArgumentException>(() => AccompanimentService.ValidateEndpoint(address));
}
