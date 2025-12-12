using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CsvHelper;
using CsvHelper.Configuration;
using MainCli.BuilderNew.Impl;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;

namespace MainCli.Topics;

public sealed class LessonTopic
{
    public required string Name { get; set; }
    public LessonType LessonType { get; set; }
}

public sealed class LessonTopicDefaults
{
    public LessonType? LessonType { get; set; }
}

public static class LessonTopicCsvSerializer
{
    private static CsvConfiguration Config => new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        MissingFieldFound = null, // Don't throw error on missing fields
        IgnoreBlankLines = true,
        DetectDelimiter = true,
    };

    public static async IAsyncEnumerable<LessonTopic> Deserialize(
        TextReader input,
        LessonTopicDefaults defaults,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Func<CsvConfiguration, CsvConfiguration>? additionalConfig = null)
    {
        var config = Config;
        if (additionalConfig is { } c)
        {
            config = c(config);
        }
        using var csvReader = new CsvReader(input, config);
        csvReader.Context.RegisterClassMap(new LessonTopicMap(defaults));
        var ret = csvReader.GetRecordsAsync<LessonTopic>(cancellationToken);
        await foreach (var x in ret)
        {
            if (defaults.LessonType is { } lessonType)
            {
                x.LessonType = lessonType;
            }
            yield return x;
        }
    }
}

// Custom mapping for when Age column is missing entirely
public sealed class LessonTopicMap : ClassMap<LessonTopic>
{
    public LessonTopicMap(LessonTopicDefaults defaults)
    {
        Map(m => m.Name).Name("name");
        var lessonTypeMap = Map(m => m.LessonType).Name("lessonType");

        if (defaults.LessonType is { })
        {
            lessonTypeMap.Ignore();
        }
    }
}

public interface ILessonNameProvider
{
    public string? Get(int index);
}

public sealed class NoNameProvider : ILessonNameProvider
{
    public string? Get(int index)
    {
        _ = index;
        return null;
    }
}

public sealed class ListLessonNameProvider : ILessonNameProvider
{
    private readonly ImmutableArray<string> _values;

    public ListLessonNameProvider(ImmutableArray<string> values)
    {
        _values = values;
    }

    public string? Get(int index)
    {
        if (index < 0 || index >= _values.Length)
        {
            return null;
        }
        return _values[index];
    }
}

public sealed class LabAutoNumberingNameProvider : ILessonNameProvider
{
    public string Get(int index)
    {
        return $"Лабораторная работа {index + 1}";
    }
}

public readonly record struct ClassifiedTopicsProviders(
    in Key Key,
    in ValueForEachLessonType<ILessonNameProvider?> Providers)
{
    public readonly Key Key = Key;
    public readonly ValueForEachLessonType<ILessonNameProvider?> Providers = Providers;
}

public sealed class LessonTopicsFromDatabase : ILessonTopics
{
    private readonly ImmutableArray<ClassifiedTopicsProviders> _list;

    public LessonTopicsFromDatabase(ImmutableArray<ClassifiedTopicsProviders> list)
    {
        _list = list;
    }

    public string? Get(in AttendanceLookupKey key)
    {
        foreach (ref readonly var it in _list.AsSpan())
        {
            if (!it.Key.IsLessonGroupsMatch(key.Groups.Value))
            {
                continue;
            }
            if (it.Key.CourseId != key.CourseId)
            {
                continue;
            }
            if (!it.Key.IsSubGroupMatch(key.SubGroup))
            {
                continue;
            }
            var provider = it.Providers[(int) key.LessonType];
            if (provider is null)
            {
                return null;
            }

            return provider.Get(key.DayIndex);
        }
        return null;
    }
}

public readonly record struct Key
{
    public readonly LessonGroups Groups;
    public readonly SubGroup? SubGroup;
    public readonly CourseId CourseId;

    public Key(
        CourseId courseId,
        LessonGroups groups = default,
        SubGroup? subGroup = null)
    {
        Groups = groups;
        SubGroup = subGroup;
        CourseId = courseId;
    }

    public readonly bool IsForAllGroups => Groups.Count == 0;

    public readonly bool IsLessonGroupsMatch(
        in LessonGroups lessonGroups)
    {
        if (IsForAllGroups)
        {
            return true;
        }
        if (!lessonGroups.IsSubSetOf(Groups))
        {
            return false;
        }
        return true;
    }

    public readonly bool IsSubGroupMatch(
        SubGroup subGroup)
    {
        if (SubGroup is { } sg)
        {
            return sg == subGroup;
        }
        return true;
    }
}

public sealed class LessonTopicsBuilder
{
    internal readonly Key Key;
    internal ValueForEachLessonType<List<string>?> _lists;

    public LessonTopicsBuilder(Key key)
    {
        Key = key;
    }

    public void Add(LessonType type, string value)
    {
        var list = _lists[(int) type] ??= new();
        list.Add(value);
    }
}

public sealed class AllLessonTopicsDatabaseBuilder
{
    private readonly List<LessonTopicsBuilder> _items = new();
    private ValueForEachLessonType<ILessonNameProvider?> _defaultProviders;
    // Only includes the relevant lessons.
    // NOTE: Currently, recreated per teacher.
    private readonly FilteredSchedule _schedule;

