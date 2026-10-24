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

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetString() is not { } dateStr)
        {
            return DateOnly.MinValue;
        }
        return DateOnly.ParseExact(dateStr, AcceptedFormats, CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        global::System.Text.Json.Utf8JsonWriter utf8JsonWriter = writer;
        string value2 = value.ToString(WrittenFormat, CultureInfo.InvariantCulture);
        utf8JsonWriter.WriteStringValue(value2);
    }
}
