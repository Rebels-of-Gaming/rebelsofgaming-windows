using RoG.Core.Features.Media;

namespace RoG.Core.Tests.Features.Media;

public class MarkOfTheWebTests
{
    [Fact]
    public void Content_marks_the_internet_zone_without_query_or_credentials()
    {
        var text = MarkOfTheWeb.Content(new Uri("https://user:pw@cdn.example.test/files/map.pk3?X-Amz-Signature=secret"));

        Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://cdn.example.test/files/map.pk3\r\n", text);
        Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\n", MarkOfTheWeb.Content(null));
    }
}
