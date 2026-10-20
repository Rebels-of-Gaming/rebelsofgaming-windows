using System.Globalization;
using System.Text.Json;
using RoG.Core.Api;

namespace RoG.Core.Features.Servers;

/// <summary>Ein zugewiesener Port aus dem Panel (<c>provisioning.allocations</c>); <see cref="Alias"/> = IP-Alias oder <c>null</c>.</summary>
public sealed record ServerAllocation(string Ip, string? Alias, int Port, bool Primary)
{
    /// <summary>Adresse wie auf der Website: Alias statt IP, wenn gesetzt.</summary>
    public string Address => string.Create(CultureInfo.InvariantCulture, $"{(string.IsNullOrWhiteSpace(Alias) ? Ip : Alias)}:{Port}");

    /// <summary>Hauptport zuerst, danach nach Port; ohne Einträge ohne IP oder mit ungültigem Port.</summary>
    public static IReadOnlyList<ServerAllocation> Ordered(IEnumerable<ServerAllocation>? allocations) =>
        allocations is null
            ? []
            : allocations.Where(a => !string.IsNullOrWhiteSpace(a.Ip) && a.Port is > 0 and <= 65535)
                .OrderByDescending(a => a.Primary).ThenBy(a => a.Port).ToList();

    internal static List<ServerAllocation>? Parse(JsonElement provisioning)
    {
        if (!provisioning.TryGetProperty("allocations", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<ServerAllocation>();
        foreach (var a in list.EnumerateArray())
        {
            if (a.ValueKind == JsonValueKind.Object
                && a.TryGetProperty("ip", out var ip) && ip.ValueKind == JsonValueKind.String
                && a.TryGetProperty("port", out var port) && port.TryGetInt32(out var p))
            {
                var alias = a.TryGetProperty("alias", out var al) && al.ValueKind == JsonValueKind.String ? ApiValues.Text(al.GetString()) : null;
                result.Add(new ServerAllocation(ip.GetString()!, alias, p, a.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True));
            }
        }

        return result;
    }
}
