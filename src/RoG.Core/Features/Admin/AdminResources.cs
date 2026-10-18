using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;
using RoG.Core.Features.Account;

namespace RoG.Core.Features.Admin;

/// <summary>Art eines Formularfelds im allgemeinen Verwaltungs-Editor.</summary>
public enum AdminFieldType
{
    /// <summary>Einzeiliger Text.</summary>
    Text,

    /// <summary>Mehrzeiliger Text (Beschreibung, Markdown, BBCode).</summary>
    Multiline,

    Bool,
    WholeNumber,
    Number,

    /// <summary>Auswahl aus festen Werten (<see cref="AdminField.Choices"/>).</summary>
    Choice,

    /// <summary>Farbe als <c>#RRGGBB</c>.</summary>
    Color,

    /// <summary>Datum <c>yyyy-MM-dd</c>.</summary>
    Date,

    /// <summary>Zeitpunkt (ISO 8601, lokal eingegeben, als UTC gesendet).</summary>
    DateTime,

    /// <summary>Absolute https-Adresse.</summary>
    Url,

    /// <summary>Übersetzbarer Text: <c>{"de": …, "en": …, "fr": …}</c>.</summary>
    Translated,

    /// <summary>Übersetzbarer mehrzeiliger Text.</summary>
    TranslatedMultiline,

    /// <summary>ID eines Eintrags aus einem anderen Bereich (<see cref="AdminField.Reference"/>), Auswahl aus dessen Liste.</summary>
    Reference,

    /// <summary>Liste von Texten, in der Oberfläche eine Zeile je Eintrag.</summary>
    Lines,

    /// <summary>Mehrfachauswahl aus <see cref="AdminField.Choices"/> (gesendet als Liste).</summary>
    MultiChoice,
}

/// <summary>Auswahlwert: <see cref="Value"/> geht an die API, <see cref="LabelKey"/> ist ein Ressourcenschlüssel (oder Text, wenn er nicht existiert).</summary>
/// <param name="LabelText">Fester Text statt Ressource (z. B. Werte aus dem Panel).</param>
public sealed record AdminChoice(string Value, string LabelKey, string? LabelText = null);

/// <summary>Formularfeld eines Bereichs.</summary>
/// <param name="Key">Schlüssel beim Schreiben (Body).</param>
/// <param name="LabelKey">Ressourcenschlüssel der Beschriftung.</param>
/// <param name="ReadFrom">Pfad beim Lesen (Punkte für verschachtelt, z. B. <c>game.id</c>), wenn er vom Schreibschlüssel abweicht.</param>
/// <param name="CreateOnly">Nur beim Anlegen änderbar (z. B. Slug).</param>
/// <param name="Reference">Bereich, aus dessen Liste <see cref="AdminFieldType.Reference"/> wählt.</param>
/// <param name="AlwaysSend">Auch unverändert mitsenden (Schlüssel eines Upserts, z. B. <c>role_id</c>).</param>
/// <remarks>Ein Punkt im <see cref="Key"/> schreibt verschachtelt: <c>maintenance.enabled</c> → <c>{"maintenance":{"enabled":…}}</c>.</remarks>
public sealed record AdminField(
    string Key,
    string LabelKey,
    AdminFieldType Type,
    bool Required = false,
    int? MaxLength = null,
    IReadOnlyList<AdminChoice>? Choices = null,
    string? ReadFrom = null,
    bool CreateOnly = false,
    string? Reference = null,
    double? Min = null,
    double? Max = null,
    string? HelpKey = null,
    bool AlwaysSend = false,
    string? LabelText = null,
    string? HelpText = null)
{
    public bool IsTranslated => Type is AdminFieldType.Translated or AdminFieldType.TranslatedMultiline;
}

