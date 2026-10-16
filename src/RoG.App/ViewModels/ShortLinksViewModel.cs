using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Links;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Kurzlinks: anlegen (Ziel, optional Wunsch-Code, Titel), Liste mit Klicks, kopieren.</summary>
public sealed partial class ShortLinksViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly ShortLinkService _links = new(services.Api.Client);

    public ObservableCollection<ShortLinkItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Target { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Code { get; set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (await Services.Tokens.GetAsync(cancellationToken) is null)
        {
            Items.Clear();
            return;
        }

        var page = await _links.ListAsync(null, cancellationToken);
        Items.Clear();
        foreach (var link in page.Items)
        {
            Items.Add(new ShortLinkItemViewModel(link, this, Services.Localizer));
        }
    }

    private bool CanCreate() => Uri.TryCreate(Target.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        try
        {
            var link = await _links.CreateAsync(new Uri(Target.Trim()), Code, Title);
            Items.Insert(0, new ShortLinkItemViewModel(link, this, Services.Localizer));
            DataChanged();
            Target = string.Empty;
            Code = null;
            Title = null;
            var copied = link.ShortUrl is { } url && TryCopy(url.ToString());
            Show(Services.Localizer.GetString(copied ? StringKeys.LinksCreatedCopied : StringKeys.LinksCreated), InfoBarSeverity.Success);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    internal bool TryCopy(string text)
    {
        try
        {
            Services.Clipboard.SetText(text);
            return true;
        }
#pragma warning disable CA1031 // Zwischenablage kann gesperrt sein
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    internal void Copied(bool ok) =>
        Show(Services.Localizer.GetString(ok ? StringKeys.CommonLinkCopied : StringKeys.CommonCopyFailed), ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

public sealed partial class ShortLinkItemViewModel(ShortLink link, ShortLinksViewModel owner, ILocalizer localizer)
{
    public string ShortUrl => link.ShortUrl?.ToString() ?? link.Code;

    public string Target => link.Title is null ? link.Target?.ToString() ?? string.Empty : $"{link.Title} – {link.Target}";

    public string Meta { get; } = string.Join(" · ", new[]
    {
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(StringKeys.LinksClicks), link.Clicks),
        link.Blocked ? localizer.GetString(StringKeys.LinksBlocked) : link.Active ? null : localizer.GetString(StringKeys.LinksInactive),
        link.CreatedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
    }.Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private void Copy() => owner.Copied(owner.TryCopy(ShortUrl));
}
