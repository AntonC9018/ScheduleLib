using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib;

public static class ScheduleSerializer
{
    public static Task Serialize(
        Schedule schedule,
        Stream outputFile,
        string hash,
        CancellationToken cancellationToken)
    {
        var model = SerializationModels.ConvertToSerializationModel(schedule, hash);
        var json = JsonSerializer.SerializeAsync(outputFile, model, SerializerOptions, cancellationToken);
        return json;
    }

    public static async Task<SerializationModels.ScheduleModel> Deserialize(
        Stream inputFile,
        CancellationToken cancellationToken)
    {
        var model = await JsonSerializer.DeserializeAsync<SerializationModels.ScheduleModel>(inputFile, SerializerOptions, cancellationToken);
        if (model is null)
        {
            throw new InvalidDataException("Could not deserialize schedule");
        }
        return model;
    }

    public static void AddToBuilder(
        ScheduleBuilder builder,
        SerializationModels.ScheduleModel schedule,
        CourseNameUnifierModule? unifier = null)
    {
        SerializationModels.ConvertToScheduleBuilder(builder, schedule);

        if (unifier is not null)
        {
            builder.EnableLookupModule();
            for (int i = 0; i < builder.Courses.Count; i++)
            {
                var courseId = new CourseId(i);
                unifier.AddSlow(builder.Courses.Ref(i).FullName, courseId);
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new NamePartsJsonConverter<OptionalNamePart>());
        options.Converters.Add(new NamePartsJsonConverter<string>());
        options.Converters.Add(new DateOnlyJsonConverter());
        options.Converters.Add(new SingleValueWrapperConverterFactory());
        options.Converters.Add(new UserDefinedTypeConverterFactory());
        var textEncoder = new TextEncoderSettings();
        textEncoder.AllowRanges(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.Latin1Supplement,
            UnicodeRanges.LatinExtendedA,
            UnicodeRanges.LatinExtendedB,
            UnicodeRanges.LatinExtendedC,
            UnicodeRanges.LatinExtendedD,
            UnicodeRanges.LatinExtendedE);
        options.Encoder = JavaScriptEncoder.Create(textEncoder);
        options.ReferenceHandler = null;
        options.WriteIndented = true;
        return options;
    }
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
}

public static class SerializationModels
{
    public sealed class RegularLessonModel
    {
        public required ImmutableArray<GroupId> Groups { get; set; }
        public required CourseId Course { get; set; }
        public required ImmutableArray<TeacherId> Teachers { get; set; }
        public required RoomId Room { get; set; }
        public required LessonType Type { get; set; }
        public required SubGroup SubGroup { get; set; }
        public required Parity Parity { get; set; }
        public required DayOfWeek DayOfWeek { get; set; }
        public required TimeSlot TimeSlot { get; set; }
        public required PeriodId Period { get; set; }
    }

    public sealed class GroupModel
    {
        public required string Name { get; set; }
    }

    public sealed class PersonModel
    {
        public required NameParts<OptionalNamePart> FirstName { get; set; }
        public required NameParts<string?> LastName { get; set; }
    }

    public sealed class CourseModel
    {
        public required ImmutableArray<string> Names { get; set; }
    }

    public sealed class PeriodModel
    {
        public required DateOnly Start { get; set; }
        public required DateOnly? End { get; set; }
    }

    public sealed class ScheduleModel
    {
        public required string Hash { get; set; }
        public required ImmutableArray<RegularLessonModel> RegularLessons { get; set; }
        public required ImmutableArray<GroupModel> Groups { get; set; }
        public required ImmutableArray<PersonModel> Teachers { get; set; }
        public required ImmutableArray<CourseModel> Courses { get; set; }
        public required ImmutableArray<PeriodModel> Periods { get; set; }
    }

