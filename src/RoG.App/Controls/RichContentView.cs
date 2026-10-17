using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using RoG.Core.Content;
using RoG.Core.Localization;
using Windows.UI.Text;

namespace RoG.App.Controls;

/// <summary>
/// Zeichnet ein <see cref="RichDocument"/> (HTML der API) mit nativen Steuerelementen im RoG-Design: Absätze als
/// markierbarer Text, Überschriften in Barlow Condensed, Zitate mit roter Linie, Code in Consolas, Spoiler als Expander,
/// Bilder nur von der Plattform. Links öffnen den Standardbrowser.
/// </summary>
public sealed partial class RichContentView : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(RichDocument), typeof(RichContentView), new PropertyMetadata(null, (d, _) => ((RichContentView)d).Render()));

    private readonly StackPanel _root = new() { Spacing = 12 };

    public RichContentView()
    {
        Content = _root;
        IsTabStop = false;
    }

    public RichDocument? Document
    {
        get => (RichDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Zur Überschrift mit diesem Text scrollen (Inhaltsverzeichnis); <c>false</c>, wenn es keine gibt.</summary>
    public bool BringHeadingIntoView(string text)
    {
        var wanted = text.Trim();
        var heading = _root.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag is string tag && string.Equals(tag, wanted, StringComparison.OrdinalIgnoreCase))
            ?? _root.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag is string tag && tag.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (heading is null)
        {
            return false;
        }

        // „Animationseffekte“ von Windows respektieren
        heading.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled });
        return true;
    }

    private static T Res<T>(string key) => (T)Application.Current.Resources[key];

    private void Render()
    {
        _root.Children.Clear();
        foreach (var block in Document?.Blocks ?? [])
        {
            if (Build(block) is { } element)
            {
                _root.Children.Add(element);
            }
        }
    }

    private static FrameworkElement? Build(ContentBlock block) => block switch
    {
        ParagraphBlock p => BuildParagraph(p.Inlines, p.Centered),
        HeadingBlock h => BuildHeading(h),
        ListBlock l => BuildList(l),
        QuoteBlock q => BuildQuote(q),
        CodeBlock c => BuildCode(c),
        ImageBlock i => BuildImage(i),
        MediaLinkBlock m => BuildMediaLink(m),
        RuleBlock => new Rectangle { Height = 1, Fill = Res<Brush>("RoGLineBrush"), Margin = new Thickness(0, 4, 0, 4) },
        SpoilerBlock s => BuildSpoiler(s),
        TableBlock t => BuildTable(t),
        _ => null,
    };

    private static StackPanel BuildBlocks(IEnumerable<ContentBlock> blocks)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var block in blocks)
        {
            if (Build(block) is { } element)
            {
                panel.Children.Add(element);
            }
        }

        return panel;
    }

    private static RichTextBlock BuildParagraph(IReadOnlyList<ContentInline> inlines, bool centered = false, double fontSize = 14)
    {
        var paragraph = new Paragraph();
        foreach (var inline in inlines)
        {
            paragraph.Inlines.Add(BuildInline(inline));
        }

        var block = new RichTextBlock
        {
            FontFamily = Res<FontFamily>("RoGTextFont"),
            FontSize = fontSize,
            LineHeight = fontSize * 1.6,
            Foreground = Res<Brush>("RoGInkBrush"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
        };
        block.Blocks.Add(paragraph);
        return block;
    }

    private static Inline BuildInline(ContentInline inline)
    {
        switch (inline)
        {
            case LineBreakInline:
                return new LineBreak();
            case LinkInline link:
                var hyperlink = new Hyperlink { Foreground = Res<Brush>("RoGRedBrush") };
                var target = link.Target;
                hyperlink.Click += (_, _) => OpenLink(target);
                foreach (var child in link.Children)
                {
                    hyperlink.Inlines.Add(BuildInline(child));
                }

                ToolTipService.SetToolTip(hyperlink, link.Target.ToString());
                return hyperlink;
            case TextInline text:
                var run = new Run { Text = text.Text };
                if (text.Style.HasFlag(TextStyle.Bold))
                {
                    run.FontWeight = FontWeights.SemiBold;
                }

                if (text.Style.HasFlag(TextStyle.Italic))
                {
                    run.FontStyle = FontStyle.Italic;
                }

                var decorations = TextDecorations.None;
                if (text.Style.HasFlag(TextStyle.Underline))
                {
                    decorations |= TextDecorations.Underline;
                }

                if (text.Style.HasFlag(TextStyle.Strikethrough))
                {
                    decorations |= TextDecorations.Strikethrough;
                }

                run.TextDecorations = decorations;
                if (text.Style.HasFlag(TextStyle.Code))
                {
                    run.FontFamily = new FontFamily("Consolas");
                    run.Foreground = Res<Brush>("RoGGoldBrush");
                }

                if (text.Style.HasFlag(TextStyle.Small))
                {
                    run.FontSize = 12;
                }

                return run;
            default:
                return new Run();
        }
    }

    private static TextBlock BuildHeading(HeadingBlock heading)
    {
        var plain = string.Concat(heading.Inlines.Select(PlainOf));
        var text = new TextBlock
        {
            Text = plain.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            Tag = plain.Trim(),
            Style = Res<Style>(heading.Level <= 2 ? "RoGTitleTextStyle" : "RoGSectionTextStyle"),
            Margin = new Thickness(0, 8, 0, 0),
            IsTextSelectionEnabled = true,
        };
        if (heading.Level >= 3)
        {
            text.Foreground = Res<Brush>("RoGInkBrush");
        }

        AutomationProperties.SetHeadingLevel(text, heading.Level switch
        {
            1 => Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1,
            2 => Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2,
            3 => Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3,
            4 => Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level4,
            5 => Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level5,
            _ => Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level6,
        });
        return text;
    }

    private static string PlainOf(ContentInline inline) => inline switch
    {
        TextInline t => t.Text,
        LinkInline l => string.Concat(l.Children.Select(PlainOf)),
        LineBreakInline => " ",
        _ => string.Empty,
    };

    private static StackPanel BuildList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 6 };
        for (var i = 0; i < list.Items.Count; i++)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var marker = new TextBlock
            {
                Text = list.Ordered ? $"{i + 1}." : "•",
                Foreground = Res<Brush>("RoGRedBrush"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            AutomationProperties.SetAccessibilityView(marker, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            var content = BuildBlocks(list.Items[i]);
            Grid.SetColumn(content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Border BuildQuote(QuoteBlock quote)
    {
        var inner = BuildBlocks(quote.Blocks);
        if (quote.Cite is not null)
        {
            inner.Children.Insert(0, new TextBlock { Text = quote.Cite, Style = Res<Style>("RoGMutedTextStyle"), FontStyle = FontStyle.Italic });
        }

        return new Border
        {
            Background = Res<Brush>("RoGBg3Brush"),
            BorderBrush = Res<Brush>("RoGRedBrush"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Padding = new Thickness(16, 12, 16, 12),
            Child = inner,
        };
    }

    private static Border BuildCode(CodeBlock code) => new()
    {
        Background = Res<Brush>("RoGBg3Brush"),
        BorderBrush = Res<Brush>("RoGLineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = Res<CornerRadius>("RoGControlRadius"),
        Padding = new Thickness(12),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = code.Code,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = Res<Brush>("RoGInkBrush"),
                IsTextSelectionEnabled = true,
            },
        },
    };

    private static FrameworkElement BuildImage(ImageBlock image)
    {
        var picture = new Image
        {
            Source = new BitmapImage(image.Source),
            Stretch = Stretch.Uniform,
            MaxWidth = 720,
            MaxHeight = 540,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(picture, image.Alt ?? string.Empty);
        if (image.Link is null)
        {
            return picture;
        }

        var button = new HyperlinkButton { NavigateUri = image.Link, Content = picture, Padding = new Thickness(0) };
        AutomationProperties.SetName(button, image.Alt ?? image.Link.ToString());
        return button;
    }

    /// <summary>Links der Plattform (Wiki, Forum, News …) in der App öffnen, alles andere im Standardbrowser.</summary>
    private static void OpenLink(Uri target)
    {
        var services = App.Services;
        if (RoG.Core.Navigation.AppRoute.FromWebUrl(target, services.Environment) is { } route && services.Navigate(route))
        {
            return;
        }

        _ = services.Browser.OpenAsync(target);
    }

    private static HyperlinkButton BuildMediaLink(MediaLinkBlock media)
    {
        var localizer = App.Services.Localizer;
        var label = media.Label ?? localizer.GetString(media.Kind switch
        {
            MediaKind.Video => StringKeys.ContentOpenVideo,
            MediaKind.Image => StringKeys.ContentOpenImage,
            _ => StringKeys.ContentOpenEmbed,
        });
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = media.Kind == MediaKind.Video ? "" : media.Kind == MediaKind.Image ? "" : "", FontSize = 16 });
        content.Children.Add(new TextBlock { Text = label });
        var button = new HyperlinkButton { NavigateUri = media.Target, Content = content };
        ToolTipService.SetToolTip(button, media.Target.ToString());
        return button;
    }

    private static Expander BuildSpoiler(SpoilerBlock spoiler) => new()
    {
        Header = string.IsNullOrEmpty(spoiler.Title) ? App.Services.Localizer.GetString(StringKeys.ContentSpoiler) : spoiler.Title,
        Content = BuildBlocks(spoiler.Blocks),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    private static ScrollViewer BuildTable(TableBlock table)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        var columns = table.Rows.Max(r => r.Cells.Count);
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = table.Rows[r];
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = BuildParagraph(row.Cells[c]);
                if (row.IsHeader)
                {
                    cell.FontWeight = FontWeights.SemiBold;
                    cell.Foreground = Res<Brush>("RoGMutedBrush");
                }

                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid,
        };
    }
}
