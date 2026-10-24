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
/// Server → Minecraft (Website v0.43.0): „So kommst du auf den Server“ für Java, Bedrock und Konsole mit Kopier-Knöpfen –
/// auch im Gastmodus. Mit Recht <c>minecraft.manage</c> und Scope <c>minecraft:manage</c> lassen sich die Beitrittsdaten
/// ändern; das Formular lädt erst beim Aufklappen.
/// </summary>
public sealed partial class MinecraftJoinViewModel(AppServices services) : ObservableObject
{
    private readonly MinecraftService _minecraft = new(services.Api.Http);
    private bool _loaded;
    private bool _editLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJoin))]
    public partial MinecraftJoin? Join { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public bool HasJoin => Join is not null;

    public string JavaAddress => Join?.JavaText ?? string.Empty;

    public string BedrockAddress => Join?.Bedrock.Address ?? string.Empty;

    public string BedrockPort => Join?.Bedrock.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public bool ShowConsole => Join?.ConsoleEnabled == true && !string.IsNullOrEmpty(Join.Gamertag);

    public string Gamertag => Join?.Gamertag ?? string.Empty;

    public bool HasHint => ShowConsole && !string.IsNullOrEmpty(Join?.Hint);

    public string Hint => Join?.Hint ?? string.Empty;

    /// <summary>Hinweis „25565 ist Standard“ nur, wenn der Standard-Port gilt (dann steht kein Port in der Adresse).</summary>
    public bool ShowDefaultPortNote => Join is { } j && !MinecraftService.ShowJavaPort(j.Java.Port);

    /// <summary>„Konto verknüpfen“ nur angemeldet (im Gastmodus gibt es kein Konto).</summary>
    public bool IsSignedIn => services.CurrentUser is not null;

    public bool CanEdit => services.CurrentUser is { } me && me.Can(MinecraftService.ManagePermission) && me.HasScope(MinecraftService.ManageScope);

    // --- Formular ---------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsEditLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsSaving { get; set; }

    /// <summary>Speichern erst, wenn das Formular mit den gespeicherten Werten gefüllt ist.</summary>
    public bool CanSave => _editLoaded && !IsEditLoading && !IsSaving;

    [ObservableProperty]
    public partial string EditJavaAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double EditJavaPort { get; set; } = MinecraftService.DefaultJavaPort;

    [ObservableProperty]
    public partial string EditBedrockAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double EditBedrockPort { get; set; } = MinecraftService.DefaultBedrockPort;

    [ObservableProperty]
    public partial bool EditConsoleEnabled { get; set; }

    [ObservableProperty]
    public partial string EditGamertag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditHintDe { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditHintEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditHintFr { get; set; } = string.Empty;

    public async Task LoadAsync(bool force = false)
    {
        if (IsLoading || (_loaded && !force))
        {
            return;
        }

        IsLoading = true;
        try
        {
            Apply(await _minecraft.JoinAsync());
            ErrorMessage = Join is null ? services.Localizer.GetError(ApiProblem.Codes.Unknown) : null;
            _loaded = Join is not null;
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            // Bei erneutem Laden die letzten Daten stehen lassen
            ErrorMessage = Join is null ? ErrorText(ex) : null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(force: true);

    /// <summary>Formular füllen (einmal, beim Aufklappen) – mit den Hinweisen aller Sprachen.</summary>
    public async Task LoadEditAsync()
    {
        if (!CanEdit || _editLoaded || IsEditLoading)
        {
            return;
        }

        IsEditLoading = true;
        try
        {
            if (await _minecraft.JoinForEditAsync() is { } join)
            {
                Apply(join);
                Fill(join);
                _editLoaded = true;
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            ShowFeedback(ErrorText(ex), InfoBarSeverity.Error);
        }
        finally
        {
            IsEditLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanEdit || !_editLoaded || IsSaving)
        {
            return;
        }

        if (Invalid() is { } invalid)
        {
            ShowFeedback(services.Localizer.GetString(invalid), InfoBarSeverity.Warning);
            return;
        }

        IsSaving = true;
        try
        {
            var update = new MinecraftJoinUpdate(
                EditJavaAddress,
                (int)EditJavaPort,
                EditBedrockAddress,
                (int)EditBedrockPort,
                EditConsoleEnabled,
                EditConsoleEnabled || !string.IsNullOrWhiteSpace(EditGamertag) ? EditGamertag : null,
                new Dictionary<string, string?> { ["de"] = EditHintDe, ["en"] = EditHintEn, ["fr"] = EditHintFr });
            if (await _minecraft.UpdateJoinAsync(update) is { } join)
            {
                Apply(join);
                Fill(join);
            }

            ShowFeedback(services.Localizer.GetString(StringKeys.MinecraftJoinSaved), InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException)
        {
            ShowFeedback(ErrorText(ex), InfoBarSeverity.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void CopyJava() => Copy(JavaAddress);

    [RelayCommand]
    private void CopyBedrockAddress() => Copy(BedrockAddress);

    [RelayCommand]
    private void CopyBedrockPort() => Copy(BedrockPort);

    [RelayCommand]
    private void CopyGamertag() => Copy(Gamertag);

    private string? Invalid()
    {
        if (!MinecraftService.IsValidAddress(EditJavaAddress) || !MinecraftService.IsValidAddress(EditBedrockAddress))
        {
            return StringKeys.MinecraftJoinInvalidAddress;
        }

        if (double.IsNaN(EditJavaPort) || double.IsNaN(EditBedrockPort)
            || !MinecraftService.IsValidPort((int)EditJavaPort) || !MinecraftService.IsValidPort((int)EditBedrockPort))
        {
            return StringKeys.MinecraftJoinInvalidPort;
        }

        return (EditConsoleEnabled || !string.IsNullOrWhiteSpace(EditGamertag)) && !MinecraftService.IsValidGamertag(EditGamertag)
            ? StringKeys.MinecraftJoinInvalidGamertag
            : null;
    }

    private void Apply(MinecraftJoin? join)
    {
        if (join is null)
        {
            return;
        }

        Join = join;
        OnPropertyChanged(nameof(JavaAddress));
        OnPropertyChanged(nameof(BedrockAddress));
        OnPropertyChanged(nameof(BedrockPort));
        OnPropertyChanged(nameof(ShowConsole));
        OnPropertyChanged(nameof(Gamertag));
        OnPropertyChanged(nameof(HasHint));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(ShowDefaultPortNote));
    }

    private void Fill(MinecraftJoin join)
    {
        EditJavaAddress = join.Java.Address;
        EditJavaPort = join.Java.Port;
        EditBedrockAddress = join.Bedrock.Address;
        EditBedrockPort = join.Bedrock.Port;
        EditConsoleEnabled = join.ConsoleEnabled;
        // Bei ausgeblendeter Konsole liefert die API keinen Gamertag – Feld bleibt dann wie zuletzt
        if (join.Gamertag is { } gamertag)
        {
            EditGamertag = gamertag;
        }

        if (join.Hints is { } hints)
        {
            EditHintDe = hints.GetValueOrDefault("de") ?? string.Empty;
            EditHintEn = hints.GetValueOrDefault("en") ?? string.Empty;
            EditHintFr = hints.GetValueOrDefault("fr") ?? string.Empty;
        }
    }

    private void Copy(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var ok = services.ServerConnector.Copy(text);
        ShowFeedback(
            string.Format(CultureInfo.CurrentCulture, services.Localizer.GetString(ok ? StringKeys.ServersCopied : StringKeys.ServersCopyFailed), text),
            ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private string ErrorText(Exception ex) =>
        services.Localizer.GetError(ex is ApiProblemException p ? p.Problem.Code : ApiProblem.Codes.Unknown);

    private void ShowFeedback(string message, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = message;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}
