using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper.JsonConverters;

public sealed class OnlySerializeSettersConverter<T> : JsonConverter<T>
    where T : new()
{
    private sealed record PropertyContract(
        int Index,
        PropertyInfo Property,
        bool IsRequired,
        bool AllowsNull,
        bool ElementsAllowNull);

    private static readonly PropertyContract[] _writableProperties = CreateContracts();

    private static PropertyContract[] CreateContracts()
    {
        var nullability = new NullabilityInfoContext();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var writable = properties.Where(IsSerialized);
        return writable
            .Select((p, index) => CreateContract(p, index, nullability))
            .ToArray();
    }

    private static PropertyContract CreateContract(PropertyInfo property, int index, NullabilityInfoContext nullability)
    {
        var isRequired = IsRequired(property);
        var allowsNull = AllowsNull(property, nullability);
        var elementsAllowNull = ElementsAllowNull(property, nullability);
        return new PropertyContract(
            Index: index,
            Property: property,
            IsRequired: isRequired,
            AllowsNull: allowsNull,
            ElementsAllowNull: elementsAllowNull);
    }

    private static bool IsSerialized(PropertyInfo property)
    {
        if (!property.CanRead || !property.CanWrite)
        {
            return false;
        }
        if (property.GetIndexParameters().Length > 0)
        {
            return false;
        }
        return true;
    }

    private static bool IsRequired(PropertyInfo property)
    {
        if (property.IsDefined(typeof(RequiredMemberAttribute)))
        {
            return true;
        }
        if (property.IsDefined(typeof(JsonRequiredAttribute)))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reads the element nullability of an array or a single-argument generic collection.
    /// Other shapes, and value-type elements, are not checked.
    /// </summary>
    private static bool ElementsAllowNull(PropertyInfo property, NullabilityInfoContext nullability)
    {
        var type = property.PropertyType;
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return true;
        }
        var info = nullability.Create(property);
        var element = type.IsArray ? info.ElementType : SingleGenericArgument(info);
        if (element is null || element.Type.IsValueType)
        {
            return true;
        }
        if (element.ReadState == NullabilityState.NotNull)
        {
            return false;
        }
        return true;

        static NullabilityInfo? SingleGenericArgument(NullabilityInfo info)
        {
            if (info.GenericTypeArguments.Length != 1)
            {
                return null;
            }
            return info.GenericTypeArguments[0];
        }
    }

    private static bool AllowsNull(PropertyInfo property, NullabilityInfoContext nullability)
    {
        var type = property.PropertyType;
        if (type.IsValueType)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is null)
            {
                return false;
            }
            return true;
        }
        var writeState = nullability.Create(property).WriteState;
        if (writeState == NullabilityState.NotNull)
        {
            return false;
        }
        return true;
    }

    private record struct Key(JsonNamingPolicy? Policy);

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
            reader.Read();

            // TODO: remove reflection
            if (props.TryGetValue(propertyName, out var contract))
            {
                object? value = JsonSerializer.Deserialize(
                    ref reader,
                    contract.Property.PropertyType,
                    options);
                RejectInvalidValue(contract, value);
                contract.Property.SetValue(obj: obj, value: value);
                supplied[contract.Index] = true;
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Incomplete JSON object");
    }

    private static void RejectInvalidValue(PropertyContract contract, object? value)
    {
        if (value is null)
        {
            if (!contract.AllowsNull)
            {
                throw new InvalidJsonMemberException(typeof(T), contract.Property.Name, JsonMemberProblem.Null);
            }
            return;
        }
        if (!contract.ElementsAllowNull && ContainsNull(value))
        {
            throw new InvalidJsonMemberException(typeof(T), contract.Property.Name, JsonMemberProblem.NullElement);
        }
    }

    private static bool ContainsNull(object value)
    {
        if (value is not IEnumerable items)
        {
            return false;
        }
        foreach (object? item in items)
        {
            if (item is null)
            {
                return true;
            }
        }
        return false;
    }

    private static void RejectMissingRequired(bool[] supplied)
    {
        foreach (var contract in _writableProperties)
        {
            if (!contract.IsRequired)
            {
                continue;
            }
            if (!supplied[contract.Index])
            {
                throw new InvalidJsonMemberException(typeof(T), contract.Property.Name, JsonMemberProblem.Missing);
            }
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var contract in _writableProperties)
        {
            var prop = contract.Property;
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
        var converterType = typeof(OnlySerializeSettersConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter) Activator.CreateInstance(converterType)!;
    }
}
