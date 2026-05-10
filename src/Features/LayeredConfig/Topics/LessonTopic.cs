using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using ScheduleLib.Application.Config;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Application.Core.Topics;

using LocalizedString = OneForEachEnumMemberArray<Language, string>;
using LocalizedStringBuilder = OneForEachEnumMemberArrayBuilder<Language, string>;

public sealed class LessonTopic
{
    public required string Name { get; set; }
    public LessonType LessonType { get; set; }
    public Language Language { get; set; }
}

public sealed class LessonTopicDefaults
{
    public LessonType? LessonType { get; set; }
    public string? CourseName { get; set; }
    public Language? Language { get; set; }
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
            if (defaults.Language is { } lang)
            {
                x.Language = lang;
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
        lessonTypeMap.EnumConverter(opts =>
        {
            opts.Include(LessonType.Curs);
            opts.Include(LessonType.Seminar);
            opts.Include(LessonType.Lab);
            opts.Include(LessonType.Prelegere);
        });
        if (defaults.LessonType is { })
        {
            lessonTypeMap.Ignore();
        }

        var languageMap = Map(m => m.Language).Name("language");
        languageMap.EnumConverter();
        languageMap.Default(Language.None);
        languageMap.Optional();
        if (defaults.Language is { })
        {
            languageMap.Ignore();
        }
    }
}

public interface ILessonNameProviderBase
{
}

public interface ILessonNameProviderFactory : ILessonNameProviderBase
{
    public ILessonNameProvider Get(Language language);
}

public sealed class NonLocalizedLessonNameProvider(
    ILessonNameProvider provider)

    : ILessonNameProviderFactory
{
    public ILessonNameProvider Get(Language language)
    {
        return provider;
    }
}

public interface ILessonNameProvider : ILessonNameProviderBase
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
    public ImmutableArray<string> Values { get; }

    public ListLessonNameProvider(ImmutableArray<string> values)
    {
        Values = values;
    }

    public string? Get(int index)
    {
        if (index < 0 || index >= Values.Length)
        {
            return null;
        }
        return Values[index];
    }
}

public sealed class LabAutoNumberingNameProviderFactory : ILessonNameProviderFactory
{
    public ILessonNameProvider Get(Language language)
    {
        return new LabAutoNumberingNameProvider(language);
    }
}

public sealed class LabAutoNumberingNameProvider : ILessonNameProvider
{
    private readonly Language _language;
    private static readonly LocalizedString _labs = new LocalizedStringBuilder()
        .Set(Language.Ru, "Лабораторная работа")
        .Set(Language.Ro, "Lucrarea de laborator")
        .Set(Language.En, "Lab")
        .Build();

    public LabAutoNumberingNameProvider(Language language)
    {
        _language = language;
    }

    public string Get(int index)
    {
        return $"{_labs[_language]} {index + 1}";
    }
}

public readonly record struct ClassifiedTopicsKey
{
    public readonly TopicsGroupsKey GroupsKey;
    public readonly CourseKey CourseKey;

    public ClassifiedTopicsKey(
        TopicsGroupsKey groupsKey,
        CourseKey courseId)
    {
        CourseKey = courseId;
        GroupsKey = groupsKey;
    }
}

public readonly record struct ClassifiedTopicsProviders(
    in ClassifiedTopicsKey Key,
    SparseArray<LessonType, ILessonNameProvider> Providers)
{
    public readonly ClassifiedTopicsKey Key = Key;
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
            if (!it.Key.GroupsKey.IsLessonGroupsMatch(key.Groups.Value))
            {
                continue;
            }
            if (!it.Key.CourseKey.MatchesCourse(key.CourseId))
            {
                continue;
            }
            if (!it.Key.GroupsKey.IsSubGroupMatch(key.SubGroup))
            {
                continue;
            }
            var provider = it.Providers[key.LessonType];
            if (provider is null)
            {
                return null;
            }

            return provider.Get(key.DayIndex);
        }
        return null;
    }
}

