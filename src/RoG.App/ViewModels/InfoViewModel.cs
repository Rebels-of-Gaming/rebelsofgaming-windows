using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Content;
using RoG.Core.Features.Info;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Info: Regeln (mit Bestätigung), Seiten (Über uns, Partner …), Rechtstexte.</summary>
public sealed partial class InfoViewModel : LoadableViewModel
{
    private readonly InfoService _info;

    public InfoViewModel(AppServices services)
        : base(services)
    {
        _info = new InfoService(services.Api.Client, services.Environment.WebBaseUrl);
        Detail = new InfoDetailViewModel(services, _info);
        Detail.Accepted += (_, key) => _ = LoadAsync(silent: true);
    }

    public ObservableCollection<InfoItemViewModel> Items { get; } = [];

    public InfoDetailViewModel Detail { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial InfoItemViewModel? Selected { get; set; }

    public bool IsDetailOpen => Selected is not null;

    public bool ShowSelectHint => Selected is null;

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var entries = await _info.ListAsync(cancellationToken);
        var selected = Selected?.Entry;
        Items.Clear();
        InfoKind? kind = null;
        foreach (var entry in entries)
        {
            Items.Add(new InfoItemViewModel(entry, entry.Kind != kind ? GroupName(entry.Kind) : null, Services.Localizer));
            kind = entry.Kind;
        }

        if (selected is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Entry.Kind == selected.Kind && i.Entry.Key == selected.Key);
        }
    }

    private string GroupName(InfoKind kind) => Services.Localizer.GetString(kind switch
    {
        InfoKind.Rules => StringKeys.InfoGroupRules,
        InfoKind.Page => StringKeys.InfoGroupPages,
        _ => StringKeys.InfoGroupLegal,
    }).ToUpper(System.Globalization.CultureInfo.CurrentCulture);

    partial void OnSelectedChanged(InfoItemViewModel? value)
    {
        if (value is not null)
        {
            _ = Detail.ShowAsync(value.Entry);
        }
    }
}

public sealed class InfoItemViewModel(InfoEntry entry, string? groupHeader, ILocalizer localizer)
{
    public InfoEntry Entry => entry;

    public string Title => entry.Title;

    public string Subtitle => entry.Subtitle ?? string.Empty;

    public bool HasSubtitle => entry.Subtitle is not null;

    public string GroupHeader => groupHeader ?? string.Empty;

    public bool ShowGroupHeader => groupHeader is not null;

    public bool NeedsAcceptance => entry.NeedsAcceptance;

    public string AcceptanceText { get; } = localizer.GetString(StringKeys.InfoNeedsAcceptance);
}

public sealed partial class InfoDetailViewModel(AppServices services, InfoService info) : LoadableViewModel(services)
{
    private InfoEntry? _entry;
    private InfoDocument? _document;

    public event EventHandler<string>? Accepted;

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Intro { get; set; }

    [ObservableProperty]
    public partial string? Meta { get; set; }

    [ObservableProperty]
    public partial bool CanAccept { get; set; }

    [ObservableProperty]
    public partial bool HasWebUrl { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    public ObservableCollection<InfoSectionViewModel> Sections { get; } = [];

    protected override bool HasData => _document is not null;

    public Task ShowAsync(InfoEntry entry)
    {
        _entry = entry;
        _document = null;
        Sections.Clear();
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_entry is null)
        {
            return;
        }

        var doc = await info.GetAsync(_entry, cancellationToken);
        Title = doc.Title;
        Intro = doc.Intro;
        Meta = doc.Meta;
        HasWebUrl = doc.WebUrl is not null;
        CanAccept = doc.CanAccept && await Services.Tokens.GetAsync(cancellationToken) is not null;
        Sections.Clear();
        foreach (var section in doc.Sections)
        {
            Sections.Add(new InfoSectionViewModel(section));
        }

        _document = doc;
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (_entry is not { Kind: InfoKind.Rules } entry)
        {
            return;
        }

        try
        {
            await info.AcceptRulesAsync(entry.Key);
            CanAccept = false;
            Show(Services.Localizer.GetString(StringKeys.InfoAccepted), InfoBarSeverity.Success);
            Accepted?.Invoke(this, entry.Key);
        }
        catch (ApiProblemException ex)
        {
            Show(Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (RoG.Core.Api.Generated.GeneratedApiException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private Task OpenInBrowserAsync() => _document?.WebUrl is { } url ? Services.Browser.OpenAsync(url) : Task.CompletedTask;

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

public sealed class InfoSectionViewModel(InfoSection section)
{
    public string Title => section.Title ?? string.Empty;

    public bool HasTitle => section.Title is not null;

    public RichDocument Content => section.Content;

    public Brush TitleBrush { get; } = (Brush)Application.Current.Resources[section.Important ? "RoGRedBrush" : "RoGInkBrush"];
}
