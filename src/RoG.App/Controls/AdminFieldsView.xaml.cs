using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RoG.App.Controls;

public sealed partial class AdminFieldsView : UserControl
{
    public static readonly DependencyProperty FieldsProperty = DependencyProperty.Register(
        nameof(Fields), typeof(object), typeof(AdminFieldsView), new PropertyMetadata(null));

    public AdminFieldsView() => InitializeComponent();

    /// <summary>Liste von <see cref="ViewModels.AdminFieldViewModel"/>.</summary>
    public object? Fields
    {
        get => GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }
}