public readonly record struct TopicsGroupsKey
{
    public readonly LessonGroups Groups;
    public readonly SubGroup? SubGroup;

    public TopicsGroupsKey(
        in LessonGroups groups,
        SubGroup? subGroup)
    {
        Groups = groups;
        SubGroup = subGroup;
    }

    public readonly bool IsForAllGroups => Groups.Count == 0;

    public readonly bool IsLessonGroupsMatch(
        in LessonGroups lessonGroups)
    {
        if (IsForAllGroups)
        {
            return true;
        }
        if (lessonGroups.IsSubSetOf(Groups))
        {
            return true;
        }
        return false;
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

    public bool IncludesAllOf(in TopicsGroupsKey key)
    {
        if (this.IsForAllGroups)
        {
            return true;
        }
        if (key.IsForAllGroups)
        {
            return false;
        }
        return this.IsLessonGroupsMatch(key.Groups);
    }
}

// Abstraction to be able to store 1-2 courses inline later.
public struct CourseKeyBuilder
{
    private ImmutableArray<CourseId>.Builder _builder;
    public CourseKeyBuilder(int capacity)
    {
        _builder = ImmutableArray.CreateBuilder<CourseId>();
    }
    public bool Add(CourseId courseId)
    {
        foreach (var x in _builder)
        {
            if (x == courseId)
            {
                return false;
            }
        }

        _builder.Add(courseId);
        return true;
    }

    public CourseKey Build() => new(new(_builder.ToImmutable()));
}

public readonly record struct CourseKey(
    SequenceComparableImmutableArray<CourseId> Ids)
{
    public bool MatchesCourse(
        CourseId courseId)
    {
        foreach (var id in Ids.Array)
        {
            if (id == courseId)
            {
                return true;
            }
        }
        return false;
    }

    public bool IncludesAllOf(CourseKey other)
    {
        foreach (var id in other.Ids.Array)
        {
            if (!MatchesCourse(id))
            {
                return false;
            }
        }
        return true;
    }

    public ImmutableArray<CourseId> EnumerateIds() => Ids.Array;
}

public readonly record struct TopicsBuilderKey
{
    // Needed for instantiating the fallback providers.
    public Language Language { get; init; }

    public readonly CourseKey CourseKey;
    public readonly TopicsGroupsKey GroupsKey;

    public TopicsBuilderKey(
        CourseKey courseKey,
        TopicsGroupsKey groupsKey,
        Language language = Language.None)
    {
        Language = language;
        CourseKey = courseKey;
        GroupsKey = groupsKey;
    }

    public bool MatchesLanguage(
        Schedule schedule,
        in LessonGroups groups)
    {
        if (Language == Language.None)
        {
            return true;
        }
        if (groups.IsEmpty)
        {
            return false;
        }
        var g0 = groups.Group0;
        if (Language == schedule.Get(g0).Language)
        {
            return true;
        }
        return false;
    }
}

public sealed class LessonTopicsBuilder
{
    internal readonly TopicsBuilderKey Key;
    internal readonly Dictionary<LessonType, List<string>> _lists = new();

    public bool IsFallbackBuilder => Key.Language == Language.None;

    public LessonTopicsBuilder(TopicsBuilderKey key)
    {
        Key = key;
    }

    public void Add(LessonType type, string value)
    {
        var list = _lists.GetOrAdd(type, key =>
        {
            _ = key;
            return new();
        });
        list.Add(value);
    }
}

public sealed partial class AllLessonTopicsDatabaseBuilder
{
    private readonly List<LessonTopicsBuilder> _items = new();
    private readonly SparseArray<LessonType, ILessonNameProviderFactory> _defaultProviders;
    // Only includes the relevant lessons.
    // NOTE: Currently, recreated per teacher.
    private readonly FilteredSchedule _schedule;
    private readonly ILogger _logger;

    public AllLessonTopicsDatabaseBuilder(
        FilteredSchedule schedule,
        ILogger<AllLessonTopicsDatabaseBuilder> logger)
    {
        _schedule = schedule;
        _logger = logger;
        _defaultProviders = new();
    }

    public void FallbackProvider(LessonType lessonType, ILessonNameProviderFactory provider)
    {
        _defaultProviders[lessonType] = provider;
    }

    public LessonTopicsFromDatabase Build()
    {
        // TODO: Better DS: list for search of key inclusion
        // TODO: Explore moving this to runtime
        var fallbacks = new FallbackProvidersDS();
        foreach (ref readonly var it in CollectionsMarshal.AsSpan(_items))
        {
            if (!it.IsFallbackBuilder)
            {
                continue;
            }

            var fallbackProviders = GetProvidersNoFallback(it);
            fallbacks.Add(new(it, fallbackProviders.Providers));
        }

        var b = ImmutableArray.CreateBuilder<ClassifiedTopicsProviders>(_items.Count - fallbacks.Count);
        foreach (ref readonly var it in CollectionsMarshal.AsSpan(_items))
        {
            if (it.IsFallbackBuilder)
            {
                continue;
            }

            var p = GetProvidersNoFallback(it);
            var fallbackProvider = fallbacks.FindFallbacks(it.Key);
            SetProviderFallbacks(
                fallbackProvider,
                p.Providers,
                p.LessonTypesToProcess.Required,
                it.Key.CourseKey,
                it.Key.Language);

            var builtKey = new ClassifiedTopicsKey(it.Key.GroupsKey, it.Key.CourseKey);
            b.Add(new(builtKey, p.Providers));
        }
        return new LessonTopicsFromDatabase(b.MoveToImmutable());
    }

    public LessonTopicsBuilder Topics(TopicsBuilderKey key)
    {
        var item = _items.FirstOrDefault(i => i.Key.Equals(key));
        if (item is null)
        {
            item = new LessonTopicsBuilder(key);
            _items.Add(item);
        }
        return item;
    }

    public async Task AddFromManifest(
        ManifestAtLocation m,
        LookupFacade lookup,
        CancellationToken cancellationToken)
    {
        var bylang = new TopicsByLanguage();

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
                Language = document.Language,
            };

            var courseKeyBuilder = new CourseKeyBuilder(document.Course.Count);
            foreach (var course in document.Course)
            {
                if (lookup.Course(course.AsMemory()) is not { } courseId)
                {
                    LogCourseCourseNotFoundInLookup(course);
                    continue;
                }
                courseKeyBuilder.Add(courseId);
            }
            var courseKey = courseKeyBuilder.Build();
            bylang.Begin(courseKey);

            var lessonGroups = FindMatchingGroups(_schedule, document);
            if (lessonGroups.IsEmpty)
            {
                LogNoMatchingGroups(document.Path);
                continue;
            }

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

            foreach (var (lang, groups) in lessonGroups)
            {
                bylang.SetupBuilder(this, lang, groups);
            }

            await foreach (var lessonTopic in e)
            {
                if (lessonTopic.Language == Language.None)
                {
                    // Must have been checked earlier at CSV binding.
                    Debug.Assert(defaults.Language == null);
                }

                var builder = bylang.GetBuilder(this, lessonTopic.Language);
                builder.Add(lessonTopic.LessonType, lessonTopic.Name);
            }

        }
    }

    private static SparseArray<Language, LessonGroups> FindMatchingGroups(
        FilteredSchedule filteredSchedule,
        Document document)
    {
        SparseArray<Language, LessonGroups> ret = new();
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
            if (!IsLanguageMatch())
            {
                continue;
            }
            if (!IsAttendanceMatch())
            {
                continue;
            }

            var language = group.Language;
            ref var groups = ref ret.GetOrAdd(language, out bool existed);
            if (!existed)
            {
                groups = new LessonGroups();
            }
            groups.Add(groupId);
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

            bool IsAttendanceMatch()
            {
                if (document.Attendance == default)
                {
                    return true;
                }
                if (document.Attendance.Contains(group.AttendanceMode))
                {
                    return true;
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

            bool IsLanguageMatch()
            {
                if (document.Language is not { } docLang)
                {
                    return true;
                }
                if (group.Language != docLang)
                {
                    return false;
                }
                return true;
            }
        }
        return ret;
    }

    private static EnumBitArray<LessonType> GetLessonTypesExistingInSchedule(
        LessonTopicsBuilder it,
        FilteredSchedule schedule)
    {
        EnumBitArray<LessonType> foundLessonTypes = new();
        foreach (var lesson in schedule.EnumerateLessons())
        {
            ref readonly var l = ref lesson.Lesson;
            if (!it.Key.GroupsKey.IsLessonGroupsMatch(l.Groups))
            {
                continue;
            }
            if (!it.Key.GroupsKey.IsSubGroupMatch(l.SubGroup))
            {
                continue;
            }
            if (!it.Key.CourseKey.MatchesCourse(l.Course))
            {
                continue;
            }
            if (!it.Key.MatchesLanguage(schedule.Source, l.Groups))
            {
                continue;
            }
            foundLessonTypes.Set(l.Type);
        }
        return foundLessonTypes;
    }

    private (SparseArray<LessonType, ILessonNameProvider> Providers, LessonTypesToProcess LessonTypesToProcess)
        GetProvidersNoFallback(LessonTopicsBuilder builder)
    {
        var hooks = new ProcessingHooks();
        var foundLessonTypes = GetLessonTypesExistingInSchedule(builder, _schedule);
        var ltypes = hooks.GetLessonTypesToProcess(foundLessonTypes);

        var providers = OneForEach.Enum<LessonType>().CreateSparseArray<ILessonNameProvider>();
        foreach (var lessonType in ltypes.ToProcess.SetValues())
        {
            if (builder._lists.TryGetValue(lessonType, out var list))
            {
                var arr = list.ToImmutableArray();
                var provider = new ListLessonNameProvider(arr);
                providers.Add(lessonType, provider);
            }
        }
        hooks.UpdateProvidersAfterInitialized(providers);
        return (providers, ltypes);
    }

    private void SetProviderFallbacks(
        BuiltFallbackProvider fallback,
        SparseArray<LessonType, ILessonNameProvider> providers,
        EnumBitArray<LessonType> typesToProcess,
        CourseKey courseKey,
        Language language)
    {
        foreach (var lessonType in typesToProcess.SetValues())
        {
            if (providers.TryGet(lessonType, out _))
            {
                continue;
            }

            if (!fallback.IsNull)
            {
                if (fallback.Provider.TryGet(lessonType, out var provider))
                {
                    providers[lessonType] = provider;
                    continue;
                }
            }

            {
                if (_defaultProviders.TryGet(lessonType, out var provider))
                {
                    providers[lessonType] = provider.Get(language);
                    continue;
                }
            }

            {
                var sb = new StringBuilder();
                var list = new ListStringBuilder(sb, ", ");
                foreach (var courseId in courseKey.EnumerateIds())
                {
                    list.Append(_schedule.Source.Get(courseId).FullName);
                }
                throw new InvalidOperationException(
                    $"No provider for lesson type {lessonType} for courses {sb}.");
            }
        }
    }

    [LoggerMessage(LogLevel.Warning, "Course '{Course}' not found in lookup")]
    partial void LogCourseCourseNotFoundInLookup(string Course);

    [LoggerMessage(LogLevel.Warning, "No matching lesson groups found for document '{DocumentPath}' with specified faculty/grade filters")]
    partial void LogNoMatchingGroups(string DocumentPath);
}

