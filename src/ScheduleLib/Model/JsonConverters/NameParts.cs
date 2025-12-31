using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.JsonConverters;

public sealed class NamePartsJsonConverter<T> : JsonConverter<NameParts<T>>
{
    public override NameParts<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException();
        }

        var nameParts = new NameParts<T>();
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (i >= nameParts.Length)
            {
                throw new JsonException("Too many elements in NameParts array");
            }

            T? item;
            if (reader.TokenType == JsonTokenType.Null)
            {
                item = default;
            }
            else
            {
                item = JsonSerializer.Deserialize<T>(ref reader, options);
            }

            nameParts[i] = item!;
            i++;
        }

        return nameParts;
    }

    public override void Write(Utf8JsonWriter writer, NameParts<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        int lastNullStart = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(value[i], default))
            {
                lastNullStart = -1;
                continue;
            }
            if (lastNullStart == -1)
            {
                lastNullStart = i;
            }
        }
        for (int i = 0; i < value.Length; i++)
        {
            if (lastNullStart == -1 || i < lastNullStart)
            {
                JsonSerializer.Serialize(writer, value[i], options);
            }
        }
        writer.WriteEndArray();
    }
}
