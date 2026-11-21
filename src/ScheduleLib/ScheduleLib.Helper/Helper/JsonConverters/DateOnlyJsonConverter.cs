using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper.JsonConverters;

public sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    // Parsing used to go through the OS culture, so day-first files don't read on a
    // month-first machine. Both sides are pinned; the data on disk is day-first.
    private const string WrittenFormat = "dd/MM/yyyy";
    private static readonly string[] AcceptedFormats = [WrittenFormat, "yyyy-MM-dd"];

    public override DateOnly Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return DateOnly.MinValue;
        }
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new UnexpectedJsonTokenException(reader.TokenType, JsonTokenExpectation.String);
        }
        var dateStr = reader.GetString()!;
        return DateOnly.ParseExact(
            dateStr,
            AcceptedFormats,
            CultureInfo.InvariantCulture);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateOnly value,
        JsonSerializerOptions options)
    {
        var formatted = value.ToString(WrittenFormat, CultureInfo.InvariantCulture);
        writer.WriteStringValue(formatted);
    }
}
