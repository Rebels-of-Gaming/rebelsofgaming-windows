using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RoG.Core.Api;
using RoG.Core.Features.Admin;

namespace RoG.Core.Features.Servers;

/// <summary>
/// Über die Website angelegter Server (<c>provisioning</c> an der Server-Ressource, nur mit <c>servers.manage</c> bzw.
/// <c>servers.create</c>): Installationsstand installing · ready · failed, optionaler Ablauf, löschbar, zugewiesene Ports
/// (<c>allocations</c>, Hauptport zuerst; leer, wenn das Panel nicht erreichbar war).
/// </summary>
public sealed record ServerProvisioning(string Status, DateTimeOffset? ProvisionedAt, DateTimeOffset? ExpiresAt, bool Deletable, IReadOnlyList<ServerAllocation>? Allocations = null)
{
    public IReadOnlyList<ServerAllocation> Ports { get; } = ServerAllocation.Ordered(Allocations);

    public bool IsInstalling => Status == "installing";

    public bool IsFailed => Status == "failed";

    /// <summary>Die Website warnt 24 Stunden vor dem automatischen Löschen.</summary>
    public bool ExpiresSoon(DateTimeOffset now) => ExpiresAt is { } at && at - now <= TimeSpan.FromHours(24);

    public static ServerProvisioning? Parse(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("provisioned", out var p) && p.ValueKind == JsonValueKind.True
            ? new ServerProvisioning(
                ProvisioningJson.Str(e, "status") ?? "installing",
                ProvisioningJson.Time(e, "provisioned_at"),
                ProvisioningJson.Time(e, "expires_at"),
                e.TryGetProperty("deletable", out var d) && d.ValueKind == JsonValueKind.True,
                ServerAllocation.Parse(e))
            : null;
}

public sealed record ProvisionLocation(int Id, string Code, string Description);

public sealed record ProvisionAllocation(int Id, string Ip, string? IpAlias, int Port)
{
    public string Display => $"{IpAlias ?? Ip}:{Port.ToString(CultureInfo.InvariantCulture)}";
}

public sealed record ProvisionNode(int Id, string Name, int LocationId, string Fqdn, long MemoryMb, long DiskMb, IReadOnlyList<ProvisionAllocation> Allocations);

/// <summary>Variable eines Eggs; <see cref="Rules"/> ist der Laravel-Regelstring des Panels.</summary>
public sealed record EggVariable(string Name, string? Description, string Env, string Default, bool UserViewable, bool UserEditable, string Rules);

public sealed record ProvisionEgg(
    int Id,
    int NestId,
    string NestName,
    string Name,
    string? Description,
    string DockerImage,
    IReadOnlyDictionary<string, string> DockerImages,
    string Startup,
    IReadOnlyList<EggVariable> Variables)
{
    public string Display => $"{NestName} · {Name}";
}

/// <summary>Katalog für „Neuer Gameserver“ (<c>GET /v1/servers/provisioning/catalog</c>).</summary>
public sealed record ProvisionCatalog(
    IReadOnlyList<ProvisionLocation> Locations,
    IReadOnlyList<ProvisionNode> Nodes,
    IReadOnlyList<ProvisionEgg> Eggs,
    IReadOnlyList<int> ExpiryPresets)
{
    public static ProvisionCatalog Parse(JsonElement d)
    {
        var locations = Array(d, "locations").Select(l => new ProvisionLocation(Int(l, "id"), ProvisioningJson.Str(l, "short") ?? string.Empty, ProvisioningJson.Str(l, "long") ?? string.Empty)).ToList();
        var nodes = Array(d, "nodes").Select(n => new ProvisionNode(
            Int(n, "id"),
            ProvisioningJson.Str(n, "name") ?? "—",
            Int(n, "location_id"),
            ProvisioningJson.Str(n, "fqdn") ?? string.Empty,
            Long(n, "memory"),
            Long(n, "disk"),
            Array(n, "allocations").Select(a => new ProvisionAllocation(Int(a, "id"), ProvisioningJson.Str(a, "ip") ?? string.Empty, ProvisioningJson.Str(a, "ip_alias"), Int(a, "port"))).ToList())).ToList();
        var eggs = new List<ProvisionEgg>();
        foreach (var nest in Array(d, "nests"))
        {
            var nestId = Int(nest, "id");
            var nestName = ProvisioningJson.Str(nest, "name") ?? "—";
            foreach (var egg in Array(nest, "eggs"))
            {
                var images = new Dictionary<string, string>(StringComparer.Ordinal);
                if (egg.TryGetProperty("docker_images", out var di) && di.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in di.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                    {
                        images[p.Name] = p.Value.GetString()!;
                    }
                }

                var image = ProvisioningJson.Str(egg, "docker_image") ?? images.Values.FirstOrDefault() ?? string.Empty;
                if (image.Length > 0 && !images.ContainsValue(image))
                {
                    images[image] = image;
                }

                eggs.Add(new ProvisionEgg(
                    Int(egg, "id"),
                    nestId,
                    nestName,
                    ProvisioningJson.Str(egg, "name") ?? "—",
                    ProvisioningJson.Str(egg, "description"),
                    image,
                    images,
                    egg.TryGetProperty("startup", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString()! : string.Empty,
                    Array(egg, "variables").Select(v => new EggVariable(
                        ProvisioningJson.Str(v, "name") ?? ProvisioningJson.Str(v, "env_variable") ?? "—",
                        ProvisioningJson.Str(v, "description"),
                        ProvisioningJson.Str(v, "env_variable") ?? string.Empty,
                        v.TryGetProperty("default_value", out var dv) && dv.ValueKind is JsonValueKind.String or JsonValueKind.Number ? (dv.ValueKind == JsonValueKind.String ? dv.GetString()! : dv.GetRawText()) : string.Empty,
                        !v.TryGetProperty("user_viewable", out var uv) || uv.ValueKind != JsonValueKind.False,
                        !v.TryGetProperty("user_editable", out var ue) || ue.ValueKind != JsonValueKind.False,
                        ProvisioningJson.Str(v, "rules") ?? string.Empty)).Where(v => v.Env.Length > 0).ToList()));
            }
        }

        var presets = Array(d, "expiry_presets").Where(p => p.ValueKind == JsonValueKind.Number).Select(p => p.GetInt32()).ToList();
        return new ProvisionCatalog(locations, nodes, eggs, presets.Count > 0 ? presets : [1, 3, 7, 14, 30]);
    }

    public string LocationOf(ProvisionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Locations.FirstOrDefault(l => l.Id == node.LocationId) is { } l ? $"{l.Description} ({l.Code})" : string.Empty;
    }

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static int Int(JsonElement e, string name) => (int)Long(e, name);

    private static long Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}

/// <summary>Fehler einer Egg-Regel: Ressourcen-Schlüssel und optional ein Wert (z. B. die Grenze).</summary>
public sealed record EggRuleError(string Key, string? Argument = null);

/// <summary>
/// Laravel-Regeln eines Eggs (Teilmenge, die auch die Website prüft: required, nullable, string, numeric, integer,
/// boolean, max, min, between, in, alpha_dash, alpha_num, digits, url, regex, size). Unbekanntes wird ignoriert –
/// das Panel prüft ohnehin.
/// </summary>
public sealed partial record EggRules(
    bool Required,
    bool Nullable,
    string Kind,
    double? Min,
    double? Max,
    IReadOnlyList<string>? In,
    string? Pattern,
    int? Digits,
    double? Size,
    bool AlphaDash,
    bool AlphaNum,
    bool Url)
{
    public const string ErrorRequired = "Provision_Rule_required";
    public const string ErrorNumber = "Provision_Rule_numeric";
    public const string ErrorInteger = "Provision_Rule_integer";
    public const string ErrorBoolean = "Provision_Rule_boolean";
    public const string ErrorMin = "Provision_Rule_min";
    public const string ErrorMax = "Provision_Rule_max";
    public const string ErrorMinLength = "Provision_Rule_min_length";
    public const string ErrorMaxLength = "Provision_Rule_max_length";
    public const string ErrorIn = "Provision_Rule_in";
    public const string ErrorFormat = "Provision_Rule_format";
    public const string ErrorDigits = "Provision_Rule_digits";
    public const string ErrorUrl = "Provision_Rule_url";

    public bool IsNumeric => Kind is "numeric" or "integer";

    public static EggRules Parse(string? rules)
    {
        bool required = false, nullable = false, alphaDash = false, alphaNum = false, url = false;
        var kind = "string";
        double? min = null, max = null, size = null;
        int? digits = null;
        IReadOnlyList<string>? choices = null;
        string? pattern = null;
        foreach (var raw in (rules ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = raw.IndexOf(':', StringComparison.Ordinal);
            var name = (colon < 0 ? raw : raw[..colon]).ToLowerInvariant();
            var arg = colon < 0 ? string.Empty : raw[(colon + 1)..];
            switch (name)
            {
                case "required":
                    required = true;
                    break;
                case "nullable":
                    nullable = true;
                    break;
                case "string" or "numeric" or "integer" or "boolean":
                    kind = name;
                    break;
                case "min":
                    min = Number(arg);
                    break;
                case "max":
                    max = Number(arg);
                    break;
                case "between":
                    var parts = arg.Split(',');
                    if (parts.Length == 2)
                    {
                        min = Number(parts[0]);
                        max = Number(parts[1]);
                    }

                    break;
                case "size":
                    size = Number(arg);
                    break;
                case "digits":
                    digits = (int?)Number(arg);
                    break;
                case "in":
                    choices = arg.Split(',').Select(v => v.Trim().Trim('"')).Where(v => v.Length > 0).ToList();
                    break;
                case "alpha_dash":
                    alphaDash = true;
                    break;
                case "alpha_num":
                    alphaNum = true;
                    break;
                case "url":
                    url = true;
                    break;
                case "regex":
                    pattern = arg;
                    break;
            }
        }

        return new EggRules(required, nullable, kind, min, max, choices, pattern, digits, size, alphaDash, alphaNum, url);
    }

    /// <summary>Prüft einen Wert wie Laravel (Länge bei Text, Wert bei Zahlen); leer ist ok, außer bei required.</summary>
    public EggRuleError? Validate(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length == 0)
        {
            return Required ? new EggRuleError(ErrorRequired) : null;
        }

        if (Kind == "boolean" && v is not ("0" or "1" or "true" or "false"))
        {
            return new EggRuleError(ErrorBoolean);
        }

        double? number = null;
        if (IsNumeric)
        {
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                return new EggRuleError(ErrorNumber);
            }

            if (Kind == "integer" && !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return new EggRuleError(ErrorInteger);
            }

            number = n;
        }

        if (In is { } choices && !choices.Contains(v, StringComparer.Ordinal))
        {
            return new EggRuleError(ErrorIn, string.Join(", ", choices));
        }

        var measure = number ?? v.Length;
        if (Min is { } min && measure < min)
        {
            return new EggRuleError(number is null ? ErrorMinLength : ErrorMin, Format(min));
        }

        if (Max is { } max && measure > max)
        {
            return new EggRuleError(number is null ? ErrorMaxLength : ErrorMax, Format(max));
        }

        if (Size is { } size && Math.Abs(measure - size) > double.Epsilon)
        {
            return new EggRuleError(number is null ? ErrorMaxLength : ErrorMax, Format(size));
        }

        if (Digits is { } digits && (v.Length != digits || !v.All(char.IsAsciiDigit)))
        {
            return new EggRuleError(ErrorDigits, digits.ToString(CultureInfo.InvariantCulture));
        }

        if ((AlphaDash && !AlphaDashPattern().IsMatch(v)) || (AlphaNum && !v.All(char.IsLetterOrDigit)))
        {
            return new EggRuleError(ErrorFormat);
        }

        if (Url && !(Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)))
        {
            return new EggRuleError(ErrorUrl);
        }

        if (Pattern is { } pattern && ToRegex(pattern) is { } regex && !regex.IsMatch(v))
        {
            return new EggRuleError(ErrorFormat);
        }

        return null;
    }

