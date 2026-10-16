using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoG.Core.Api.Generated;

/// <summary>JSON-Einstellungen des generierten Clients ergänzen (Haken aus NSwag: <c>UpdateJsonSerializerSettings</c>).</summary>
public partial class RoGApiClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        settings.Converters.Add(new EmptyArrayAsDictionaryConverterFactory());
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
