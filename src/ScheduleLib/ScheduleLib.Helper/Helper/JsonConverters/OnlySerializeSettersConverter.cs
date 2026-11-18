using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper.JsonConverters;

// mostly ChatGPT
public sealed class OnlySerializeSettersConverter<T> : JsonConverter<T>
    where T : new()
{
    /// <summary>A settable property and what the JSON must supply for it.</summary>
    private sealed record PropertyContract(int Index, PropertyInfo Property, bool IsRequired, bool AllowsNull);

    // Cache properties with setters
    private static readonly PropertyContract[] _writableProperties = CreateContracts();

    private static PropertyContract[] CreateContracts()
    {
        var nullability = new NullabilityInfoContext();
        return typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select((p, index) => new PropertyContract(
                Index: index,
                Property: p,
                IsRequired: p.IsDefined(typeof(RequiredMemberAttribute)),
                AllowsNull: AllowsNull(p, nullability)))
            .ToArray();
    }

    private static bool AllowsNull(PropertyInfo property, NullabilityInfoContext nullability)
    {
        if (property.PropertyType.IsValueType)
        {
            return Nullable.GetUnderlyingType(property.PropertyType) is not null;
        }
        return nullability.Create(property).WriteState != NullabilityState.NotNull;
    }

    private record struct Key(JsonNamingPolicy? Policy);

    // Cache property name -> contract mapping per naming policy
    private static readonly ConcurrentDictionary<Key, Dictionary<string, PropertyContract>> _propertyCache = new();

    private static Dictionary<string, PropertyContract> GetPropertyMap(JsonNamingPolicy? namingPolicy)
    {
        Key key = new(namingPolicy);
        Func<Key, Dictionary<string, PropertyContract>> valueFactory = np =>
        {
            var dict = new Dictionary<string, PropertyContract>(StringComparer.OrdinalIgnoreCase);
            foreach (var contract in _writableProperties)
            {
                string name = np.Policy?.ConvertName(contract.Property.Name) ?? contract.Property.Name;
                dict[name] = contract;
            }

            return dict;
        };
        return _propertyCache.GetOrAdd(key, valueFactory);
    }

    /// <summary>
    /// Reads an object, rejecting a missing <c>required</c> property and a null value for a
    /// non-nullable one, so malformed input fails as a <see cref="JsonException"/>.
    /// </summary>
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new UnexpectedJsonTokenException(reader.TokenType, JsonTokenExpectation.StartObject);
        }

        object obj = new T();
        var props = GetPropertyMap(options.PropertyNamingPolicy);
        var supplied = new bool[_writableProperties.Length];

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                RejectMissingRequired(supplied);
                return (T) obj;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new UnexpectedJsonTokenException(reader.TokenType, JsonTokenExpectation.PropertyName);
            }

            string propertyName = reader.GetString()!;
            reader.Read(); // move to value

            // TODO: remove reflection
            if (props.TryGetValue(propertyName, out var contract))
            {
                object? value = JsonSerializer.Deserialize(
                    ref reader,
                    contract.Property.PropertyType,
                    options);
                if (value is null && !contract.AllowsNull)
                {
                    throw new JsonException($"{typeof(T).Name}.{contract.Property.Name} must not be null");
                }
                contract.Property.SetValue(obj: obj, value: value);
                supplied[contract.Index] = true;
            }
            else
            {
                reader.Skip(); // unknown property
            }
        }

        throw new JsonException("Incomplete JSON object");
    }

    private static void RejectMissingRequired(bool[] supplied)
    {
        var missing = _writableProperties.FirstOrDefault(c => c.IsRequired && !supplied[c.Index]);
        if (missing is not null)
        {
            throw new JsonException($"{typeof(T).Name}.{missing.Property.Name} is required");
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
            {
                if (!p.CanRead)
                {
                    return false;
                }

                if (p.CanWrite)
                {
                    return true;
                }

                return false;
            });

        foreach (var prop in props)
        {
            var propValue = prop.GetValue(value);
            writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name);
            JsonSerializer.Serialize(
                writer,
                propValue,
                prop.PropertyType,
                options);
        }

        writer.WriteEndObject();
    }
}

public sealed class OnlySerializeSettersForUserDefinedTypesConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Check if the type is a user-defined type (not a primitive, enum, string, or built-in collection)
        if (typeToConvert.IsPrimitive)
        {
            return false;
        }

        if (typeToConvert.IsEnum)
        {
            return false;
        }

        if (typeToConvert == typeof(string))
        {
            return false;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(typeToConvert))
        {
            return false;
        }

        if (!typeToConvert.IsGenericType)
        {
            return true;
        }

        return false;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // Create a converter for user-defined types
        var converterType = typeof(OnlySerializeSettersConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter) Activator.CreateInstance(converterType)!;
    }
}
