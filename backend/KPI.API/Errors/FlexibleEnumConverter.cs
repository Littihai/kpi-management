using System.Text.Json;
using System.Text.Json.Serialization;

namespace KPI.API.Errors;

/// <summary>
/// Reads an enum from either its name ("InProgress", any case) or its number, and always writes the number,
/// so existing responses keep their shape while requests may use readable names.
/// </summary>
public sealed class FlexibleEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(FlexibleEnumConverter<>).MakeGenericType(typeToConvert))!;
}

public sealed class FlexibleEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (Enum.TryParse<T>(text, ignoreCase: true, out var named) && Enum.IsDefined(named)) return named;
            throw new JsonException($"'{text}' is not a valid {typeof(T).Name}.");
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        {
            var value = (T)Enum.ToObject(typeof(T), number);
            if (Enum.IsDefined(value)) return value;
        }

        throw new JsonException($"Expected a {typeof(T).Name} name or number.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(Convert.ToInt64(value));
}
