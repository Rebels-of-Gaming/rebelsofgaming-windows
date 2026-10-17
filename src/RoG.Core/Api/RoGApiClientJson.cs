using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoG.Core.Api.Generated;

/// <summary>JSON-Einstellungen des generierten Clients ergänzen (Haken aus NSwag: <c>UpdateJsonSerializerSettings</c>).</summary>
public partial class RoGApiClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.Converters.Add(new EmptyArrayAsDictionaryConverterFactory());
        settings.Converters.Add(new EmptyArrayAsOpenObjectConverterFactory());

        // Nicht gesetzte Felder nicht mitsenden: Ein PATCH mit `"birthday": null` würde sonst Daten löschen, die die App
        // gar nicht ändern wollte. Zum Leeren eines Textfelds einen leeren String senden.
        settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }
}

/// <summary>
/// PHP gibt ein leeres assoziatives Array als <c>[]</c> statt <c>{}</c> aus. Für Felder, die laut Spec ein Objekt
/// (Wörterbuch) sind, wird <c>[]</c> deshalb als leeres Wörterbuch gelesen statt die ganze Antwort zu verwerfen.
/// </summary>
internal sealed class EmptyArrayAsDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() is var def
        && (def == typeof(IDictionary<,>) || def == typeof(Dictionary<,>))
        && typeToConvert.GetGenericArguments()[0] == typeof(string)
        // `AdditionalProperties` ([JsonExtensionData]) bleiben bei System.Text.Json
        && typeToConvert.GetGenericArguments()[1] != typeof(object)
        && typeToConvert.GetGenericArguments()[1] != typeof(JsonElement);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[1];
        return (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(valueType))!;
    }

    private sealed class Converter<TValue> : JsonConverter<IDictionary<string, TValue>>
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert == typeof(IDictionary<string, TValue>) || typeToConvert == typeof(Dictionary<string, TValue>);

        public override IDictionary<string, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                reader.Skip();
                return new Dictionary<string, TValue>();
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            var result = new Dictionary<string, TValue>();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Objekt erwartet, {reader.TokenType} gefunden.");
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                result[key] = JsonSerializer.Deserialize<TValue>(ref reader, options)!;
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, IDictionary<string, TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, item) in value)
            {
                writer.WritePropertyName(key);
                JsonSerializer.Serialize(writer, item, options);
            }

            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// Dasselbe für offene Objekte der Spec (Klassen nur mit <c>AdditionalProperties</c>, z. B. <c>profile.accounts</c>):
/// <c>[]</c> wird ein leeres Objekt. Ohne das scheitert z. B. jedes Profil ohne verknüpfte Konten.
/// </summary>
internal sealed class EmptyArrayAsOpenObjectConverterFactory : JsonConverterFactory
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> s_extension = new();

    internal static System.Reflection.PropertyInfo? ExtensionProperty(Type type) => s_extension.GetOrAdd(type, static t =>
    {
        if (!t.IsClass || t.Namespace != typeof(RoGApiClient).Namespace || t.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        var props = t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return props.Length == 1
            && props[0].PropertyType == typeof(IDictionary<string, object>)
            && props[0].IsDefined(typeof(JsonExtensionDataAttribute), inherit: false)
            ? props[0]
            : null;
    });

    public override bool CanConvert(Type typeToConvert) => ExtensionProperty(typeToConvert) is not null;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T>
        where T : class, new()
    {
        private static readonly System.Reflection.PropertyInfo s_property = ExtensionProperty(typeof(T))!;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.StartArray:
                    reader.Skip();
                    return new T();
                case JsonTokenType.StartObject:
                    var values = new Dictionary<string, object>();
                    using (var doc = JsonDocument.ParseValue(ref reader))
                    {
                        foreach (var p in doc.RootElement.EnumerateObject())
                        {
                            values[p.Name] = p.Value.Clone();
                        }
                    }

                    var result = new T();
                    s_property.SetValue(result, values);
                    return result;
                default:
                    throw new JsonException($"Objekt erwartet, {reader.TokenType} gefunden.");
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (s_property.GetValue(value) is IDictionary<string, object> values)
            {
                foreach (var (key, item) in values)
                {
                    writer.WritePropertyName(key);
                    JsonSerializer.Serialize(writer, item, options);
                }
            }

            writer.WriteEndObject();
        }
    }
}
