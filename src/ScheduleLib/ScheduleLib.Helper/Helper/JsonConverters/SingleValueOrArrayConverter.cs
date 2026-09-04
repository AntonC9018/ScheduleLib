using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
// Claude
/// <summary>
/// Converts JSON that can be either a single item or an array into T[], ImmutableArray&lt;T&gt;, List&lt;T&gt;, or IEnumerable&lt;T&gt;.
/// During serialization, uses the default behavior for the collection type.
/// </summary>
public sealed class SingleValueOrArrayConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Handle arrays
        if (typeToConvert.IsArray && typeToConvert.GetArrayRank() == 1)
        {
            return true;
        }

        // Handle generic collections
        if (typeToConvert.IsGenericType)
        {
            var genericTypeDef = typeToConvert.GetGenericTypeDefinition();
            return genericTypeDef == typeof(ImmutableArray<>) ||
                   genericTypeDef == typeof(List<>) ||
                   genericTypeDef == typeof(IEnumerable<>) ||
                   genericTypeDef == typeof(ICollection<>) ||
                   genericTypeDef == typeof(IList<>) ||
                   genericTypeDef == typeof(IReadOnlyCollection<>) ||
                   genericTypeDef == typeof(IReadOnlyList<>);
        }

        return false;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type elementType;

        if (typeToConvert.IsArray)
        {
            elementType = typeToConvert.GetElementType()!;
        }
        else
        {
            elementType = typeToConvert.GetGenericArguments()[0];
        }

        var converterType = typeof(SingleValueOrArrayConverter<,>).MakeGenericType(elementType, typeToConvert);
        return (JsonConverter) Activator.CreateInstance(converterType)!;
    }
}

public sealed class SingleValueOrArrayConverter<TElement, TCollection> : JsonConverter<TCollection>
{
    public override TCollection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        TElement[] items;

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            items = JsonSerializer.Deserialize<TElement[]>(ref reader, options)!;
        }
        else
        {
            var item = JsonSerializer.Deserialize<TElement>(ref reader, options);
            items = [item!];
        }

        // Convert to target collection type
        return ConvertToCollectionType(items, typeToConvert);
    }

    public override void Write(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }

    private static TCollection ConvertToCollectionType(TElement[] items, Type targetType)
    {
        // Handle arrays
        if (targetType.IsArray)
        {
            return (TCollection) (object) items;
        }

        // Handle generic types
        if (targetType.IsGenericType)
        {
            var genericTypeDef = targetType.GetGenericTypeDefinition();

            if (genericTypeDef == typeof(ImmutableArray<>))
            {
                return (TCollection) (object) ImmutableArray.Create(items);
            }
            if (genericTypeDef == typeof(List<>))
            {
                return (TCollection) (object) new List<TElement>(items);
            }
            if (genericTypeDef == typeof(IEnumerable<>) ||
                genericTypeDef == typeof(ICollection<>) ||
                genericTypeDef == typeof(IList<>) ||
                genericTypeDef == typeof(IReadOnlyCollection<>) ||
                genericTypeDef == typeof(IReadOnlyList<>))
            {
                return (TCollection) (object) items;
            }
        }

        throw new NotSupportedException($"Collection type {targetType} is not supported.");
    }
}
