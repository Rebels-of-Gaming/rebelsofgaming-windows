using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Features.Shop;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Shop &amp; Spenden (Website v0.47.0): Unterstützer-Ränge und Spenden nur anzeigen, eigene Bestellungen mit Quittung.
/// Gekauft und gespendet wird auf der Website – die App verlinkt nur (Microsoft-Store-Regeln für digitale Güter,
/// Entscheidung des Owners steht aus).
/// </summary>
public sealed partial class ShopViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly ShopService _shop = new(services.Api.Http);
    private string? _ordersCursor;

    public ObservableCollection<ShopProductViewModel> Products { get; } = [];

    public ObservableCollection<ShopOrderViewModel> Orders { get; } = [];

    public ObservableCollection<string> Supporters { get; } = [];

    [ObservableProperty]
    public partial bool IsClosed { get; set; }

    [ObservableProperty]
    public partial bool HasDonations { get; set; }

    [ObservableProperty]
    public partial string DonationRange { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSupporters { get; set; }

    [ObservableProperty]
    public partial bool HasOrders { get; set; }

    [ObservableProperty]
    public partial bool HasMoreOrders { get; set; }

    /// <summary>Bestellungen nur angemeldet mit Scope <c>shop:read</c>.</summary>
    public bool CanSeeOrders => Services.CurrentUser is { } me && me.HasScope(ShopService.ReadScope);

    protected override bool HasData => Products.Count > 0 || HasDonations || IsClosed || HasOrders;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var shop = await _shop.OverviewAsync(cancellationToken);
        IsClosed = !shop.Enabled && shop.Donations is null;
        Products.Clear();
        foreach (var p in shop.Products)
        {
            Products.Add(new ShopProductViewModel(p, this));
        }

        HasDonations = shop.Donations is not null;
        Supporters.Clear();
        if (shop.Donations is { } d)
        {
            DonationRange = Format(StringKeys.ShopDonationRange, Money(d.Min, d.Currency), Money(d.Max, d.Currency));
            foreach (var (name, month) in d.Supporters)
            {
                Supporters.Add(name + (month.Length > 0 ? " · " + MonthText(month) : string.Empty));
            }
        }

        HasSupporters = Supporters.Count > 0;

        Orders.Clear();
        _ordersCursor = null;
        if (CanSeeOrders)
        {
            await LoadOrdersAsync(cancellationToken);
        }

        HasOrders = Orders.Count > 0;
    }

    [RelayCommand]
    private Task MoreOrdersAsync() => LoadOrdersAsync(CancellationToken.None);

    private async Task LoadOrdersAsync(CancellationToken cancellationToken)
    {
        var page = await _shop.OrdersAsync(_ordersCursor, cancellationToken);
        foreach (var o in page.Items)
        {
            Orders.Add(new ShopOrderViewModel(o, this));
        }

        _ordersCursor = page.NextCursor;
        HasMoreOrders = _ordersCursor is not null;
        HasOrders = Orders.Count > 0;
    }

    [RelayCommand]
    private Task DonateAsync() => Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "donate"));

    [RelayCommand]
    private Task OpenShopAsync() => Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "shop"));

    internal Task OpenProductAsync(ShopProduct product) =>
        Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "shop/" + Uri.EscapeDataString(product.Slug)));

    internal Task OpenReceiptAsync(ShopOrder order) =>
        order.ReceiptUrl is { } url ? Services.Browser.OpenAsync(url) : Task.CompletedTask;

    internal string L(string key) => Services.Localizer.GetString(key);

    internal string F(string key, params object?[] args) => Format(key, args);

    /// <summary>Cent → „4,99 €“ in der App-Sprache.</summary>
    internal static string Money(long cents, string currency)
    {
        var value = cents / 100m;
        try
        {
            var region = CultureInfo.CurrentCulture;
            return string.Format(region, "{0:N2} {1}", value, currency == "EUR" ? "€" : currency);
        }
        catch (FormatException)
        {
            return value.ToString(CultureInfo.InvariantCulture) + " " + currency;
        }
    }

    private static string MonthText(string month) =>
        DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
            : month;
}

public sealed partial class ShopProductViewModel(ShopProduct product, ShopViewModel owner)
{
    public string Name => product.Name;

    public string Price => product.Price.Formatted.Length > 0 ? product.Price.Formatted : ShopViewModel.Money(product.Price.Amount, product.Price.Currency);

    public string Meta => string.Join(" · ", new[]
    {
        product.RoleLabel,
        product.Permanent || product.DurationDays is null
            ? owner.L(StringKeys.ShopPermanent)
            : owner.F(StringKeys.ShopDuration, product.DurationDays),
        product.Providers.Count > 0 ? string.Join(", ", product.Providers) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string? Description => product.Description;

    public bool HasDescription => !string.IsNullOrEmpty(product.Description);

    [RelayCommand]
    private Task OpenAsync() => owner.OpenProductAsync(product);
}

public sealed partial class ShopOrderViewModel(ShopOrder order, ShopViewModel owner)
{
    public string Title => order.ItemName.Length > 0 ? order.ItemName : owner.L(order.Kind == ShopService.KindDonation ? StringKeys.ShopDonation : StringKeys.ShopProduct);

    public string Price => order.Price.Formatted;

    public string Status
    {
        get
        {
            var key = "Shop_Status_" + order.Status;
            var text = owner.L(key);
            return string.IsNullOrEmpty(text) || text == key ? order.Status : text;
        }
    }

    public string Meta => string.Join(" · ", new[]
    {
        order.Number,
        (order.PaidAt ?? order.CreatedAt)?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
        order.Permanent ? owner.L(StringKeys.ShopPermanent)
            : order.ExpiresAt is { } until ? owner.F(StringKeys.ShopValidUntil, until.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
        order.Sandbox ? owner.L(StringKeys.ShopSandbox) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool HasReceipt => order.ReceiptUrl is not null;

    [RelayCommand]
    private Task OpenReceiptAsync() => owner.OpenReceiptAsync(order);
}