/// <summary>
/// Ein Bereich der Website-Verwaltung als Liste mit Formular. Pfade relativ zu <c>/v1/</c>; <c>{id}</c> wird durch
/// <see cref="IdField"/> des Eintrags ersetzt. Ohne <see cref="CreatePath"/>/<see cref="UpdatePath"/>/<see cref="DeletePath"/>
/// gibt es die Aktion nicht. Sichtbar nur mit Recht <see cref="Permission"/> (global oder irgendwo) und Scope
/// <see cref="ReadScope"/>; schreiben zusätzlich mit <see cref="WriteScope"/> – der Server prüft ohnehin.
/// <see cref="WriteIdField"/>: manche Bereiche lesen per Slug, schreiben aber per ID. <see cref="WebPath"/>: Seite der
/// Website-Verwaltung für alles, was der Editor (noch) nicht kann (Reihenfolge, verschachtelte Listen, Bilder).
/// <see cref="FullReplace"/>: der Server ersetzt den ganzen Eintrag (PUT) – dann gehen immer alle Felder mit.
/// </summary>
public sealed record AdminArea(
    string Key,
    string TitleKey,
    string Permission,
    string ReadScope,
    string WriteScope,
    string ListPath,
    IReadOnlyList<AdminField> Fields,
    string IdField = "id",
    string TitleField = "name",
    IReadOnlyList<string>? SubtitleFields = null,
    string? DetailPath = null,
    string? CreatePath = null,
    string? UpdatePath = null,
    string UpdateMethod = "PATCH",
    string? DeletePath = null,
    string ItemsPath = "data",
    bool Paged = false,
    string? HelpKey = null,
    string? WriteIdField = null,
    string? WebPath = null,
    bool FullReplace = false,
    string Group = "")
{
    /// <summary>Einstellungen statt Liste: <see cref="DetailPath"/> ist das eine Objekt, <see cref="UpdatePath"/> speichert es.</summary>
    public bool IsSingleton => ListPath.Length == 0;

    public bool CanCreate => CreatePath is not null;

    public bool CanUpdate => UpdatePath is not null;

    public bool CanDelete => DeletePath is not null;

    /// <summary>Darf die Person den Bereich sehen?</summary>
    public bool IsVisibleTo(UserProfile? me) =>
        me is not null && Permission.Split('|').Any(p => me.CanAnywhere(p)) && (me.HasScope(ReadScope) || me.HasScope(WriteScope));

    /// <summary>Darf die Person schreiben (Recht und Schreib-Scope)?</summary>
    public bool IsWritableBy(UserProfile? me) =>
        me is not null && Permission.Split('|').Any(p => me.CanAnywhere(p)) && me.HasScope(WriteScope);

    public static string PathFor(string template, string id) => template.Replace("{id}", Uri.EscapeDataString(id), StringComparison.Ordinal);
}

/// <summary>Zeile der Liste: ID, Titel, Untertitel und der rohe Eintrag (für das Formular).</summary>
public sealed record AdminRecord(string Id, string Title, string? Subtitle, JsonElement Raw);

/// <summary>
/// Übersetzungen eines Feldes. <see cref="Partial"/>: die API lieferte nur die Sprache der Anfrage – das Formular zeigt
/// dann nur diese, und gesendet wird nur, was geändert wurde (die Website führt Sprachen zusammen statt zu ersetzen).
/// </summary>
public sealed class AdminTranslations : Dictionary<string, string>
{
    public AdminTranslations()
        : base(StringComparer.Ordinal)
    {
    }

    public bool Partial { get; init; }
}

/// <summary>
/// Werte eines Formulars: Text (<see cref="string"/>), <see cref="bool"/> oder Übersetzungen
/// (<see cref="IReadOnlyDictionary{TKey, TValue}"/> Sprache → Text). Zahlen, Daten und Listen sind Text.
/// </summary>
public sealed class AdminFormValues : Dictionary<string, object?>
{
    public AdminFormValues()
        : base(StringComparer.Ordinal)
    {
    }

    public string Text(string key) => TryGetValue(key, out var v) && v is string s ? s : string.Empty;

