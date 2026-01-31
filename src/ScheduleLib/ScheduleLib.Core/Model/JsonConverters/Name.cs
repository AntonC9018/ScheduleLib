using System.Text.Json;
using System.Text.Json.Serialization;
using ScheduleLib.Parsing;

namespace ScheduleLib.JsonConverters;

// Claude
public sealed class NameJsonConverter : JsonConverter<Name>
{
    public override Name Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException("Name cannot be null");
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string value for Name");
        }

        string? nameString = reader.GetString();
        if (string.IsNullOrEmpty(nameString))
        {
            throw new JsonException("Name string cannot be empty");
        }

        try
        {
            return NameHelper.Parse(nameString);
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException($"Failed to parse name: {ex.Message}", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Name value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // TODO: don't allocate a string builder and string.
        var stringValue = value.ToString(NameToStringParams.IncludeEverything);

        writer.WriteStringValue(stringValue);
    }
}