    /// <summary>PHP-Regex <c>/…/flags</c> → .NET; nicht übersetzbare Muster prüft nur das Panel.</summary>
    internal static Regex? ToRegex(string pattern)
    {
        if (pattern.Length < 2)
        {
            return null;
        }

        var delimiter = pattern[0];
        var end = pattern.LastIndexOf(delimiter);
        if (end <= 0)
        {
            return null;
        }

        var options = RegexOptions.CultureInvariant;
        foreach (var flag in pattern[(end + 1)..])
        {
            options |= flag switch
            {
                'i' => RegexOptions.IgnoreCase,
                'm' => RegexOptions.Multiline,
                's' => RegexOptions.Singleline,
                'x' => RegexOptions.IgnorePatternWhitespace,
                _ => RegexOptions.None,
            };
        }

        try
        {
            return new Regex(pattern[1..end], options, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static double? Number(string s) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string Format(double n) => n.ToString("0.##", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[\p{L}\p{N}_-]+$")]
    private static partial Regex AlphaDashPattern();
}

/// <summary>
/// Formular „Neuer Gameserver“: Felder je gewähltem Node und Egg (Allocation, Docker-Image, Egg-Variablen als
/// <c>environment.ENV</c>), Limits und Website-Felder. Rendern und Body-Bau über den allgemeinen Editor
/// (<see cref="AdminForm"/>); Egg-Regeln prüft <see cref="Validate"/> zusätzlich.
/// </summary>
public static class ProvisionForm
{
    public const string EnvPrefix = "environment.";
    public const string Expiry = "expires_in_days";

    public static readonly IReadOnlyList<string> Visibility = ["hidden", "members", "public"];
    /// <summary>Abfrage-Typen der Website (<c>query_type</c>, ab v0.35.0 zwölf) – Reihenfolge wie die Website-Auswahl.</summary>
    public static readonly IReadOnlyList<string> QueryTypes =
    [
        "source", "quake3", "quake2", "minecraft", "mc_query", "mc_bedrock",
        "gamespy1", "gamespy3", "fivem", "samp", "satisfactory", "teeworlds",
    ];
    public static readonly IReadOnlyList<string> PasswordVisibility = ["members", "squad", "managers"];

    /// <summary>Höchstens so viele zusätzliche Ports (Liste + automatisch), wie die Website (<c>ProvisionServer::MAX_ADDITIONAL</c>).</summary>
    public const int MaxAdditional = 10;

    /// <summary>Felder für die Auswahl; <paramref name="node"/>/<paramref name="egg"/> null → nur die allgemeinen.</summary>
    public static AdminArea Area(ProvisionCatalog catalog, ProvisionNode? node, ProvisionEgg? egg)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var fields = new List<AdminField>
        {
            new("name", "AdminField_name", AdminFieldType.Text, Required: true, MaxLength: 80),
            new("description", "AdminField_description", AdminFieldType.Multiline, MaxLength: 500, HelpKey: "Provision_Help_description"),
        };
        if (node is not null)
        {
            fields.Add(new("allocation_id", "Provision_allocation", AdminFieldType.Choice,
                Choices: node.Allocations.Select(a => new AdminChoice(a.Id.ToString(CultureInfo.InvariantCulture), string.Empty, a.Display)).ToList(),
                HelpKey: "Provision_Help_allocation"));

            // v0.32.0: weitere Ports (Query/RCON/Voice) – Liste und/oder die nächsten freien nach dem Hauptport
            fields.Add(new("additional_allocations", "Provision_additional_allocations", AdminFieldType.MultiChoice,
                Choices: node.Allocations.Select(a => new AdminChoice(a.Id.ToString(CultureInfo.InvariantCulture), string.Empty, a.Display)).ToList(),
                HelpKey: "Provision_Help_additional_allocations"));
            fields.Add(new("additional_auto", "Provision_additional_auto", AdminFieldType.WholeNumber, Min: 0, Max: MaxAdditional,
                HelpKey: "Provision_Help_additional_auto"));
        }

        if (egg is not null)
        {
            fields.Add(new("docker_image", "Provision_docker_image", AdminFieldType.Choice, Required: true,
                Choices: egg.DockerImages.Select(i => new AdminChoice(i.Value, string.Empty, i.Key == i.Value ? i.Value : $"{i.Key} – {i.Value}")).ToList()));
            fields.Add(new("startup", "Provision_startup", AdminFieldType.Multiline, Required: true, MaxLength: 4000));
            foreach (var v in egg.Variables)
            {
                var rules = EggRules.Parse(v.Rules);
                fields.Add(rules.In is { Count: > 0 } choices
                    ? new(EnvPrefix + v.Env, string.Empty, AdminFieldType.Choice, rules.Required, null,
                        choices.Select(c => new AdminChoice(c, string.Empty, c)).ToList(), LabelText: v.Name, HelpText: HelpOf(v), AlwaysSend: true)
                    : new(EnvPrefix + v.Env, string.Empty, AdminFieldType.Text, rules.Required, rules.IsNumeric ? null : (int?)rules.Max,
                        LabelText: v.Name, HelpText: HelpOf(v), AlwaysSend: true));
            }
        }

        fields.AddRange(
        [
            new("memory", "Provision_memory", AdminFieldType.WholeNumber, Required: true, Min: 128, Max: 1048576, HelpKey: "Provision_Help_mb"),
            new("swap", "Provision_swap", AdminFieldType.WholeNumber, Required: true, Min: -1, Max: 1048576, HelpKey: "Provision_Help_swap"),
            new("disk", "Provision_disk", AdminFieldType.WholeNumber, Required: true, Min: 256, Max: 104857600, HelpKey: "Provision_Help_mb"),
            new("cpu", "Provision_cpu", AdminFieldType.WholeNumber, Required: true, Min: 0, Max: 100000, HelpKey: "Provision_Help_cpu"),
            new("io", "Provision_io", AdminFieldType.WholeNumber, Required: true, Min: 10, Max: 1000),
            new("threads", "Provision_threads", AdminFieldType.Text, MaxLength: 64, HelpKey: "Provision_Help_threads"),
            new("databases", "Provision_databases", AdminFieldType.WholeNumber, Required: true, Min: 0, Max: 100),
            new("backups", "Provision_backups", AdminFieldType.WholeNumber, Required: true, Min: 0, Max: 100),
            new("allocations", "Provision_allocations", AdminFieldType.WholeNumber, Required: true, Min: 0, Max: 100, HelpKey: "Provision_Help_allocations"),
            new("start_on_completion", "Provision_start", AdminFieldType.Bool),
            new(Expiry, "Provision_expiry", AdminFieldType.Choice,
                Choices: catalog.ExpiryPresets.Select(d => new AdminChoice(d.ToString(CultureInfo.InvariantCulture), "Provision_Days_" + d.ToString(CultureInfo.InvariantCulture))).ToList(),
                HelpKey: "Provision_Help_expiry"),
            new("slug", "AdminField_slug", AdminFieldType.Text, MaxLength: 80, HelpKey: "AdminHelp_slug"),
            new("game_id", "AdminField_game_id", AdminFieldType.Reference, Reference: "games"),
            new("squad_id", "AdminField_squad_id", AdminFieldType.Reference, Reference: "squads"),
            new("visibility", "AdminField_visibility", AdminFieldType.Choice, Required: true, Choices: Visibility.Select(v => new AdminChoice(v, "AdminChoice_" + v)).ToList()),
            new("query_type", "AdminField_query_type", AdminFieldType.Choice, Required: true, Choices: QueryTypes.Select(v => new AdminChoice(v, "AdminChoice_" + v)).ToList()),
            new("query_enabled", "AdminField_query_enabled", AdminFieldType.Bool),
            new("query_port", "AdminField_query_port", AdminFieldType.WholeNumber, Min: 1, Max: 65535),
            new("join_password", "Provision_join_password", AdminFieldType.Text, MaxLength: 64),
            new("join_password_visibility", "AdminField_join_password_visibility", AdminFieldType.Choice, Choices: PasswordVisibility.Select(v => new AdminChoice(v, "AdminChoice_" + v)).ToList()),
        ]);

        return new AdminArea("provision", "Provision_Title", "servers.create", "servers:manage", "servers:manage", "servers/provision", fields,
            CreatePath: "servers/provision");
    }

    /// <summary>Vorgaben: Werte der Egg-Variablen, Startbefehl und Image des Eggs, übliche Limits; Bisheriges bleibt.</summary>
    public static AdminFormValues Defaults(ProvisionEgg? egg, AdminFormValues? keep = null)
    {
        var values = new AdminFormValues
        {
            ["memory"] = "4096",
            ["swap"] = "0",
            ["disk"] = "20480",
            ["cpu"] = "200",
            ["io"] = "500",
            ["databases"] = "0",
            ["backups"] = "2",
            ["allocations"] = "1",
            ["start_on_completion"] = true,
            ["visibility"] = "hidden",
            ["query_type"] = "source",
            ["query_enabled"] = true,
        };
        if (egg is not null)
        {
            values["docker_image"] = egg.DockerImage;
            values["startup"] = egg.Startup;
            foreach (var v in egg.Variables)
            {
                values[EnvPrefix + v.Env] = v.Default;
            }
        }

        if (keep is not null)
        {
            // Eingaben behalten – außer Egg-abhängigen Feldern, die zum neuen Egg passen müssen
            foreach (var (key, value) in keep.Where(k => !k.Key.StartsWith(EnvPrefix, StringComparison.Ordinal) && k.Key is not ("docker_image" or "startup" or "allocation_id")))
            {
                values[key] = value;
            }
        }

        return values;
    }

    /// <summary>Allgemeine Feldprüfung plus Egg-Regeln; Schlüssel → Fehler (Ressourcen-Schlüssel, ggf. mit „|Wert“).</summary>
    public static IReadOnlyDictionary<string, string> Validate(AdminArea area, ProvisionEgg? egg, AdminFormValues values)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(values);
        var errors = AdminForm.Validate(area, values, isNew: true).ToDictionary(StringComparer.Ordinal);
        foreach (var v in egg?.Variables ?? [])
        {
            var key = EnvPrefix + v.Env;
            if (!errors.ContainsKey(key) && EggRules.Parse(v.Rules).Validate(values.Text(key).Trim()) is { } error)
            {
                errors[key] = error.Argument is null ? error.Key : error.Key + "|" + error.Argument;
            }
        }

        var additional = AdditionalIds(values);
        var auto = long.TryParse(values.Text("additional_auto").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        if (!errors.ContainsKey("additional_allocations"))
        {
            if (additional.Contains(values.Text("allocation_id").Trim(), StringComparer.Ordinal))
            {
                errors["additional_allocations"] = "Provision_Rule_AdditionalPrimary";
            }
            else if (additional.Count + Math.Max(0, auto) > MaxAdditional)
            {
                errors["additional_allocations"] = "Provision_Rule_AdditionalMax|" + MaxAdditional.ToString(CultureInfo.InvariantCulture);
            }
        }

        return errors;
    }

    /// <summary>Body für <c>POST /v1/servers/provision</c>: Formularwerte plus Node, Nest und Egg.</summary>
    public static JsonObject Body(AdminArea area, ProvisionNode node, ProvisionEgg egg, AdminFormValues values)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(egg);
        var body = AdminForm.BuildBody(area, null, values);
        body["node_id"] = node.Id;
        body["nest_id"] = egg.NestId;
        body["egg_id"] = egg.Id;
        if (body["allocation_id"] is JsonValue alloc && alloc.TryGetValue<string>(out var a) && int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            body["allocation_id"] = id;
        }

        // Allocation-IDs als Zahlen; leer bzw. 0 nicht senden (ältere Website kennt die Felder nicht)
        body.Remove("additional_allocations");
        if (AdditionalIds(values) is { Count: > 0 } ids)
        {
            body["additional_allocations"] = new JsonArray(ids.Select(i => (JsonNode?)JsonValue.Create(int.Parse(i, NumberStyles.Integer, CultureInfo.InvariantCulture))).ToArray());
        }

        if (body["additional_auto"] is JsonValue auto && auto.TryGetValue<long>(out var count) && count <= 0)
        {
            body.Remove("additional_auto");
        }

        if (body[Expiry] is JsonValue exp && exp.TryGetValue<string>(out var e) && int.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
        {
            body[Expiry] = days;
        }

        return body;
    }

    private static List<string> AdditionalIds(AdminFormValues values) =>
        values.Text("additional_allocations").Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string? HelpOf(EggVariable v) =>
        string.Join(" · ", new[] { v.Description, v.Env, v.Rules.Length > 0 ? v.Rules : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Gameserver über die Website anlegen, Ablauf ändern, löschen, Installationsstand prüfen (Pterodactyl Application
/// API, Website v0.30.0). Recht <c>servers.create</c> mit 2FA, Scope <c>servers:manage</c>; den Rest prüft der Server.
/// </summary>
public sealed class ServerProvisioningService(HttpClient http)
{
    public const string Permission = "servers.create";
    public const string Scope = "servers:manage";

    public async Task<ProvisionCatalog> CatalogAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, "servers/provisioning/catalog", null, cancellationToken).ConfigureAwait(false);
        return ProvisionCatalog.Parse(doc.RootElement.GetProperty("data"));
    }

    /// <summary>Server anlegen; liefert Slug und Installationsstand. Idempotency-Key setzt <see cref="RoGHttpHandler"/>.</summary>
    public async Task<(string Id, string Slug, ServerProvisioning? Provisioning)> ProvisionAsync(JsonObject body, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, "servers/provision", body, cancellationToken).ConfigureAwait(false);
        var d = doc.RootElement.GetProperty("data");
        return (ProvisioningJson.Str(d, "id") ?? string.Empty, ProvisioningJson.Str(d, "slug") ?? string.Empty,
            d.TryGetProperty("provisioning", out var p) ? ServerProvisioning.Parse(p) : null);
    }

    /// <summary>Ablauf: genau eins von „ab jetzt“, „verlängern“ oder festem Zeitpunkt; alles null = kein Ablauf.</summary>
    public async Task<ServerProvisioning?> SetExpiryAsync(string server, int? inDays = null, int? extendDays = null, DateTimeOffset? at = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (inDays is { } d)
        {
            body["expires_in_days"] = d;
        }
        else if (extendDays is { } e)
        {
            body["extend_days"] = e;
        }
        else
        {
            body["expires_at"] = at?.ToString("o", CultureInfo.InvariantCulture);
        }

        using var doc = await SendAsync(HttpMethod.Patch, $"servers/{Uri.EscapeDataString(server)}/expiry", body, cancellationToken).ConfigureAwait(false);
        return ProvisioningOf(doc);
    }

    public async Task<ServerProvisioning?> RefreshAsync(string server, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"servers/{Uri.EscapeDataString(server)}/provision/refresh", null, cancellationToken).ConfigureAwait(false);
        return ProvisioningOf(doc);
    }

    /// <summary>Löscht den Server im Panel (inkl. Dateien) und auf der Website. <paramref name="confirmName"/> = Servername exakt.</summary>
    public async Task DeleteAsync(string server, string confirmName, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"servers/{Uri.EscapeDataString(server)}/provision", new JsonObject { ["confirm"] = confirmName }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Löschen erst, wenn der eingegebene Name genau dem Servernamen entspricht (wie die Website).</summary>
    public static bool ConfirmMatches(string serverName, string? typed) => typed is not null && string.Equals(serverName, typed, StringComparison.Ordinal);

    private static ServerProvisioning? ProvisioningOf(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("provisioning", out var p) ? ServerProvisioning.Parse(p) : null;

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        }

        request.Options.Set(RoGHttpHandler.SkipEtag, true);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }
}

internal static class ProvisioningJson
{
    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? ApiValues.Text(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
            : null;

    public static DateTimeOffset? Time(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
