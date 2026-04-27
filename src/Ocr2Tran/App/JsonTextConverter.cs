using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ocr2Tran.App;

public sealed class JsonTextConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return "";
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? "";
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (TryWriteJson(writer, value))
        {
            return;
        }

        writer.WriteStringValue(value);
    }

    private static bool TryWriteJson(Utf8JsonWriter writer, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                return false;
            }

            document.RootElement.WriteTo(writer);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