    public static ScheduleModel ConvertToSerializationModel(
        Schedule schedule,
        string hash)
    {
        var regularLessons = schedule.RegularLessons.Select(rl => new RegularLessonModel
        {
            Groups = [.. rl.Lesson.Groups],
            Course = rl.Lesson.Course,
            Teachers = rl.Lesson.Teachers,
            Room = rl.Lesson.Room,
            Type = rl.Lesson.Type,
            SubGroup = rl.Lesson.SubGroup,
            Parity = rl.Date.Parity,
            DayOfWeek = rl.Date.DayOfWeek,
            TimeSlot = rl.Date.TimeSlot,
            Period = rl.Date.Period,
        }).ToImmutableArray();

        var groups = schedule.Groups.Select(g => new GroupModel
        {
            Name = g.Name,
        }).ToImmutableArray();

        var teachers = schedule.Teachers.Select(t => new PersonModel
        {
            FirstName = t.PersonName.FirstName,
            LastName = t.PersonName.LastName,
        }).ToImmutableArray();

        var courses = schedule.Courses.Select(c => new CourseModel
        {
            Names = c.Names,
        }).ToImmutableArray();

        var periods = schedule.Periods.Select(p => new PeriodModel
        {
            Start = p.Start,
            End = p.End,
        }).ToImmutableArray();

        var model = new ScheduleModel
        {
            Hash = hash,
            RegularLessons = regularLessons,
            Groups = groups,
            Teachers = teachers,
            Courses = courses,
            Periods = periods,
        };
        return model;
    }

    // NOTE: the issue is that multiple serialized things can't be merged easily.
    // need to do some id remaps and value merges for this to work.
    public static void ConvertToScheduleBuilder(
        ScheduleBuilder builder,
        ScheduleModel schedule)
    {
        Debug.Assert(builder.Courses.Count == 0);
        foreach (var s in schedule.Courses)
        {
            builder.Courses.List.Add(new()
            {
                Names = s.Names,
            });
        }

        Debug.Assert(builder.Groups.Count == 0);
        foreach (var s in schedule.Groups)
        {
            builder.Group(s.Name);
        }

        Debug.Assert(builder.Periods.Count == 0);
        foreach (var p in schedule.Periods)
        {
            var per = new PeriodBuilderModel
            {
                Start = p.Start,
            };
            if (p.End is { } e)
            {
                per.EndExclusive = e;
            }

            builder.Periods.List.Add(per);
        }

        Debug.Assert(builder.Teachers.Count == 0);
        foreach (var t in schedule.Teachers)
        {
            var teacher = builder.Teacher(new TeacherBuilderModel.NameModel
            {
                FirstName = t.FirstName,
                LastName = new(t.LastName),
            });
            _ = teacher;
        }

        Debug.Assert(builder.RegularLessons.Count == 0);
        foreach (var rl in schedule.RegularLessons)
        {
            var lesson = builder.RegularLessons.New();
            lesson.Value = new();

            ref var g = ref lesson.Value.General;
            g.Course = rl.Course;
            g.Room = rl.Room;
            g.Teachers = rl.Teachers.ToList();
            g.Type = rl.Type;
            g.Period = rl.Period;

            ref var date = ref lesson.Value.Date;
            date.DayOfWeek = rl.DayOfWeek;
            date.TimeSlot = rl.TimeSlot;
            date.Parity = rl.Parity;

            ref var group = ref lesson.Value.Group;
            group.SubGroup = rl.SubGroup;
            group.Groups = [.. rl.Groups];
        }
    }
}


// Most of the below is ChatGPT, refactored manually.

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

file class AccessorsBase
{
    public required Type ValueType { get; init; }
}

file class Accessors<T, TValue> : AccessorsBase
{
    public required Func<TValue?, T> Creator { get; init; }
    public required Func<T, TValue?> Extractor { get; init; }
}


// ChatGPT
file sealed class SingleValueWrapperConverter<T, TValue> : JsonConverter<T>
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

file sealed class SingleValueWrapperConverterFactory : JsonConverterFactory
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

file sealed class NamePartsJsonConverter<T> : JsonConverter<NameParts<T>>
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
        if (value[0] is OptionalNamePart s
            && s.Short is not null
            && new Word(s.Short).Span.Shortened.Value.SequenceEqual("G."))
        {
            Console.WriteLine("Hello");
        }

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

file sealed class UserDefinedTypeConverterFactory : JsonConverterFactory
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
        var converterType = typeof(OnlySettersConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter) Activator.CreateInstance(converterType)!;
    }
}

file sealed class OnlySettersConverter<T> : JsonConverter<T>
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


file sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetString() is not { } dateStr)
        {
            return DateOnly.MinValue;
        }
        var dt = DateTime.Parse(dateStr);
        return DateOnly.FromDateTime(dt);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
