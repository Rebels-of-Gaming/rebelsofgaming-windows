using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;
using RoG.Core.Features.Admin;

namespace RoG.App.Controls;

/// <summary>Wählt die Vorlage eines Verwaltungs-Formularfelds nach seiner Art.</summary>
public sealed partial class AdminFieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }

    public DataTemplate? Toggle { get; set; }

    public DataTemplate? Choice { get; set; }

    public DataTemplate? Translated { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is AdminFieldViewModel f
        ? f.Field.Type switch
        {
            AdminFieldType.Bool => Toggle,
            AdminFieldType.Choice or AdminFieldType.Reference => Choice,
            AdminFieldType.Translated or AdminFieldType.TranslatedMultiline => Translated,
            _ => Text,
        }
        : Text;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