    public AllLessonTopicsDatabaseBuilder(FilteredSchedule schedule)
    {
        _schedule = schedule;
    }

    public void FallbackProvider(LessonType lessonType, ILessonNameProvider provider)
    {
        _defaultProviders[(int) lessonType] = provider;
    }

    public LessonTopicsFromDatabase Build()
    {
        var b = ImmutableArray.CreateBuilder<ClassifiedTopicsProviders>(_items.Count);
        foreach (ref readonly var it in CollectionsMarshal.AsSpan(_items))
        {
            EnumBitArray<LessonType> foundLessonTypes = new();
            foreach (var lesson in _schedule.Lessons)
            {
                if (!it.Key.IsLessonGroupsMatch(lesson.Lesson.Groups))
                {
                    continue;
                }
                if (!it.Key.IsSubGroupMatch(lesson.Lesson.SubGroup))
                {
                    continue;
                }
                if (it.Key.CourseId != lesson.Lesson.Course)
                {
                    continue;
                }
                foundLessonTypes.Set(lesson.Lesson.Type);
            }

            ValueForEachLessonType<ILessonNameProvider?> providers = new();
            foreach (var lessonType in foundLessonTypes.SetValues())
            {
                var list = it._lists[(int) lessonType];
                if (list is not null)
                {
                    var arr = list.ToImmutableArray();
                    var provider = new ListLessonNameProvider(arr);
                    providers[(int) lessonType] = provider;
                    continue;
                }

                var defaultProvider = _defaultProviders[(int) lessonType];
                if (defaultProvider is not null)
                {
                    providers[(int) lessonType] = defaultProvider;
                    continue;
                }

                throw new InvalidOperationException(
                    $"No provider for lesson type {lessonType} for course {it.Key.CourseId}.");
            }
            b.Add(new(it.Key, providers));
        }
        return new LessonTopicsFromDatabase(b.MoveToImmutable());
    }

    public LessonTopicsBuilder Topics(Key key)
    {
        var item = _items.FirstOrDefault(i => i.Key.Equals(key));
        if (item is null)
        {
            item = new LessonTopicsBuilder(key);
            _items.Add(item);
        }
        return item;
    }

    public static async Task<AllLessonTopicsDatabaseBuilder> Parse(
        string manifestPath,
        LookupFacade lookup,
        Schedule schedule,
        CancellationToken cancellationToken)
    {
        Manifest manifest;
        {
            await using var inputFile = File.OpenRead(manifestPath);
            manifest = await ManifestSerializer.Deserialize(inputFile, cancellationToken);
        }
        _ = manifest;
        return null!;
    }

    public async Task AddFromManifest(
        ManifestAtLocation m,
        LookupFacade lookup,
        CancellationToken cancellationToken)
    {
        foreach (var document in m.Manifest.Documents)
        {
            var documentFilePath = Path.Combine(m.DirectoryPath ?? "", document.Path);
            await using var documentStream = File.OpenRead(documentFilePath);

            #pragma warning disable CA2000 // Dispose objects before losing scope
            var textReader = new StreamReader(documentStream);
            #pragma warning restore CA2000 // Dispose objects before losing scope

            var defaults = new LessonTopicDefaults
            {
                LessonType = document.LessonType,
            };

            if (lookup.Course(document.Course) is not { } courseId)
            {
                throw new InvalidOperationException($"Course '{document.Course}' not found in lookup.");
            }

            var lessonGroups = FindMatchingGroups(
                _schedule,
                document);

            if (lessonGroups.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No matching lesson groups found for document '{document.Path}' with specified faculty/grade filters.");
            }

            var topics = Topics(new(
                courseId: courseId,
                groups: lessonGroups,
                subGroup: null));

            var e = LessonTopicCsvSerializer.Deserialize(
                textReader,
                defaults,
                cancellationToken,
                c =>
                {
                    if (document.Delimiter is { } delim)
                    {
                        c.DetectDelimiter = false;
                        c.Delimiter = delim;
                    }
                    return c;
                });
            await foreach (var lessonTopic in e)
            {
                topics.Add(lessonTopic.LessonType, lessonTopic.Name);
            }
        }
    }

    private static LessonGroups FindMatchingGroups(
        FilteredSchedule filteredSchedule,
        Document document)
    {
        LessonGroups ret = new();
        foreach (var groupId in filteredSchedule.Groups)
        {
            var group = filteredSchedule.Source.Get(groupId);
            if (!IsFacultyMatch())
            {
                continue;
            }
            if (!IsGradeMatch())
            {
                continue;
            }
            ret.Add(groupId);
            continue;

            bool IsFacultyMatch()
            {
                if (document.Faculty == null)
                {
                    return true;
                }
                foreach (var faculty in document.Faculty)
                {
                    if (group.Faculty == faculty)
                    {
                        return true;
                    }
                }
                return false;
            }

            bool IsGradeMatch()
            {
                if (document.Grade == null)
                {
                    return true;
                }
                foreach (var grade in document.Grade)
                {
                    if (group.Grade == grade)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        return ret;
    }
}
