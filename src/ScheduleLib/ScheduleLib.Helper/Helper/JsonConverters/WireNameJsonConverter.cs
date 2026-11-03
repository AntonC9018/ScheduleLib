using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper.JsonConverters;

/// <summary>Writes an enum as its wire string, also as a dictionary key. Reading is unsupported.</summary>
public abstract class WireNameJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    protected abstract string ToWire(T value);

    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException($"{typeof(T).Name} is written to output only.");

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(ToWire(value));

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
        => writer.WritePropertyName(ToWire(value));
}
