using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;
using static System.Enum;

namespace ScheduleLib.Application.Core.Topics;

public sealed class EnumConverterOptions<T> where T : struct, Enum
{
    internal List<T>? Included { get; private set; }
    internal List<T>? Ignored { get; private set; }

    public void Include(T value)
    {
        if (Ignored != null)
        {
            throw new ConflictingEnumConverterFilterException(
                EnumFilterConflictKind.IncludeWhileIgnoring,
                typeof(T));
        }
        Included ??= new();
        Included.Add(value);
    }

    public void Ignore(T value)
    {
        if (Included != null)
        {
            throw new ConflictingEnumConverterFilterException(
                EnumFilterConflictKind.IgnoreWhileIncluding,
                typeof(T));
        }
        Ignored ??= new();
        Ignored.Add(value);
    }
}

public sealed class ConfigurableEnumConverter<T> : DefaultTypeConverter
    where T : struct, Enum
{
    private readonly HashSet<string> _allowedNames;

    public ConfigurableEnumConverter(EnumConverterOptions<T> options)
    {
        var allValues = GetValues(typeof(T))
            .Cast<T>();

        var filtered = allValues.Where(v =>
        {
            var name = v.ToString();
            return name != "Invalid" && name != "Count";
        });

        IEnumerable<T> final;

        if (options.Included != null)
        {
            final = options.Included;
        }
        else if (options.Ignored != null)
        {
            final = filtered.Where(v => !options.Ignored.Contains(v));
        }
        else
        {
            final = new EnumMembers<T>();
        }

        _allowedNames = final
            .Select(v => v.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public override object ConvertFromString(
        string? text,
        IReaderRow row,
        MemberMapData memberMapData)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw InvalidEnumConfigurationValueException.ForEmptyValue(
                this,
                memberMapData,
                text,
                row.Context,
                typeof(T));
        }

        if (int.TryParse(text, out _))
        {
            throw InvalidEnumConfigurationValueException.ForNumericValue(
                this,
                memberMapData,
                text,
                row.Context,
                typeof(T));
        }

        if (!_allowedNames.Contains(text))
        {
            throw InvalidEnumConfigurationValueException.ForNotAllowedValue(
                this,
                memberMapData,
                text,
                row.Context,
                typeof(T),
                _allowedNames);
        }

        return Parse(
            typeof(T),
            text,
            ignoreCase: true);
    }
}

public static class EnumConverterExtensions
{
    public static MemberMap<TClass, TMember> EnumConverter<TClass, TMember>(
        this MemberMap<TClass, TMember> map,
        Action<EnumConverterOptions<TMember>>? configure = null)
        where TMember : struct, Enum
    {
        var opts = new EnumConverterOptions<TMember>();
        configure?.Invoke(opts);

        var converter = new ConfigurableEnumConverter<TMember>(opts);
        return map.TypeConverter(converter);
    }
}
