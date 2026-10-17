using RoG.Core.Content;

namespace RoG.Core.Tests.Content;

public class GameTextTests
{
    [Theory]
    [InlineData("^0[^8RoG^0]^8~", "[RoG]~")]
    [InlineData("^1Red^7Name", "RedName")]
    [InlineData("[RoG]", "[RoG]")]
    [InlineData("^1^2", null)]
    [InlineData(null, null)]
    public void Strips_quake_color_codes(string? input, string? expected) => Assert.Equal(expected, GameText.StripColors(input));
}
