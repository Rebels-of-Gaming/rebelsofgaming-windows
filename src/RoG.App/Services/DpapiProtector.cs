using System.Runtime.InteropServices.WindowsRuntime;
using RoG.Core.Security;
using Windows.Security.Cryptography.DataProtection;

namespace RoG.App.Services;

/// <summary>Lokale Caches mit DPAPI verschlüsseln – nur der angemeldete Windows-Benutzer kann sie lesen.</summary>
public sealed class DpapiProtector : IPayloadProtector
{
    public async Task<byte[]> ProtectAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var provider = new DataProtectionProvider("LOCAL=user");
        var buffer = await provider.ProtectAsync(data.AsBuffer()).AsTask(cancellationToken);
        return buffer.ToArray();
    }

    public async Task<byte[]> UnprotectAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var provider = new DataProtectionProvider();
        var buffer = await provider.UnprotectAsync(data.AsBuffer()).AsTask(cancellationToken);
        return buffer.ToArray();
    }
}