internal record struct LessonTypesToProcess(
    EnumBitArray<LessonType> Required,
    EnumBitArray<LessonType> ToProcess);
// Abstraction to guide some decisions.
file readonly struct ProcessingHooks
{
    public LessonTypesToProcess GetLessonTypesToProcess(
        EnumBitArray<LessonType> foundLessons)
    {
        if (foundLessons.IsSet(LessonType.Prelegere))
        {
            foundLessons.Set(LessonType.Curs);
        }
        return new(Required: foundLessons, ToProcess: EnumBitArray<LessonType>.AllSet);
    }

    public void UpdateProvidersAfterInitialized(
        SparseArray<LessonType, ILessonNameProvider> providers)
    {
        if (!providers.TryGet(LessonType.Prelegere, out _)
            && providers.TryGet(LessonType.Curs, out var curs))
        {
            providers.Add(LessonType.Prelegere, curs);
        }
    }
}

internal readonly record struct BuiltFallbackProvider(
    LessonTopicsBuilder OriginalBuilder,
    SparseArray<LessonType, ILessonNameProvider> Provider)
{
    public bool IsNull => OriginalBuilder == null;
}

file sealed class FallbackProvidersDS : List<BuiltFallbackProvider>
{
    public BuiltFallbackProvider FindFallbacks(
        TopicsBuilderKey mainBuilderKey)
    {
        foreach (var t in this)
        {
            if (t.OriginalBuilder.Key.Language != Language.None)
            {
                continue;
            }
            if (!t.OriginalBuilder.Key.CourseKey.IncludesAllOf(mainBuilderKey.CourseKey))
            {
                continue;
            }
            if (!t.OriginalBuilder.Key.GroupsKey.IncludesAllOf(mainBuilderKey.GroupsKey))
            {
                continue;
            }
            if (t.OriginalBuilder.Key.GroupsKey.SubGroup is { } subGroup
                && mainBuilderKey.GroupsKey.SubGroup != subGroup)
            {
                continue;
            }
            return t;
        }
        return default;
    }
}

