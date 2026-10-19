using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using RoG.App.Services;
using RoG.Core.Features.Tools;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Tools (Tool-Baukasten der Website): nach Spiel gruppiert; ein Tool öffnet im eigenen Fenster.</summary>
public sealed partial class ToolsViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly ToolService _tools = new(services.Api.Http);

    public ObservableCollection<ToolGroupViewModel> Groups { get; } = [];

    protected override bool HasData => Groups.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        // Verwaltende sehen auch Entwürfe (wie auf der Website mit filter[status]=all)
        var drafts = Services.CurrentUser is { } me && me.CanAnywhere("tools.manage") && me.HasScope("tools:manage");
        var groups = ToolService.ByGame(await _tools.ListAsync(includeDrafts: drafts, cancellationToken: cancellationToken));
        Groups.Clear();
        foreach (var g in groups)
        {
            Groups.Add(new ToolGroupViewModel(g.Game?.Name ?? Services.Localizer.GetString(StringKeys.ToolsNoGame), g.Tools.Select(t => new ToolItemViewModel(t, Services)).ToList()));
        }
    }

    /// <summary>Link auf ein Tool (<c>/tools/{slug}</c>, <c>rog://tools/{slug}</c>): Fenster öffnen.</summary>
    public void Open(string slug)
    {
        var tool = Groups.SelectMany(g => g.Tools).FirstOrDefault(t => t.Slug == slug);
        Views.ToolWindow.Open(slug, tool?.Name ?? slug, tool?.WebUrl);
    }
}

public sealed class ToolGroupViewModel(string header, IReadOnlyList<ToolItemViewModel> tools)
{
    public string Header => header;

    public IReadOnlyList<ToolItemViewModel> Tools => tools;
}

public sealed partial class ToolItemViewModel(ToolSummary tool, AppServices services)
{
    public string Slug => tool.Slug;

    public string Name => tool.Name;

    public Uri? WebUrl => tool.Url;

    public string Description => tool.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrEmpty(tool.Description);

    /// <summary>Squad, Entwurf – was die Karte zusätzlich sagt.</summary>
    public string Meta { get; } = string.Join(" · ", new[]
    {
        tool.Squad?.Name,
        tool.IsDraft ? services.Localizer.GetString(StringKeys.ToolsDraft) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool HasMeta => Meta.Length > 0;

    [RelayCommand]
    private void Open() => Views.ToolWindow.Open(tool.Slug, tool.Name, tool.Url);
}