    public bool Flag(string key) => TryGetValue(key, out var v) && v is true;

    public IReadOnlyDictionary<string, string> Translations(string key) =>
        TryGetValue(key, out var v) && v is IReadOnlyDictionary<string, string> map ? map : new Dictionary<string, string>();
}

/// <summary>Formularlogik des allgemeinen Verwaltungs-Editors: lesen, prüfen, Body bauen (nur Geändertes).</summary>
public static partial class AdminForm
{
    public static readonly IReadOnlyList<string> Locales = ["de", "en", "fr"];

    /// <summary>Formularwerte aus einem Eintrag der API (oder leer für „neu“).</summary>
    /// <param name="locale">Sprache der Anfrage – in ihr liefert die API einfache Texte statt Sprachtabellen.</param>
    public static AdminFormValues Read(AdminArea area, JsonElement? item, string locale = "de")
    {
        ArgumentNullException.ThrowIfNull(area);
        var values = new AdminFormValues();
        foreach (var field in area.Fields)
        {
            var e = item is { } i ? Find(i, field.ReadFrom ?? field.Key) : null;
            values[field.Key] = ValueOf(field, e, locale, isNew: item is null);
        }

        return values;
    }

    internal static object? ValueOf(AdminField field, JsonElement? e, string locale = "de", bool isNew = false)
    {
        if (field.IsTranslated)
        {
            if (e is { ValueKind: JsonValueKind.Object } o)
            {
                var map = new AdminTranslations();
                foreach (var p in o.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    map[p.Name] = p.Value.GetString() ?? string.Empty;
                }

                return map;
            }

            // Nur ein Text (Sprache der Anfrage) oder nichts: beim Anlegen alle Sprachen, sonst nur diese eine
            var partial = new AdminTranslations { Partial = !isNew };
            if (e is { ValueKind: JsonValueKind.String } s)
            {
                partial[locale] = s.GetString() ?? string.Empty;
            }
            else if (!isNew)
            {
                partial[locale] = string.Empty;
            }

            return partial;
        }

        if (field.Type == AdminFieldType.Bool)
        {
            return e is { ValueKind: JsonValueKind.True };
        }

        if (e is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return field.Type switch
        {
            AdminFieldType.Reference when v.ValueKind == JsonValueKind.Object => Str(v, "id") ?? string.Empty,
            AdminFieldType.Lines or AdminFieldType.MultiChoice when v.ValueKind == JsonValueKind.Array => string.Join("\n", v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())),
            AdminFieldType.DateTime when v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
                => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            AdminFieldType.Date when v.ValueKind == JsonValueKind.String => (v.GetString() ?? string.Empty).Split('T')[0],
            _ => v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.GetRawText(),
        };
    }

    /// <summary>Fehler je Feld (Schlüssel → Ressourcenschlüssel der Meldung). Leer = in Ordnung.</summary>
    public static IReadOnlyDictionary<string, string> Validate(AdminArea area, AdminFormValues values, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(values);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in area.Fields)
        {
            if (f.CreateOnly && !isNew)
            {
                continue;
            }

            if (f.IsTranslated)
            {
                var map = values.Translations(f.Key);
                if (f.Required && string.IsNullOrWhiteSpace(map.GetValueOrDefault("de")))
                {
                    errors[f.Key] = AdminFormErrors.RequiredGerman;
                }
                else if (f.MaxLength is { } maxT && map.Values.Any(t => t.Length > maxT))
                {
                    errors[f.Key] = AdminFormErrors.TooLong;
                }

                continue;
            }

            if (f.Type == AdminFieldType.Bool)
            {
                continue;
            }

            var text = values.Text(f.Key).Trim();
            if (text.Length == 0)
            {
                if (f.Required)
                {
                    errors[f.Key] = AdminFormErrors.Required;
                }

                continue;
            }

            if (f.MaxLength is { } max && text.Length > max)
            {
                errors[f.Key] = AdminFormErrors.TooLong;
                continue;
            }

            var error = f.Type switch
            {
                AdminFieldType.WholeNumber => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Range(n, f) : AdminFormErrors.Number,
                AdminFieldType.Number => ParseNumber(text) is { } d ? Range(d, f) : AdminFormErrors.Number,
                AdminFieldType.Color => ColorPattern().IsMatch(text) ? null : AdminFormErrors.Color,
                AdminFieldType.Url => Uri.TryCreate(text, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? null : AdminFormErrors.Url,
                AdminFieldType.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : AdminFormErrors.Date,
                AdminFieldType.DateTime => ParseLocal(text) is null ? AdminFormErrors.Date : null,
                AdminFieldType.Choice => f.Choices?.Any(c => c.Value == text) == false ? AdminFormErrors.Choice : null,
                AdminFieldType.MultiChoice => SplitLines(text).All(v => f.Choices?.Any(c => c.Value == v) != false) ? null : AdminFormErrors.Choice,
                _ => null,
            };
            if (error is not null)
            {
                errors[f.Key] = error;
            }
        }

        return errors;
    }

