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
    public string? Series { get; set; }
}

public sealed class LessonTopicDefaults
{
    public LessonType? LessonType { get; set; }
    public string? CourseName { get; set; }
    public Language? Language { get; set; }
}

public static class LessonTopicCsvSerializer
{
    private static CsvConfiguration Config
    {
        get
        {
            return new(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null, // Don't throw error on missing fields
                IgnoreBlankLines = true,
                DetectDelimiter = true,
            };
        }
    }

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
        if (!(await csvReader.ReadAsync()))
        {
            yield break;
        }

        csvReader.ReadHeader();
        var hasLessonType = csvReader.HeaderRecord?.Contains("lessonType", StringComparer.Ordinal) == true;
        var hasLanguage = csvReader.HeaderRecord?.Contains("language", StringComparer.Ordinal) == true;
        var csvContext = csvReader.Context;
        var map =
            new LessonTopicMap(
                defaults,
                hasLessonType: hasLessonType,
                hasLanguage: hasLanguage);
        ClassMap map2 = map;
        csvContext.RegisterClassMap(map2);
        var ret = csvReader.GetRecordsAsync<LessonTopic>(cancellationToken);
        await foreach (var x in ret)
        {
            if (!hasLessonType && defaults.LessonType is { } lessonType)
            {
                x.LessonType = lessonType;
            }
            if (!hasLanguage)
            {
                x.Language = defaults.Language ?? Language.None;
            }
            if (!RegistryAssessmentTopics.IsAssessment(x.Name))
            {
                yield return x;
            }
        }
    }
}

public sealed class LessonTopicMap : ClassMap<LessonTopic>
{
    public LessonTopicMap(
        LessonTopicDefaults defaults,
        bool hasLessonType = true,
        bool hasLanguage = true)
    {
        Map(m => m.Name)
            .Name("name");
        Map(m => m.Series)
            .Name("series")
            .Optional();

        if (hasLessonType)
        {
            var lessonTypeMap = Map(m => m.LessonType)
                .Name("lessonType")
                .Optional();
            lessonTypeMap.EnumConverter(opts =>
            {
                opts.Include(LessonType.Curs);
                opts.Include(LessonType.Seminar);
                opts.Include(LessonType.Lab);
                opts.Include(LessonType.Prelegere);
            });
            if (defaults.LessonType is { } lessonType)
            {
                lessonTypeMap.Default(lessonType);
            }
        }
        else
        {
            Map(m => m.LessonType)
                .Ignore();
        }

        if (hasLanguage)
        {
            var languageMap = Map(m => m.Language)
                .Name("language")
                .Optional();
            languageMap.EnumConverter();
            languageMap.Default(defaults.Language ?? Language.None);
        }
        else
        {
            Map(m => m.Language)
                .Ignore();
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
            if (!it.Key.GroupsKey.IsGroupPartitionMatch(key.GroupPartition))
            {
                continue;
            }
            if (!it.Providers.TryGet(key.LessonType, out var provider) || provider is null)
            {
                return null;
            }

            var result = provider.Get(key.DayIndex);
            if (result is null)
            {
                return null;
            }

            return result;
        }
        return null;
    }
}

