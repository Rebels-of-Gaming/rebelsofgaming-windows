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
    }

    public bool CanRead { get; }

    public bool CanLink { get; }

    /// <summary>Token ohne Minecraft-Scopes: Hinweis „einmal ab- und wieder anmelden“.</summary>
    public bool NeedsSignIn { get; }

    public ObservableCollection<MinecraftAccountItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

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