    /// <summary>
    /// JSON-Body: beim Anlegen alle ausgefüllten Felder, beim Ändern nur geänderte (geleerte als <c>null</c>).
    /// Übersetzungen gehen als ganze Sprachtabelle (nur nicht leere Sprachen).
    /// </summary>
    public static JsonObject BuildBody(AdminArea area, AdminFormValues? original, AdminFormValues edited)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(edited);
        if (area.FullReplace)
        {
            var all = new JsonObject();
            foreach (var f in area.Fields.Where(f => !f.CreateOnly || original is null))
            {
                Put(all, f.Key, ToNode(f, edited.GetValueOrDefault(f.Key)));
            }

            return all;
        }

        var isNew = original is null;
        var body = new JsonObject();
        foreach (var f in area.Fields)
        {
            if (f.CreateOnly && !isNew)
            {
                continue;
            }

            var now = edited.GetValueOrDefault(f.Key);
            var before = isNew ? null : original!.GetValueOrDefault(f.Key);
            if (!isNew && !f.AlwaysSend && Same(f, before, now))
            {
                continue;
            }

            var node = f.IsTranslated && !isNew ? TranslationChanges(before as IReadOnlyDictionary<string, string>, now as IReadOnlyDictionary<string, string>) : ToNode(f, now);
            if (isNew && node is null)
            {
                continue;
            }

            Put(body, f.Key, node);
        }

