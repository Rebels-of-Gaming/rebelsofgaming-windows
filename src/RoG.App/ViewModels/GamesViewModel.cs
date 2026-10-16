using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Features.Games;
using RoG.Core.Features.Squads;

namespace RoG.App.ViewModels;

/// <summary>Spiele des Clans mit Store-, Ref- und Einladungslinks (Umschalter auf der Squads-Seite).</summary>
public sealed partial class GamesViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly GameService _games = new(services.Api.Client);

    public ObservableCollection<GameItemViewModel> Items { get; } = [];

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var games = await _games.ListAsync(cancellationToken);
        Items.Clear();
        foreach (var g in games)
        {
            Items.Add(new GameItemViewModel(g, Services));
        }
    }
}

public sealed class GameItemViewModel(GameInfo game, AppServices services)
{
    public string Title { get; } = string.IsNullOrEmpty(game.Emoji) ? game.Name : $"{game.Emoji} {game.Name}";

    public string Meta { get; } = string.Join(" · ", new[] { game.Genre, game.Platform, game.ClanTag }.Where(s => !string.IsNullOrEmpty(s)));

    public IReadOnlyList<GameLinkViewModel> Links { get; } = game.Links.Select(l => new GameLinkViewModel(l, services)).ToList();

    public bool HasLinks => game.Links.Count > 0;
}

public sealed partial class GameLinkViewModel(SquadLink link, AppServices services)
{
    public string Label => link.Code is { } code ? $"{link.Label} ({code})" : link.Label;

    [RelayCommand]
    private Task OpenAsync() => services.Browser.OpenAsync(link.Url);
}