file struct TopicsByLanguage()
{
    private CourseKey _courseKey;
    private readonly SparseArray<Language, LessonTopicsBuilder> _arr = new();
    private readonly HashSet<GroupId> _set = new();
    private LessonTopicsBuilder? _fallback;

    public void Begin(CourseKey courseKey)
    {
        _courseKey = courseKey;
        _set.Clear();
        _arr.Clear();
        _fallback = null;
    }

    public void SetupBuilder(
        AllLessonTopicsDatabaseBuilder self,
        Language language,
        in LessonGroups groups)
    {
        var topics = self.Topics(new(
            courseKey: _courseKey,
            groupsKey: new(groups, null),
            language: language));
        _arr[language] = topics;
    }

    public LessonTopicsBuilder GetBuilder(
        AllLessonTopicsDatabaseBuilder self,
        Language language)
    {
        if (language != Language.None)
        {
            return _arr[language];
        }

        if (_fallback is null)
        {
            var allGroups = new LessonGroups();
            foreach (var (_, builder) in _arr)
            {
                foreach (var g in builder.Key.GroupsKey.Groups)
                {
                    _set.Add(g);
                }
            }

            _fallback = self.Topics(new(
                courseKey: _courseKey,
                groupsKey: new(allGroups, null),
                language: Language.None));
        }
        return _fallback;
    }
}

