using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.Core.Layout;
using Windows.Foundation;

namespace RoG.App.Controls;

/// <summary>
/// Kachel-Raster für <see cref="ItemsRepeater"/> mit unterschiedlich hohen Karten: Spalten wie
/// <c>UniformGridLayout</c> (Mindestbreite, Breite gefüllt), aber jede Zeile so hoch wie ihre höchste Karte –
/// aufgeklappte Bereiche (Ports, Auslastung) werden nicht abgeschnitten. Für kurze Listen (keine Virtualisierung).
/// </summary>
public sealed partial class CardGridLayout : NonVirtualizingLayout
{
    private readonly List<double> _rowHeights = [];
    private int _columns = 1;
    private double _itemWidth;

    public double MinItemWidth { get; set; } = 320;

    public double ColumnSpacing { get; set; } = 16;

    public double RowSpacing { get; set; } = 16;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        _rowHeights.Clear();
        if (children.Count == 0)
        {
            return new Size(0, 0);
        }

        var width = availableSize.Width;
        _columns = CardGrid.Columns(width, MinItemWidth, ColumnSpacing);
        _itemWidth = double.IsFinite(width) ? CardGrid.ItemWidth(width, _columns, ColumnSpacing) : MinItemWidth;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            child.Measure(new Size(_itemWidth, double.PositiveInfinity));
            var row = i / _columns;
            if (row == _rowHeights.Count)
            {
                _rowHeights.Add(0);
            }

            _rowHeights[row] = Math.Max(_rowHeights[row], child.DesiredSize.Height);
        }

        var used = double.IsFinite(width) ? width : (_columns * _itemWidth) + ((_columns - 1) * ColumnSpacing);
        return new Size(used, CardGrid.TotalHeight(_rowHeights, RowSpacing));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var y = 0d;
        for (var row = 0; row < _rowHeights.Count; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                var index = (row * _columns) + column;
                if (index >= children.Count)
                {
                    break;
                }

                var x = column * (_itemWidth + ColumnSpacing);
                children[index].Arrange(new Rect(x, y, _itemWidth, _rowHeights[row]));
            }

            y += _rowHeights[row] + RowSpacing;
        }

        return finalSize;
    }
}
