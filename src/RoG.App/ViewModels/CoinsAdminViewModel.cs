using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>
/// Verwaltung → Rebel-Coins (Website v0.39.0, Guide 46): Wallets (Konten und Gäste) mit Buchungen, Gutschrift/Abzug mit
/// Pflicht-Grund, Tagesbonus und Grenzen gegen Missbrauch. Sichtbar mit Scope <c>minecraft:economy</c> und Recht
/// <c>minecraft.economy</c>; Rangschutz, eigene Wallet und Tageslimit je Admin prüft der Server.
/// </summary>
public sealed partial class CoinsAdminViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<string, InfoBarSeverity> _show;
    private readonly MinecraftService _minecraft;
    private string? _cursor;
    private string? _txCursor;
    private bool _loaded;

    public CoinsAdminViewModel(AppServices services, Action<string, InfoBarSeverity> show)
    {
        _services = services;
        _show = show;
        _minecraft = new MinecraftService(services.Api.Http);
        var me = services.CurrentUser;
        CanView = me is not null && me.HasScope(MinecraftService.EconomyScope) && me.Can(MinecraftService.EconomyPermission);
        KindOptions =
        [
            new SettingsOption(string.Empty, L(StringKeys.CoinsAdminKindAll)),
            .. MinecraftService.WalletKinds.Select(k => new SettingsOption(k, L("CoinsAdmin_Kind_" + k))),
        ];
        SelectedKind = KindOptions[0];
        DirectionOptions = [new SettingsOption(MinecraftService.Credit, L(StringKeys.CoinsAdminCredit)), new SettingsOption(MinecraftService.Debit, L(StringKeys.CoinsAdminDebit))];
        SelectedDirection = DirectionOptions[0];
    }

    public bool CanView { get; }

    public ObservableCollection<CoinWalletItemViewModel> Items { get; } = [];

    public IReadOnlyList<SettingsOption> KindOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedKind { get; set; }

    partial void OnSelectedKindChanged(SettingsOption value)
    {
        if (_loaded)
        {
            _ = LoadAsync(reset: true);
        }
    }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>Buchung bestätigen lassen (Titel, Text) – setzt die Seite.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    // --- Einstellungen ---------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool HasSettings { get; set; }

    [ObservableProperty]
    public partial double DailyBase { get; set; }

    [ObservableProperty]
    public partial string Milestones { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double SystemDailyLimit { get; set; }

    [ObservableProperty]
    public partial double KeySystemDailyLimit { get; set; }

    [ObservableProperty]
    public partial double KeyEarningsHourlyLimit { get; set; }

    [ObservableProperty]
    public partial double PayDailyLimit { get; set; }

    [ObservableProperty]
    public partial double AdminDailyLimit { get; set; }

    // --- Gewählte Wallet -------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial CoinWalletItemViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    public ObservableCollection<CoinTransactionViewModel> Transactions { get; } = [];

    [ObservableProperty]
    public partial bool HasMoreTransactions { get; set; }

    [ObservableProperty]
    public partial bool NoTransactions { get; set; }

    public IReadOnlyList<SettingsOption> DirectionOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedDirection { get; set; }

    [ObservableProperty]
    public partial double Amount { get; set; } = double.NaN;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    partial void OnSelectedChanged(CoinWalletItemViewModel? value)
    {
        Transactions.Clear();
        NoTransactions = false;
        HasMoreTransactions = false;
        _txCursor = null;
        Amount = double.NaN;
        Reason = string.Empty;
        if (value is not null)
        {
            _ = LoadTransactionsAsync(reset: true);
        }
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded || !CanView)
        {
            return;
        }

        _loaded = true;
        await Task.WhenAll(LoadAsync(reset: true), LoadSettingsAsync());
    }

    [RelayCommand]
    private Task ReloadAsync() => Task.WhenAll(LoadAsync(reset: true), LoadSettingsAsync());

    [RelayCommand]
    private Task SearchNowAsync() => LoadAsync(reset: true);

    [RelayCommand]
    private Task MoreAsync() => LoadAsync(reset: false);

    [RelayCommand]
    private Task MoreTransactionsAsync() => LoadTransactionsAsync(reset: false);

    private async Task LoadAsync(bool reset)
    {
        if (!CanView || IsBusy || (!reset && _cursor is null))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var kind = string.IsNullOrEmpty(SelectedKind.Value) ? null : SelectedKind.Value;
            var page = await _minecraft.WalletsAsync(Search, kind, reset ? null : _cursor);
            if (reset)
            {
                Selected = null;
                Items.Clear();
            }

            foreach (var wallet in page.Items)
            {
                Items.Add(new CoinWalletItemViewModel(wallet, this, _services.Localizer));
            }

            _cursor = page.NextCursor;
            HasMore = page.HasMore;
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadTransactionsAsync(bool reset)
    {
        if (Selected is not { } wallet || (!reset && _txCursor is null))
        {
            return;
        }

        try
        {
            var page = await _minecraft.WalletTransactionsAsync(wallet.Id, reset ? null : _txCursor);
            if (Selected != wallet)
            {
                return; // inzwischen andere Wallet gewählt
            }

            if (reset)
            {
                Transactions.Clear();
            }

            foreach (var tx in page.Items)
            {
                Transactions.Add(new CoinTransactionViewModel(tx, _services.Localizer));
            }

            _txCursor = page.NextCursor;
            HasMoreTransactions = page.HasMore;
            NoTransactions = Transactions.Count == 0;
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or ArgumentException)
        {
            ShowError(ex);
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            Apply(await _minecraft.EconomyAsync());
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            ShowError(ex);
        }
    }

    private void Apply(EconomySettings s)
    {
        DailyBase = s.DailyBase;
        Milestones = MinecraftService.FormatMilestones(s.Milestones);
        SystemDailyLimit = s.SystemDailyLimit;
        KeySystemDailyLimit = s.KeySystemDailyLimit;
        KeyEarningsHourlyLimit = s.KeyEarningsHourlyLimit;
        PayDailyLimit = s.PayDailyLimit;
        AdminDailyLimit = s.AdminDailyLimit;
        HasSettings = true;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!HasSettings)
        {
            return;
        }

        if (MinecraftService.ParseMilestones(Milestones) is not { } milestones)
        {
            _show(L(StringKeys.CoinsAdminMilestonesFormat), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            Apply(await _minecraft.SaveEconomyAsync(new EconomySettings(
                ToInt(DailyBase), milestones, ToInt(SystemDailyLimit), ToInt(KeySystemDailyLimit), ToInt(KeyEarningsHourlyLimit), ToInt(PayDailyLimit), ToInt(AdminDailyLimit))));
            _show(L(StringKeys.CoinsAdminSaved), InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task AdjustAsync()
    {
        if (Selected is not { } wallet)
        {
            return;
        }

        var amount = ToInt(Amount);
        if (!MinecraftService.IsValidAmount(amount))
        {
            _show(L(StringKeys.CoinsAdminAmountRange), InfoBarSeverity.Warning);
            return;
        }

        if (!MinecraftService.IsValidReason(Reason))
        {
            _show(L(StringKeys.CoinsAdminReasonRequired), InfoBarSeverity.Warning);
            return;
        }

        var direction = SelectedDirection.Value;
        var text = string.Format(CultureInfo.CurrentCulture, L(StringKeys.CoinsAdminConfirmText), SelectedDirection.Label, amount.ToString("N0", CultureInfo.CurrentCulture), wallet.Title);
        if (!await Confirm(L(StringKeys.CoinsAdminConfirmTitle), text))
        {
            return;
        }

        try
        {
            var balance = await _minecraft.AdjustAsync(wallet.Id, direction, amount, Reason);
            wallet.Balance = balance;
            Amount = double.NaN;
            Reason = string.Empty;
            _show(string.Format(CultureInfo.CurrentCulture, L(StringKeys.CoinsAdminAdjusted), balance.ToString("N0", CultureInfo.CurrentCulture)), InfoBarSeverity.Success);
            await LoadTransactionsAsync(reset: true);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or ArgumentException)
        {
            ShowError(ex);
        }
    }

    internal void OpenProfile(string slug) => _services.Navigate(new AppRoute(AppSection.Members, slug));

    private void ShowError(Exception ex) =>
        _show(ex is ApiProblemException p ? _services.Localizer.GetError(p.Problem.Code) : _services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);

    private static int ToInt(double value) => double.IsNaN(value) ? 0 : (int)Math.Clamp(Math.Round(value), 0, int.MaxValue);

    private string L(string key) => _services.Localizer.GetString(key);
}

/// <summary>Eine Wallet in der Liste: Spieler- bzw. Kontoname, Art, verknüpfte Accounts, Kontostand, Serie.</summary>
public sealed partial class CoinWalletItemViewModel : ObservableObject
{
    private readonly AdminWallet _wallet;
    private readonly CoinsAdminViewModel _owner;

    public CoinWalletItemViewModel(AdminWallet wallet, CoinsAdminViewModel owner, ILocalizer localizer)
    {
        _wallet = wallet;
        _owner = owner;
        Balance = wallet.Balance;
        Title = wallet.UserName ?? wallet.Name ?? localizer.GetString(StringKeys.CoinsAdminUnknownName);
        Meta = string.Join(" · ", new[]
        {
            localizer.GetString("CoinsAdmin_Kind_" + wallet.Kind),
            wallet.Accounts.Count > 0 ? string.Join(", ", wallet.Accounts.Select(a => a.Name)) : null,
            MinecraftAccountsViewModel.DailySummary(localizer, wallet.Daily.Streak, wallet.Daily.BestStreak),
            wallet.LastActivityAt is { } at ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.CoinsAdminLastActivity), at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
    }

    public string Id => _wallet.Id;

    public string Title { get; }

    public string Meta { get; }

    /// <summary>UUID nur bei Gast-Wallets.</summary>
    public string? Uuid => _wallet.Uuid;

    public bool HasUuid => _wallet.Uuid is not null;

    public bool HasProfile => _wallet.UserSlug is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceText))]
    public partial int Balance { get; set; }

    public string BalanceText => Balance.ToString("N0", CultureInfo.CurrentCulture) + " ⛃";

    [RelayCommand]
    private void OpenProfile()
    {
        if (_wallet.UserSlug is { } slug)
        {
            _owner.OpenProfile(slug);
        }
    }
}
