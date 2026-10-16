using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoG.App.ViewModels;

namespace RoG.App.Controls;

public sealed partial class ServerCard : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(ServerItemViewModel), typeof(ServerCard), new PropertyMetadata(null));

    public ServerCard() => InitializeComponent();

    public ServerItemViewModel? Item
    {
        get => (ServerItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
}
