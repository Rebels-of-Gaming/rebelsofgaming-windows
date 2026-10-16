using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Features.Search;
using RoG.Core.Localization;
using RoG.Core.Navigation;

namespace RoG.App.ViewModels;

/// <summary>Suchergebnisse (Strg+K): Gruppen nach Bereich; ein Treffer öffnet den Bereich in der App, sonst den Browser.</summary>
public sealed partial class SearchViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly SearchService _search = new(services.Api.Client);

    public ObservableCollection<SearchRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Header { get; set; }

    protected override bool HasData => Rows.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        Header = Format(StringKeys.SearchHeader, Query);
        var groups = await _search.SearchAsync(Query, cancellationToken);
        Rows.Clear();
        foreach (var group in groups)
        {
            Rows.Add(SearchRowViewModel.Group(group.Label));
            foreach (var hit in group.Hits)
            {
                Rows.Add(SearchRowViewModel.Hit(hit, Open));
            }
        }
    }

    private void Open(SearchHit hit)
    {
        if (hit.Url is null)
        {
            return;
        }

        if (AppRoute.FromWebUrl(hit.Url, Services.Environment) is not { } route || !Services.Navigate(route))
        {
            _ = Services.Browser.OpenAsync(hit.Url);
        }
    }
}

/// <summary>Zeile: Gruppenüberschrift oder Treffer (anklickbar).</summary>
public sealed partial class SearchRowViewModel
{
    private SearchHit? _hit;
    private Action<SearchHit>? _open;

    public required string Title { get; init; }

    public string Excerpt { get; init; } = string.Empty;

    public string Meta { get; init; } = string.Empty;

    public bool IsGroup { get; init; }

    public bool IsHit => !IsGroup;

    public bool HasExcerpt => Excerpt.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public static SearchRowViewModel Group(string label) => new() { Title = label.ToUpper(System.Globalization.CultureInfo.CurrentCulture), IsGroup = true };

    public static SearchRowViewModel Hit(SearchHit hit, Action<SearchHit> open) => new()
    {
        Title = hit.Title,
        Excerpt = hit.Excerpt ?? string.Empty,
        Meta = hit.Meta ?? string.Empty,
        _hit = hit,
        _open = open,
    };

    [RelayCommand]
    private void Open()
    {
        if (_hit is not null)
        {
            _open?.Invoke(_hit);
        }
    }
}
