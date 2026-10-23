using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Konto → Minecraft (Website v0.36.0, Guide 44): eigene Java-/Bedrock-Accounts, Code aus <c>/link</c> einlösen
/// (immer erst Vorschau, dann Bestätigen – auch bei einem Code aus einem Link), trennen. Ältere Geräte-Tokens haben die
/// Scopes <c>minecraft:read</c>/<c>minecraft:write</c> erst nach neuer Anmeldung.
/// </summary>
public sealed partial class MinecraftAccountsViewModel : LoadableViewModel
{
    private readonly MinecraftService _minecraft;

    public MinecraftAccountsViewModel(AppServices services)
        : base(services)
    {
        _minecraft = new MinecraftService(services.Api.Http);
        var me = services.CurrentUser;
        CanRead = me is not null && me.HasScope(MinecraftService.ReadScope);
        CanLink = me is not null && me.HasScope(MinecraftService.WriteScope);
        NeedsSignIn = me is not null && !(CanRead && CanLink);
        CanEditCoinsVisibility = me is not null && me.HasScope("profile:read") && me.HasScope("profile:write");
        CoinsVisibilityOptions = MinecraftService.CoinsVisibilities.Select(v => new SettingsOption(v, L("Coins_Visibility_" + v))).ToList();
        SelectedCoinsVisibility = CoinsVisibilityOptions[0];
        SelectedStatsVisibility = CoinsVisibilityOptions[0];
    }

    public bool CanRead { get; }

    public bool CanLink { get; }

    /// <summary>Token ohne Minecraft-Scopes: Hinweis „einmal ab- und wieder anmelden“.</summary>
    public bool NeedsSignIn { get; }

    public ObservableCollection<MinecraftAccountItemViewModel> Items { get; } = [];

    // --- Rebel-Coins (Website v0.39.0) -----------------------------------------------------------------------------

    public ObservableCollection<CoinTransactionViewModel> Transactions { get; } = [];

    /// <summary>Wallet geladen – auch ohne verknüpften Account (die Wallet bleibt beim Konto).</summary>
    [ObservableProperty]
    public partial bool HasWallet { get; set; }

