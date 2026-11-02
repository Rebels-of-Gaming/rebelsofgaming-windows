using System.Collections.ObjectModel;
using System.Globalization;
using RoG.App.Services;
using RoG.Core.Features.Minecraft;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>
/// Minecraft-Secrets (Website v0.55.0): alle Secrets je Gruppe mit Hinweis und Anzahl der Finder; angemeldet zusätzlich
/// die eigenen Funde. Fundorte gibt es nicht – ungefundene Secrets zeigen nur den Hinweis.
/// </summary>
public sealed partial class MinecraftSecretsViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly MinecraftService _minecraft = new(services.Api.Http);

    public ObservableCollection<SecretGroupViewModel> Groups { get; } = [];

    protected override bool HasData => Groups.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var groups = await _minecraft.SecretsAsync(cancellationToken);
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(new SecretGroupViewModel(group, Services.Localizer));
        }
    }
}

public sealed class SecretGroupViewModel(SecretGroup group, ILocalizer localizer)
{
    public string Title => group.Label.ToUpper(CultureInfo.CurrentCulture);

    /// <summary>„5 von 7 gefunden“ (angemeldet) bzw. „7 Secrets“.</summary>
    public string Progress => group.Found is { } found
        ? string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.SecretsFoundOf), found, group.Total)
        : string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.SecretsTotal), group.Total);

    public IReadOnlyList<SecretEntryViewModel> Secrets { get; } = group.Secrets.Select(s => new SecretEntryViewModel(s, localizer)).ToList();
}

public sealed class SecretEntryViewModel(SecretEntry secret, ILocalizer localizer)
{
    public string Name => secret.Name;

    public string? Hint => secret.Hint;

    public bool HasHint => !string.IsNullOrEmpty(secret.Hint);

    /// <summary>„12 haben es gefunden“ und – angemeldet – „von dir gefunden am …“.</summary>
    public string Meta
    {
        get
        {
            var text = string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.SecretsFinders), secret.Finders);
            return secret.FoundAt is { } at
                ? text + " · " + string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.SecretsFoundAt), at.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
                : text;
        }
    }

    public bool IsFound => secret.FoundAt is not null;

    public string Glyph => IsFound ? "" : "";

    public string AutomationName => Name + ", " + Meta + (HasHint ? ", " + Hint : string.Empty);
}