public readonly record struct TopicsGroupsKey
{
    public readonly LessonGroups Groups;
    public readonly GroupPartitionKey? GroupPartition;

    public TopicsGroupsKey(
        in LessonGroups groups,
        GroupPartitionKey? groupPartition)
    {
        Groups = groups;
        GroupPartition = groupPartition;
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

    public readonly bool IsGroupPartitionMatch(
        GroupPartitionKey groupPartition)
    {
        if (!(GroupPartition is { } partition))
        {
            return true;
        }

        if (partition == groupPartition)
        {
            return true;
        }

        return false;
    }

    public bool IncludesAllOf(in TopicsGroupsKey key)
    {
        if (IsForAllGroups)
        {
            return true;
        }
        if (key.IsForAllGroups)
        {
            return false;
        }

        if (IsLessonGroupsMatch(key.Groups))
        {
            return true;
        }

        return false;
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

    public CourseKey Build()
    {
        SequenceComparableImmutableArray<CourseId> ids =
            new(_builder.ToImmutable());
        CourseKey courseKey = new(ids);
        return courseKey;
    }
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
    internal bool AllowMissingLessonTypes;
    internal readonly TopicsBuilderKey Key;
    internal readonly Dictionary<LessonType, List<string>> _lists = new();

    public bool IsFallbackBuilder => Key.Language == Language.None;

    public LessonTopicsBuilder(TopicsBuilderKey key)
    {
        Key = key;
    }

    public void Add(LessonType type, string value)
    {
        var list = _lists.GetOrAdd(
            type,
            key =>
            {
                _ = key;
                return new();
            });
        list.Add(value);
    }
}

public sealed partial class AllLessonTopicsDatabaseBuilder
{
    public int? Semester { get; set; }
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
            var fallbackProvidersDS = fallbacks;
            BuiltFallbackProvider item = new(
                it,
                fallbackProviders.Providers);
            fallbackProvidersDS.Add(item);
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
                it.Key.Language,
                it.AllowMissingLessonTypes);

            var builtKey = new ClassifiedTopicsKey(it.Key.GroupsKey, it.Key.CourseKey);
            var builder = b;
            ClassifiedTopicsProviders item2 = new(builtKey, p.Providers);
            builder.Add(item2);
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
            if (document.Semester is { } semester && semester != Semester)
            {
                continue;
            }

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
                bool CheckCondition()
                {
                    if (document.WholeGroupOnly)
                    {
                        return true;
                    }

                    if (document.SubGroup is not null)
                    {
                        return true;
                    }

                    if (document.Specialization is not null)
                    {
                        return true;
                    }

                    if (document.Alternative is not null)
                    {
                        return true;
                    }

                    return false;
                }

                bylang.SetupBuilder(
                    this,
                    lang,
                    groups,
                    CheckCondition()
                        ? new GroupPartitionKey(
                            new SubGroup(document.SubGroup),
                            new Specialization(document.Specialization),
                            new Alternative(document.Alternative))
                        : null,
                    document.AllowMissingLessonTypes);
            }

            await foreach (var lessonTopic in e)
            {
                if (document.Series is { } series && lessonTopic.Series != series)
                {
                    continue;
                }

                if (document.LessonType is { } lessonType && lessonTopic.LessonType != lessonType)
                {
                    continue;
                }

                if (document.Language is { } language && lessonTopic.Language != language)
                {
                    continue;
                }

                if (lessonTopic.Language == Language.None)
                {
                    // Must have been checked earlier at CSV binding.
                    Debug.Assert(defaults.Language == null);
                }

                var builder = bylang.FindBuilder(this, lessonTopic.Language);
                if (builder is null)
                {
                    continue;
                }

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
            if (document.Groups is { } names
                && !names.Contains(group.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

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

                if (group.Language == docLang)
                {
                    return true;
                }

                return false;
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
            if (!it.Key.GroupsKey.IsGroupPartitionMatch(l.GroupPartitionKey))
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

    /// <summary>The lesson-name providers built for a topic builder, and the lesson types still to process.</summary>
    private readonly record struct ProviderSetup(
        SparseArray<LessonType, ILessonNameProvider> Providers,
        LessonTypesToProcess LessonTypesToProcess);

    private ProviderSetup GetProvidersNoFallback(LessonTopicsBuilder builder)
    {
        var hooks = new ProcessingHooks();
        var foundLessonTypes = GetLessonTypesExistingInSchedule(builder, _schedule);
        var ltypes = hooks.GetLessonTypesToProcess(foundLessonTypes);

        var providers = OneForEach.Enum<LessonType>()
            .CreateSparseArray<ILessonNameProvider>();
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
        return new(providers, ltypes);
    }

    private void SetProviderFallbacks(
        BuiltFallbackProvider fallback,
        SparseArray<LessonType, ILessonNameProvider> providers,
        EnumBitArray<LessonType> typesToProcess,
        CourseKey courseKey,
        Language language,
        bool allowMissingLessonTypes)
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

            if (allowMissingLessonTypes)
            {
                providers[lessonType] = new NoNameProvider();
                continue;
            }

            {
                var sb = new StringBuilder();
                var list = new ListStringBuilder(sb, ", ");
                foreach (var courseId in courseKey.EnumerateIds())
                {
                    list.Append(_schedule.Source.Get(courseId).FullName);
                }
                var courses = sb.ToString();
                throw new NoLessonNameProviderForLessonTypeException(lessonType, courses);
            }
        }
    }

    [LoggerMessage(LogLevel.Warning, "Course '{Course}' not found in lookup")]
    partial void LogCourseCourseNotFoundInLookup(string Course);

    [LoggerMessage(LogLevel.Warning,
        "No matching lesson groups found for document '{DocumentPath}' with specified faculty/grade filters")]
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
            if (t.OriginalBuilder.Key.GroupsKey.GroupPartition is { } groupPartition
                && mainBuilderKey.GroupsKey.GroupPartition != groupPartition)
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
    private GroupPartitionKey? _partition;

    public void Begin(CourseKey courseKey)
    {
        _courseKey = courseKey;
        _set.Clear();
        _arr.Clear();
        _fallback = null;
        _partition = null;
    }

    public void SetupBuilder(
        AllLessonTopicsDatabaseBuilder self,
        Language language,
        in LessonGroups groups,
        GroupPartitionKey? partition,
        bool allowMissingLessonTypes)
    {
        _partition = partition;
        var
            allLessonTopicsDatabaseBuilder = self;
        var courseKey2 = _courseKey;
        TopicsGroupsKey groupsKey2 = new(groups, partition);
        var language2 = language;
        TopicsBuilderKey key = new(
            courseKey: courseKey2,
            groupsKey: groupsKey2,
            language: language2);
        var topics = allLessonTopicsDatabaseBuilder.Topics(key);
        topics.AllowMissingLessonTypes |= allowMissingLessonTypes;
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

            foreach (var group in _set)
            {
                allGroups.Add(group);
            }

            var allLessonTopicsDatabaseBuilder =
                self;
            var courseKey2 = _courseKey;
            TopicsGroupsKey groupsKey2 = new(allGroups, _partition);
            var language2 = Language.None;
            TopicsBuilderKey key = new(
                courseKey: courseKey2,
                groupsKey: groupsKey2,
                language: language2);
            var topics =
                allLessonTopicsDatabaseBuilder.Topics(key);
            _fallback = topics;
        }
        return _fallback;
    }

    public LessonTopicsBuilder? FindBuilder(
        AllLessonTopicsDatabaseBuilder self,
        Language language)
    {
        if (language == Language.None)
        {
            return GetBuilder(self, language);
        }

        if (!_arr.TryGet(language, out var builder))
        {
            return null;
        }

        return builder;
    }
}
