using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RoG.App.Services;
using RoG.Core.Api;
using RoG.Core.Features.Absences;
using RoG.Core.Localization;

namespace RoG.App.ViewModels;

/// <summary>Eigene Abwesenheiten (Konto-Seite): melden, Liste, löschen.</summary>
public sealed partial class AbsencesViewModel(AppServices services) : LoadableViewModel(services)
{
    private readonly AbsenceService _absences = new(services.Api.Client);

    public ObservableCollection<AbsenceItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial DateTimeOffset? From { get; set; } = DateTimeOffset.Now.Date;

    [ObservableProperty]
    public partial DateTimeOffset? To { get; set; } = DateTimeOffset.Now.Date.AddDays(7);

    [ObservableProperty]
    public partial string? Reason { get; set; }

    [ObservableProperty]
    public partial string? FeedbackMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity FeedbackSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsFeedbackOpen { get; set; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var list = await _absences.MineAsync(cancellationToken);
        Items.Clear();
        foreach (var a in list)
        {
            Items.Add(new AbsenceItemViewModel(a, this, Services.Localizer));
        }
    }

    [RelayCommand]
    private async Task ReportAsync()
    {
        if (From is not { } from || To is not { } to)
        {
            return;
        }

        try
        {
            await _absences.CreateAsync(DateOnly.FromDateTime(from.Date), DateOnly.FromDateTime(to.Date), Reason);
            Reason = null;
            Show(Services.Localizer.GetString(StringKeys.AbsenceReported), InfoBarSeverity.Success);
            await LoadAsync(silent: true);
        }
        catch (ApiProblemException ex)
        {
            Show(ex.Problem.Detail ?? Services.Localizer.GetError(ex.Problem.Code), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or ArgumentOutOfRangeException)
        {
            Show(Services.Localizer.GetError(ApiProblem.Codes.Unknown), InfoBarSeverity.Error);
        }
    }

    internal async Task DeleteAsync(AbsenceItemViewModel item)
    {
        try
        {
            await _absences.DeleteAsync(item.Id);
            Items.Remove(item);
            DataChanged();
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

    private void Show(string text, InfoBarSeverity severity)
    {
        IsFeedbackOpen = false;
        FeedbackMessage = text;
        FeedbackSeverity = severity;
        IsFeedbackOpen = true;
    }
}

public sealed partial class AbsenceItemViewModel(Absence absence, AbsencesViewModel owner, ILocalizer localizer)
{
    public string Id => absence.Id;

    public string Period { get; } = $"{absence.StartsOn.ToString("d", CultureInfo.CurrentCulture)} – {absence.EndsOn.ToString("d", CultureInfo.CurrentCulture)}";

    public string Detail { get; } = string.Join(" · ", new[]
    {
        localizer.GetString(absence.IsCurrent ? StringKeys.AbsenceActive : StringKeys.AbsenceUpcoming),
        absence.Reason,
    }.Where(s => !string.IsNullOrEmpty(s)));

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);
}
