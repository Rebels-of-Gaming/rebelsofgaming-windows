using System.Collections.Concurrent;
using System.Text.Json;

namespace RoG.Core.Realtime;

/// <summary>Ein Ereignis auf einem Kanal. <see cref="DataJson"/> ist immer das entpackte JSON (Pusher sendet <c>data</c> oft als String).</summary>
public sealed record ReverbEvent(string Channel, string Name, string DataJson)
{
    public T? Deserialize<T>(JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(DataJson, options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>Mitglied eines Presence-Kanals (<c>user_id</c> + <c>user_info</c> als JSON).</summary>
public sealed record PresenceMember(string Id, string InfoJson);

/// <summary>Abonnierter Kanal. Ereignisse kommen auf dem Empfangs-Thread – die App leitet sie auf den UI-Thread um.</summary>
public sealed class ReverbChannel
{
    private readonly ConcurrentDictionary<string, PresenceMember> _members = new(StringComparer.Ordinal);

    internal ReverbChannel(string name) => Name = name;

    public string Name { get; }

    public bool IsPrivate => Name.StartsWith("private-", StringComparison.Ordinal) || IsPresence;

    public bool IsPresence => Name.StartsWith("presence-", StringComparison.Ordinal);

    public bool IsSubscribed { get; private set; }

    /// <summary>Aktuelle Mitglieder (nur Presence-Kanäle).</summary>
    public IReadOnlyCollection<PresenceMember> Members => _members.Values.ToList();

    public event EventHandler? Subscribed;

    /// <summary>Abonnement abgelehnt (z. B. 403 von <c>/v1/broadcasting/auth</c>); Status oder <c>null</c>.</summary>
    public event EventHandler<int?>? SubscriptionFailed;

    public event EventHandler<ReverbEvent>? EventReceived;

    public event EventHandler<PresenceMember>? MemberAdded;

    public event EventHandler<PresenceMember>? MemberRemoved;

    internal void MarkUnsubscribed() => IsSubscribed = false;

    internal void OnSubscriptionSucceeded(string dataJson)
    {
        IsSubscribed = true;
        if (IsPresence)
        {
            _members.Clear();
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.TryGetProperty("presence", out var presence)
                    && presence.TryGetProperty("hash", out var hash) && hash.ValueKind == JsonValueKind.Object)
                {
                    foreach (var member in hash.EnumerateObject())
                    {
                        _members[member.Name] = new PresenceMember(member.Name, member.Value.GetRawText());
                    }
                }
            }
            catch (JsonException)
            {
                // ungültige Presence-Daten: Kanal bleibt abonniert, Liste leer
            }
        }

        Subscribed?.Invoke(this, EventArgs.Empty);
    }

    internal void OnSubscriptionFailed(int? status)
    {
        IsSubscribed = false;
        SubscriptionFailed?.Invoke(this, status);
    }

    internal void OnMemberAdded(string dataJson)
    {
        if (TryReadMember(dataJson, out var member))
        {
            _members[member.Id] = member;
            MemberAdded?.Invoke(this, member);
        }
    }

    internal void OnMemberRemoved(string dataJson)
    {
        if (TryReadMember(dataJson, out var member) && _members.TryRemove(member.Id, out var removed))
        {
            MemberRemoved?.Invoke(this, removed);
        }
    }

    internal void OnEvent(string eventName, string dataJson) => EventReceived?.Invoke(this, new ReverbEvent(Name, eventName, dataJson));

    private static bool TryReadMember(string dataJson, out PresenceMember member)
    {
        member = new PresenceMember(string.Empty, "{}");
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty("user_id", out var id))
            {
                return false;
            }

            var userId = id.ValueKind == JsonValueKind.String ? id.GetString()! : id.GetRawText();
            var info = doc.RootElement.TryGetProperty("user_info", out var i) ? i.GetRawText() : "{}";
            member = new PresenceMember(userId, info);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
