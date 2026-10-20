using RoG.Core.Layout;

namespace RoG.Core.Tests.Layout;

public sealed class CardGridTests
{
    [Theory]
    [InlineData(1310, 320, 16, 0, 3)]
    [InlineData(656, 320, 16, 0, 2)]
    [InlineData(655, 320, 16, 0, 1)]
    [InlineData(200, 320, 16, 0, 1)]
    [InlineData(1310, 320, 16, 2, 2)]
    [InlineData(double.PositiveInfinity, 320, 16, 0, 1)]
    [InlineData(0, 320, 16, 0, 1)]
    public void Columns_fit_min_width_and_spacing(double width, double min, double spacing, int count, int expected) =>
        Assert.Equal(expected, CardGrid.Columns(width, min, spacing, count));

    [Fact]
    public void Item_width_splits_remaining_space()
    {
        Assert.Equal(647, CardGrid.ItemWidth(1310, 2, 16));
        Assert.Equal(0, CardGrid.ItemWidth(10, 2, 16));
    }

    [Fact]
    public void Total_height_adds_spacing_between_rows()
    {
        Assert.Equal(0, CardGrid.TotalHeight([], 16));
        Assert.Equal(380, CardGrid.TotalHeight([200, 164], 16));
    }
}
