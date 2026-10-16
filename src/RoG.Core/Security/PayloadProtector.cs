namespace RoG.Core.Security;

/// <summary>
/// Verschlüsselung lokaler Caches (Pflichtenheft 7: an den Windows-Benutzer gebunden, DPAPI). Die App setzt
/// <c>DataProtectionProvider("LOCAL=user")</c> ein; Tests und andere Plattformen nutzen <see cref="NoPayloadProtection"/>.
/// </summary>
public interface IPayloadProtector
{
    Task<byte[]> ProtectAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>Entschlüsseln; wirft bei fremden/beschädigten Daten (Aufrufer verwerfen die Datei dann).</summary>
    Task<byte[]> UnprotectAsync(byte[] data, CancellationToken cancellationToken = default);
}

/// <summary>Ohne Verschlüsselung (Tests).</summary>
public sealed class NoPayloadProtection : IPayloadProtector
{
    public static NoPayloadProtection Instance { get; } = new();

    public Task<byte[]> ProtectAsync(byte[] data, CancellationToken cancellationToken = default) => Task.FromResult(data);

    public Task<byte[]> UnprotectAsync(byte[] data, CancellationToken cancellationToken = default) => Task.FromResult(data);
}