    [ObservableProperty]
    public partial string BalanceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DailyText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoTransactions))]
    public partial bool HasTransactions { get; set; }

    public bool NoTransactions => !HasTransactions;

    public bool CanEditCoinsVisibility { get; }

    public IReadOnlyList<SettingsOption> CoinsVisibilityOptions { get; }

    [ObservableProperty]
    public partial SettingsOption SelectedCoinsVisibility { get; set; }

    /// <summary>„Minecraft-Statistiken“ im Profil (Statistiken, Bestenlisten, Awards) – gleiche Werte wie bei den Coins.</summary>
    [ObservableProperty]
    public partial SettingsOption SelectedStatsVisibility { get; set; }

    private bool _coinsVisibilityLoaded;

    partial void OnSelectedCoinsVisibilityChanged(SettingsOption value)
    {
        if (_coinsVisibilityLoaded && value is not null)
        {
            _ = SaveVisibilityAsync(() => _minecraft.SetCoinsVisibilityAsync(value.Value), StringKeys.CoinsVisibilitySaved, value.Label);
        }
    }

    partial void OnSelectedStatsVisibilityChanged(SettingsOption value)
    {
        if (_coinsVisibilityLoaded && value is not null)
        {
            _ = SaveVisibilityAsync(() => _minecraft.SetStatsVisibilityAsync(value.Value), StringKeys.StatsVisibilitySaved, value.Label);
        }
    }

    private async Task SaveVisibilityAsync(Func<Task<string>> save, string messageKey, string label)
    {
        try
        {
            await save();
            Show(Format(messageKey, label), InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or ArgumentException)
        {
            Show(ex is ApiProblemException p ? Services.Localizer.GetError(p.Problem.Code) : Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    private async Task LoadWalletAsync(CancellationToken cancellationToken)
    {
        try
        {
            var wallet = await _minecraft.WalletAsync(cancellationToken);
            BalanceText = Format(StringKeys.CoinsBalance, wallet.Balance.ToString("N0", CultureInfo.CurrentCulture));
            DailyText = DailySummary(Services.Localizer, wallet.Daily.Streak, wallet.Daily.BestStreak)
                + " · " + L(wallet.Daily.ClaimedToday ? StringKeys.CoinsClaimedToday : StringKeys.CoinsNotClaimedToday);
            Transactions.Clear();
            foreach (var tx in wallet.Transactions)
            {
                Transactions.Add(new CoinTransactionViewModel(tx, Services.Localizer));
            }

            HasTransactions = Transactions.Count > 0;
            HasWallet = true;
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            // Wallet nicht lesbar (z. B. Server noch ohne v0.39.0) – Bereich bleibt ausgeblendet
            HasWallet = false;
        }

        if (CanEditCoinsVisibility && !_coinsVisibilityLoaded)
        {
            try
            {
                var (coins, stats) = await _minecraft.ProfileVisibilityAsync(cancellationToken);
                SelectedCoinsVisibility = CoinsVisibilityOptions.First(o => o.Value == coins);
                SelectedStatsVisibility = CoinsVisibilityOptions.First(o => o.Value == stats);
                _coinsVisibilityLoaded = true;
            }
            catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
            {
            }
        }
    }

    /// <summary>„Serie: 5 Tage (beste: 12)“ bzw. „Keine laufende Serie (beste: 12)“.</summary>
    internal static string DailySummary(ILocalizer localizer, int streak, int best) =>
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(streak switch { <= 0 => StringKeys.CoinsNoStreak, 1 => StringKeys.CoinsStreakOne, _ => StringKeys.CoinsStreak }), streak, best);

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAccounts { get; set; }

    /// <summary>Website-/App-Nachrichten von Freunden im Spiel anzeigen (<c>/v1/me/minecraft/settings</c>, Standard aus).</summary>
    [ObservableProperty]
    public partial bool ShowWebsiteMessages { get; set; }

    private bool _settingLoaded;

    partial void OnShowWebsiteMessagesChanged(bool value)
    {
        if (_settingLoaded)
        {
            _ = SaveSettingAsync(value);
        }
    }

    private async Task SaveSettingAsync(bool value)
    {
        try
        {
            await _minecraft.SetShowWebsiteMessagesAsync(value);
            Show(L(value ? StringKeys.MinecraftWebsiteMessagesOn : StringKeys.MinecraftWebsiteMessagesOff), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
            Revert(!value);
        }
        catch (System.Text.Json.JsonException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
            Revert(!value);
        }
    }

    private void Revert(bool value)
    {
        _settingLoaded = false;
        ShowWebsiteMessages = value;
        _settingLoaded = true;
    }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    /// <summary>Vorschau bestätigen lassen („bist das du?“) – setzt die Seite; <c>false</c> = Nein.</summary>
    public Func<string, Task<bool>> ConfirmLink { get; set; } = _ => Task.FromResult(false);

    /// <summary>Trennen bestätigen lassen (Titel, Text) – setzt die Seite.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanRead)
        {
            Items.Clear();
            return;
        }

        var list = await _minecraft.MineAsync(cancellationToken);
        Items.Clear();
        foreach (var account in list)
        {
            Items.Add(new MinecraftAccountItemViewModel(account, this, Services.Localizer));
        }

        HasAccounts = Items.Count > 0;
        await LoadWalletAsync(cancellationToken);
        if (HasAccounts && !_settingLoaded)
        {
            try
            {
                ShowWebsiteMessages = await _minecraft.ShowWebsiteMessagesAsync(cancellationToken);
                _settingLoaded = true;
            }
            catch (ApiProblemException)
            {
                // Einstellung nicht lesbar (z. B. Server noch ohne v0.38.0) – Schalter bleibt aus und ohne Wirkung
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }
    }

    /// <summary>Code aus einem Link vorbefüllen – eingelöst wird er erst nach „Code prüfen“ und Bestätigen.</summary>
    public void Prefill(string code)
    {
        Code = code;
        Show(L(StringKeys.MinecraftPrefilled), InfoBarSeverity.Informational);
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (!CanLink || IsBusy)
        {
            return;
        }

        if (!MinecraftService.IsValidCode(Code))
        {
            Show(L(StringKeys.MinecraftCodeFormat), InfoBarSeverity.Warning);
            return;
        }

        var code = MinecraftService.NormalizeCode(Code)!;
        IsBusy = true;
        try
        {
            var preview = await _minecraft.PreviewAsync(code);
            if (!await ConfirmLink(PreviewText(preview)))
            {
                Show(L(StringKeys.MinecraftDeclined), InfoBarSeverity.Informational);
                return;
            }

            var account = await _minecraft.ConfirmAsync(code);
            Code = string.Empty;
            Show(Format(StringKeys.MinecraftLinked, account.Name), InfoBarSeverity.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task UnlinkAsync(MinecraftAccountItemViewModel item)
    {
        if (!CanLink || !await Confirm(L(StringKeys.MinecraftUnlinkTitle), Format(StringKeys.MinecraftUnlinkText, item.Name)))
        {
            return;
        }

        try
        {
            await _minecraft.UnlinkAsync(item.Id);
            Items.Remove(item);
            HasAccounts = Items.Count > 0;
            DataChanged();
            Show(Format(StringKeys.MinecraftUnlinked, item.Name), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    /// <summary>„Code für Notch (Java), angefordert vor 2 Minuten auf RoG Network – bist das du?“</summary>
    private string PreviewText(MinecraftLinkPreview preview)
    {
        var edition = EditionText(Services.Localizer, preview.Edition, preview.Platform);
        var when = preview.MinutesAgo is { } m && m > 0
            ? Format(StringKeys.MinecraftMinutesAgo, m)
            : L(StringKeys.MinecraftJustNow);
        var text = Format(StringKeys.MinecraftPreview, preview.Name, edition, when);
        return preview.Server is { } server ? text + "\n" + Format(StringKeys.MinecraftPreviewServer, server) : text;
    }

    /// <summary>„Java“ bzw. „Bedrock · Xbox“.</summary>
    internal static string EditionText(ILocalizer localizer, string edition, string? platform)
    {
        var text = localizer.GetString("Minecraft_Edition_" + edition);
        return edition == MinecraftService.Bedrock && platform is { } p && p != MinecraftService.Java
            ? text + " · " + localizer.GetString("Minecraft_Platform_" + p)
            : text;
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }

    private string L(string key) => Services.Localizer.GetString(key);
}

public sealed partial class MinecraftAccountItemViewModel(MinecraftAccount account, MinecraftAccountsViewModel owner, ILocalizer localizer)
{
    public string Id => account.Id;

    public string Name => account.Name;

    public string Meta { get; } = string.Join(" · ", new[]
    {
        MinecraftAccountsViewModel.EditionText(localizer, account.Edition, account.Platform),
        account.LinkedAt is { } linked ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftLinkedAt), linked.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) : null,
        account.LastSeenAt is { } seen ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.MinecraftLastSeen), seen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public string Uuid => account.Uuid;

    [RelayCommand]
    private Task UnlinkAsync() => owner.UnlinkAsync(this);
}

/// <summary>Eine Buchung: „+120 ⛃ · Verkauf an den Server-Shop · 2x DIAMOND“ – andere Spieler werden nie genannt.</summary>
public sealed class CoinTransactionViewModel(MinecraftTransaction tx, ILocalizer localizer)
{
    public string Amount { get; } = (tx.IsIncoming ? "+" : tx.IsOutgoing ? "−" : "±") + tx.Amount.ToString("N0", CultureInfo.CurrentCulture) + " ⛃";

    public bool IsIncoming => tx.IsIncoming;

    public string Title { get; } = TypeText(localizer, tx.Type, tx.Source);

    public string Meta { get; } = string.Join(" · ", new[]
    {
        tx.Reason,
        tx.Server,
        localizer.GetString("Coins_Counterparty_" + tx.Counterparty) is { } c && !c.StartsWith("Coins_", StringComparison.Ordinal) ? c : null,
        (tx.PeriodStart ?? tx.CreatedAt)?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Bekannte Typen übersetzt, Berufs-Verdienst mit Beruf („Verdienst: miner“), sonst der Typ selbst.</summary>
    internal static string TypeText(ILocalizer localizer, string type, string? source)
    {
        var text = localizer.GetString("Coins_Type_" + type);
        if (string.IsNullOrEmpty(text) || text.StartsWith("Coins_", StringComparison.Ordinal))
        {
            text = type;
        }

        return type == "earning" && source is { } s ? text + ": " + (s.StartsWith("job:", StringComparison.Ordinal) ? s[4..] : s) : text;
    }
}
