using System.Net;
using RoG.Core.Features.Messages;
using RoG.Core.Tests.Support;

namespace RoG.Core.Tests.Features.Messages;

public class AttachmentServiceTests
{
    [Fact]
    public async Task Loads_bytes_from_attachment_endpoint()
    {
        var stub = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var http = new HttpClient(stub) { BaseAddress = new Uri("https://api.example/v1/") };

        var content = await new AttachmentService(http).LoadAsync("01ABC");

        Assert.Equal([1, 2, 3], content.Bytes);
        Assert.Equal("https://api.example/v1/attachments/01ABC", stub.Requests[0].Uri!.ToString());
    }

    [Theory]
    [InlineData(true, 1000, true)]
    [InlineData(false, 1000, false)]
    [InlineData(true, AttachmentService.MaxPreviewBytes + 1, false)]
    [InlineData(true, 0, false)]
    public void Only_small_images_are_previewed(bool image, long size, bool expected) =>
        Assert.Equal(expected, AttachmentService.CanPreview(new ChatAttachment("1", "a.png", "image/png", size, image)));
}