        return body;
    }

    /// <summary>Nur geänderte Sprachen; eine geleerte Sprache als <c>""</c> (die Website entfernt genau diese).</summary>
    private static JsonObject? TranslationChanges(IReadOnlyDictionary<string, string>? before, IReadOnlyDictionary<string, string>? now)
    {
        var o = new JsonObject();
        var a = Clean(before);
        var b = Clean(now);
        foreach (var locale in Locales.Concat(a.Keys).Concat(b.Keys).Distinct(StringComparer.Ordinal))
        {
            var had = a.TryGetValue(locale, out var x);
            var has = b.TryGetValue(locale, out var y);
            if (has && (!had || x != y))
            {
                o[locale] = y;
            }
            else if (had && !has)
            {
                o[locale] = string.Empty;
            }
        }

        return o.Count == 0 ? null : o;
    }

    private static void Put(JsonObject body, string key, JsonNode? node)
    {
        var parts = key.Split('.');
        var target = body;
        foreach (var part in parts[..^1])
        {
            if (target[part] is not JsonObject child)
            {
                child = new JsonObject();
                target[part] = child;
            }

            target = child;
        }

        target[parts[^1]] = node;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Same(AdminField f, object? a, object? b)
    {
        if (f.IsTranslated)
        {
            var x = Clean(a as IReadOnlyDictionary<string, string>);
            var y = Clean(b as IReadOnlyDictionary<string, string>);
            return x.Count == y.Count && x.All(p => y.TryGetValue(p.Key, out var v) && v == p.Value);
        }

        return f.Type == AdminFieldType.Bool ? (a is true) == (b is true) : ((a as string)?.Trim() ?? string.Empty) == ((b as string)?.Trim() ?? string.Empty);
    }

    private static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? map) =>
        map?.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToDictionary(p => p.Key, p => p.Value.Trim(), StringComparer.Ordinal) ?? [];

    internal static JsonNode? ToNode(AdminField f, object? value)
    {
        if (f.IsTranslated)
        {
            var map = Clean(value as IReadOnlyDictionary<string, string>);
            if (map.Count == 0)
            {
                return null;
            }

            var o = new JsonObject();
            foreach (var (k, v) in map.OrderBy(p => Locales.ToList().IndexOf(p.Key) is var i && i < 0 ? 99 : i))
            {
                o[k] = v;
            }

            return o;
        }

        if (f.Type == AdminFieldType.Bool)
        {
            return JsonValue.Create(value is true);
        }

        var text = (value as string)?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        return f.Type switch
        {
            AdminFieldType.WholeNumber when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => JsonValue.Create(n),
            AdminFieldType.Number when ParseNumber(text) is { } d => JsonValue.Create(d),
            AdminFieldType.Lines => new JsonArray(text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()),
            AdminFieldType.MultiChoice => new JsonArray(SplitLines(text).Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()),
            AdminFieldType.DateTime when ParseLocal(text) is { } t => JsonValue.Create(t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            AdminFieldType.Color => JsonValue.Create(text.ToUpperInvariant()),
            _ => JsonValue.Create(text),
        };
    }

    private static string? Range(double n, AdminField f) =>
        (f.Min is { } min && n < min) || (f.Max is { } max && n > max) ? AdminFormErrors.Range : null;

    private static double? ParseNumber(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseLocal(string text) =>
        System.DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)
            ? new DateTimeOffset(t)
            : null;

    /// <summary>Wert unter einem Punkt-Pfad (<c>game.name</c>).</summary>
    public static JsonElement? Find(JsonElement item, string path)
    {
        var current = item;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>Text eines Feldes zum Anzeigen; übersetzte Felder in <paramref name="locale"/>, sonst Deutsch, sonst die erste Sprache.</summary>
    public static string? Display(JsonElement item, string path, string locale)
    {
        if (Find(item, path) is not { } e)
        {
            return null;
        }

        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.Object when (Str(e, "label") ?? Str(e, "name") ?? Str(e, "title")) is { } named => named,
            JsonValueKind.Object => Str(e, locale) ?? Str(e, "de") ?? e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString()).FirstOrDefault(),
            _ => null,
        };
    }

    internal static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}

/// <summary>Ressourcenschlüssel der Formularfehler (in den .resw-Dateien).</summary>
public static class AdminFormErrors
{
    public const string Required = "AdminForm_ErrorRequired";
    public const string RequiredGerman = "AdminForm_ErrorRequiredGerman";
    public const string TooLong = "AdminForm_ErrorTooLong";
    public const string Number = "AdminForm_ErrorNumber";
    public const string Range = "AdminForm_ErrorRange";
    public const string Color = "AdminForm_ErrorColor";
    public const string Url = "AdminForm_ErrorUrl";
    public const string Date = "AdminForm_ErrorDate";
    public const string Choice = "AdminForm_ErrorChoice";
}

