using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using RoG.Core.Content;

namespace RoG.App.Controls;

/// <summary>Nutzertext (Chat, Kommentare) als markierbarer Text mit klickbaren http(s)-Links.</summary>
public sealed partial class LinkTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LinkTextBlock), new PropertyMetadata(null, (d, _) => ((LinkTextBlock)d).Render()));

    private readonly RichTextBlock _block = new()
    {
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
    };

    public LinkTextBlock()
    {
        _block.FontFamily = (FontFamily)Application.Current.Resources["RoGTextFont"];
        _block.Foreground = (Brush)Application.Current.Resources["RoGInkBrush"];
        Content = _block;
        IsTabStop = false;
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Render()
    {
        var paragraph = new Paragraph();
        foreach (var segment in TextLinks.Split(Text))
        {
            if (segment.Link is { } link)
            {
                var hyperlink = new Hyperlink { NavigateUri = link, Foreground = (Brush)Application.Current.Resources["RoGRedBrush"] };
                hyperlink.Inlines.Add(new Run { Text = segment.Text });
                paragraph.Inlines.Add(hyperlink);
            }
            else
            {
                paragraph.Inlines.Add(new Run { Text = segment.Text });
            }
        }

        _block.Blocks.Clear();
        _block.Blocks.Add(paragraph);
    }
}
