// Most of the below is ChatGPT, refactored manually.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper.JsonConverters;

public sealed class SingleValueWrapperConverter<T, TValue> : JsonConverter<T>
{
    private readonly Accessors<T, TValue> _accessor = Accessors.Get<T, TValue>();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
        return _accessor.Creator(value);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, _accessor.Extractor(value), options);
    }
}

public sealed class SingleValueWrapperConverterFactory : JsonConverterFactory
{
    // Cache of created converters per wrapped type
    private static readonly ConcurrentDictionary<Type, JsonConverter> _converterCache = new();

    public override bool CanConvert(Type typeToConvert)
    {
        // Skip primitives and enums
        if (typeToConvert.IsPrimitive || typeToConvert.IsEnum)
        {
            return false;
        }
        if (_converterCache.ContainsKey(typeToConvert))
        {
            return true;
        }

        if (Accessors.TryAddAccessorsFor(typeToConvert) is { } accessor)
        {
            _ = accessor;
            return true;
        }

        return false;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (_converterCache.TryGetValue(typeToConvert, out var cached))
        {
            return cached;
        }

        var accessor = Accessors.Get(typeToConvert);
        var converterType = typeof(SingleValueWrapperConverter<,>).MakeGenericType(typeToConvert, accessor!.ValueType);
        var converter = (JsonConverter) Activator.CreateInstance(converterType)!;

        _converterCache[typeToConvert] = converter;
        return converter;
    }
}

// mostly Chat GPT
file static class Accessors
{
    private static ConcurrentDictionary<Type, AccessorsBase?> _accessors = new();

    public static Accessors<T, TValue> Get<T, TValue>()
    {
        var t = _accessors[typeof(T)];
        Debug.Assert(t != null);
        Debug.Assert(t.ValueType == typeof(TValue));
        return (Accessors<T, TValue>) t;
    }

    public static AccessorsBase? Get(Type type)
    {
        var t = _accessors[type];
        return t;
    }

    public static AccessorsBase? TryAddAccessorsFor(Type type)
    {
        if (_accessors.TryGetValue(type, out var accessor))
        {
            return accessor;
        }

        var info = FindInfo(type);
        if (info == default)
        {
            _accessors.TryAdd(type, null);
            return null;
        }

        var createMethod = CreateMethod.MakeGenericMethod(type, info.ValueType);
        var ret = (AccessorsBase) createMethod.Invoke(null, [info.Constructor, info.Member])!;
        ret = _accessors.GetOrAdd(type, ret);
        return ret;
    }

    private static (ConstructorInfo Constructor, MemberInfo Member, Type ValueType) FindInfo(
        Type type)
    {
        // Get all public instance constructors with exactly one parameter
        var constructors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(c => (Param: c.GetParameters(), Constructor: c))
            .Where(x => x.Param.Length == 1)
            .ToArray();

        // Candidate public instance properties (declared only) and fields
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        var fields = type
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToArray();

        // We'll collect (constructor, matchingMember) pairs where matchingMember.Type == constructor param type
        (ConstructorInfo Constructor, MemberInfo Member, Type ValueType) wholeMatch = default;

        foreach (var (paramArr, ctor) in constructors)
        {
            var paramType = paramArr[0].ParameterType;

            // find members whose type equals the constructor parameter type
            var matchingProps = properties.Where(p => p.PropertyType == paramType).Cast<MemberInfo>();
            var matchingFields = fields.Where(f => f.FieldType == paramType).Cast<MemberInfo>();
            using var matchingMembers = matchingProps.Concat(matchingFields).GetEnumerator();

            if (!matchingMembers.MoveNext())
            {
                continue;
            }
            var match = matchingMembers.Current;
            if (matchingMembers.MoveNext())
            {
                continue;
            }

            if (wholeMatch != default)
            {
                return default;
            }
            wholeMatch = (ctor, match, paramType);
        }
        return wholeMatch;
    }


    private static readonly MethodInfo CreateMethod = typeof(Accessors).GetMethod(
        nameof(Create),
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Accessors<T, TValue> Create<T, TValue>(
        ConstructorInfo constructor,
        MemberInfo getterPropOrField)
    {
        // Compile: (TValue v) => new T(v)
        var valueParam = Expression.Parameter(typeof(TValue), "value");
        var newExpr = Expression.New(constructor, valueParam);
        var creator = Expression.Lambda<Func<TValue?, T>>(newExpr, valueParam).Compile();

        // Compile: (T t) => t.Value
        var tParam = Expression.Parameter(typeof(T), "t");
        Expression memberAccess = getterPropOrField is PropertyInfo prop
            ? Expression.Property(tParam, prop)
            : Expression.Field(tParam, (FieldInfo) getterPropOrField);

        var extractor = Expression.Lambda<Func<T, TValue?>>(memberAccess, tParam).Compile();
        return new()
        {
            Creator = creator,
            Extractor = extractor,
            ValueType = typeof(TValue),
        };
    }
}

internal abstract class AccessorsBase
{
    public required Type ValueType { get; init; }
}

internal sealed class Accessors<T, TValue> : AccessorsBase
{
    public required Func<TValue?, T> Creator { get; init; }
    public required Func<T, TValue?> Extractor { get; init; }
}
