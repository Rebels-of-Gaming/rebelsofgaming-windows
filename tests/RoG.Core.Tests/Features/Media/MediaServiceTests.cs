using System.Net;
using System.Security.Cryptography;
using RoG.Core.Api;
using RoG.Core.Auth;
using RoG.Core.Config;
using RoG.Core.Features.Media;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Media;

public sealed class MediaServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rog-media-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static MediaService Create(byte[] file, string sha)
    {
        var api = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            "{\"data\":{\"url\":\"https://cdn.example.test/f?sig=1\",\"expires_at\":null,\"filename\":\"../../evil/ET Map Pack.zip\",\"size\":" + file.Length + ",\"mime\":\"application/zip\",\"sha256\":\"" + sha + "\"}}"));
        var storage = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) });
        var rog = new RoGApi(RoGEnvironment.Get(RoGEnvironment.Production), new InMemoryTokenStore(), new ApiContext(), innerHandler: api);
        return new MediaService(rog.Client, new HttpClient(storage));
    }

    [Fact]
    public async Task Download_is_saved_with_a_safe_name_and_verified()
    {
        var bytes = "Hallo Rebels"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var path = await Create(bytes, sha).DownloadAsync("01V", _dir);
        var second = await Create(bytes, sha).DownloadAsync("01V", _dir);

        Assert.Equal(Path.Combine(_dir, "ET Map Pack.zip"), path);
        Assert.Equal(Path.Combine(_dir, "ET Map Pack (2).zip"), second);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Wrong_checksum_deletes_the_file()
    {
        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => Create("x"u8.ToArray(), new string('0', 64)).DownloadAsync("01V", _dir));

        Assert.Equal("checksum_mismatch", ex.Problem.Code);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("../x.zip", "x.zip")]
    [InlineData("..", "download")]
    [InlineData("a:b.txt", "a_b.txt")]
    public void Safe_file_names(string input, string expected) => Assert.Equal(expected, MediaService.SafeFileName(input));
}
