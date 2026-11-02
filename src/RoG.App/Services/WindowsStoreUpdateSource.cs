using RoG.Core.Updates;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace RoG.App.Services;

/// <summary>
/// Updates über <see cref="StoreContext"/>. Desktop-Apps brauchen das Fenster-Handle (Dialoge des Stores). Nur bei
/// Installation aus dem Store (<see cref="PackageSignatureKind.Store"/>) – Sideload/Entwicklung fragt nie.
/// </summary>
public sealed class WindowsStoreUpdateSource(Func<nint> windowHandle) : IStoreUpdateSource
{
    private StoreContext? _context;
    private IReadOnlyList<StorePackageUpdate> _updates = [];

    public bool IsStoreInstall { get; } = DetectStoreInstall();

    public async Task<StoreUpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        var updates = await Context().GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
        _updates = updates.ToList();
        return new StoreUpdateCheck(_updates.Count, _updates.Any(u => u.Mandatory));
    }

    public async Task<StoreInstallResult> InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_updates.Count == 0)
        {
            return StoreInstallResult.Canceled;
        }

        var report = new Progress<StorePackageUpdateStatus>(s => progress?.Report(s.TotalDownloadProgress));
        var result = await Context().RequestDownloadAndInstallStorePackageUpdatesAsync(_updates).AsTask(cancellationToken, report);
        return result.OverallState switch
        {
            StorePackageUpdateState.Completed => StoreInstallResult.Completed,
            StorePackageUpdateState.Canceled => StoreInstallResult.Canceled,
            _ => StoreInstallResult.Failed,
        };
    }

    private StoreContext Context()
    {
        if (_context is null)
        {
            _context = StoreContext.GetDefault();
            WinRT.Interop.InitializeWithWindow.Initialize(_context, windowHandle());
        }

        return _context;
    }

    internal static bool DetectStoreInstall()
    {
        try
        {
            return Package.Current.SignatureKind == PackageSignatureKind.Store;
        }
#pragma warning disable CA1031 // ohne Paket-Identität (unverpackt gestartet) gibt es keinen Store
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
