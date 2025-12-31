using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper.JsonConverters;

// mostly ChatGPT
public sealed class OnlySerializeSettersConverter<T> : JsonConverter<T>
    where T : new()
{
    // Cache properties with setters
    private static readonly PropertyInfo[] _writableProperties =
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();

    private record struct Key(JsonNamingPolicy? Policy);
    // Cache property name -> PropertyInfo mapping per naming policy
    private static readonly ConcurrentDictionary<Key, Dictionary<string, PropertyInfo>> _propertyCache = new();

    private static Dictionary<string, PropertyInfo> GetPropertyMap(JsonNamingPolicy? namingPolicy)
    {
        return _propertyCache.GetOrAdd(new(namingPolicy), np =>
        {
            var dict = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in _writableProperties)
            {
                string name = np.Policy?.ConvertName(prop.Name) ?? prop.Name;
                dict[name] = prop;
            }
            return dict;
        });
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject token, got {reader.TokenType}");
        }

        object obj = new T();
        var props = GetPropertyMap(options.PropertyNamingPolicy);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return (T) obj;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Unexpected token {reader.TokenType}");
            }

            string propertyName = reader.GetString()!;
            reader.Read(); // move to value

            // TODO: remove reflection
            if (props.TryGetValue(propertyName, out var propInfo))
            {
                object? value = JsonSerializer.Deserialize(ref reader, propInfo.PropertyType, options);
                propInfo.SetValue(obj, value);
            }
            else
            {
                reader.Skip(); // unknown property
            }
        }

        throw new JsonException("Incomplete JSON object");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite);

        foreach (var prop in props)
        {
            var propValue = prop.GetValue(value);
            writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name);
            JsonSerializer.Serialize(writer, propValue, prop.PropertyType, options);
        }

        writer.WriteEndObject();
    }
}

public sealed class OnlySerializeSettersForUserDefinedTypesConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Check if the type is a user-defined type (not a primitive, enum, string, or built-in collection)
        return !typeToConvert.IsPrimitive &&
               !typeToConvert.IsEnum &&
               typeToConvert != typeof(string) &&
               !typeof(System.Collections.IEnumerable).IsAssignableFrom(typeToConvert) &&
               !typeToConvert.IsGenericType;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // Create a converter for user-defined types
        var converterType = typeof(OnlySerializeSettersConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter) Activator.CreateInstance(converterType)!;
    }
}