/// <summary>Lesen und Schreiben eines <see cref="AdminArea"/> über rohe Aufrufe (die Spec typisiert viele Bodies nicht).</summary>
public sealed class AdminResourceService(HttpClient http)
{
    public async Task<Page<AdminRecord>> ListAsync(AdminArea area, string locale, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        var path = area.ListPath;
        if (area.Paged)
        {
            path += (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "per_page=100" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
        }

        using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        return ParseList(area, doc!.RootElement, locale);
    }

    internal static Page<AdminRecord> ParseList(AdminArea area, JsonElement root, string locale)
    {
        var items = AdminForm.Find(root, area.ItemsPath) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().ToList() : [];
        var records = items.Select(i => ToRecord(area, i, locale)).Where(r => r.Id.Length > 0).ToList();
        var next = AdminForm.Find(root, "meta.next_cursor") is { ValueKind: JsonValueKind.String } c ? c.GetString() : null;
        return new Page<AdminRecord>(records, next);
    }

    internal static AdminRecord ToRecord(AdminArea area, JsonElement item, string locale)
    {
        var id = AdminForm.Display(item, area.IdField, locale) ?? string.Empty;
        var title = AdminForm.Display(item, area.TitleField, locale) ?? id;
        var subtitle = string.Join(" · ", (area.SubtitleFields ?? []).Select(f => AdminForm.Display(item, f, locale)).Where(s => !string.IsNullOrWhiteSpace(s)));
        return new AdminRecord(id, title, subtitle.Length > 0 ? subtitle : null, item.Clone());
    }

    /// <summary>Eintrag frisch laden (Detail-Pfad) – ohne Detail-Pfad bleibt der Listeneintrag.</summary>
    public async Task<AdminRecord> GetAsync(AdminArea area, AdminRecord record, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(record);
        if (area.DetailPath is null || area.IsSingleton)
        {
            return record;
        }

        using var doc = await SendAsync(HttpMethod.Get, AdminArea.PathFor(area.DetailPath, record.Id), null, cancellationToken).ConfigureAwait(false);
        var data = doc!.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
        return ToRecord(area, data, locale);
    }

    public async Task<AdminRecord?> SaveAsync(AdminArea area, AdminRecord? existing, JsonObject body, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        var (method, path) = existing is null
            ? (area.CreatePath == area.UpdatePath ? new HttpMethod(area.UpdateMethod) : HttpMethod.Post, area.CreatePath ?? throw new InvalidOperationException("Anlegen gibt es hier nicht"))
            : (new HttpMethod(area.UpdateMethod), AdminArea.PathFor(area.UpdatePath ?? throw new InvalidOperationException("Ändern gibt es hier nicht"), WriteId(area, existing)));
        using var doc = await SendAsync(method, path, body, cancellationToken).ConfigureAwait(false);
        return doc is not null && doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? ToRecord(area, d, locale) : null;
    }

    public async Task DeleteAsync(AdminArea area, AdminRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(record);
        using var _ = await SendAsync(HttpMethod.Delete, AdminArea.PathFor(area.DeletePath ?? throw new InvalidOperationException("Löschen gibt es hier nicht"), WriteId(area, record)), null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>ID für Ändern/Löschen (bei gemischten Bereichen nicht der Slug aus der Liste).</summary>
    public static string WriteId(AdminArea area, AdminRecord record)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(record);
        return area.WriteIdField is { } field && AdminForm.Display(record.Raw, field, "de") is { Length: > 0 } id ? id : record.Id;
    }

    /// <summary>Einstellungen eines <see cref="AdminArea.IsSingleton"/>-Bereichs laden.</summary>
    public async Task<AdminRecord> LoadSingletonAsync(AdminArea area, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(area);
        using var doc = await SendAsync(HttpMethod.Get, area.DetailPath ?? throw new InvalidOperationException("Kein Pfad"), null, cancellationToken).ConfigureAwait(false);
        var data = doc!.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
        return new AdminRecord("settings", string.Empty, null, data.Clone());
    }

    /// <summary>Beliebige Zusatzaktion eines Bereichs (z. B. „Test senden“) – Body optional.</summary>
    public async Task<JsonDocument?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        }

        if (method != HttpMethod.Get)
        {
            request.Options.Set(RoGHttpHandler.SkipEtag, true);
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }
}
