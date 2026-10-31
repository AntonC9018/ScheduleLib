using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

public sealed record RegistryHistoryTopicFilesResult(string InventoryFile, IReadOnlyList<string> CsvFiles);

public sealed record RegistryHistoryTopicFileSource(
    string File,
    string CurrentCourse,
    int Semester,
    string HistoricalCourse,
    string Year,
    string RawGroup,
    Uri SourceUri,
    LessonType LessonType,
    IReadOnlyList<DateTime> Dates,
    string CourseMapping = "requires-group-mapping");

/// <summary>Private topic CSV candidates and their inventory; no import manifest is emitted.</summary>
internal static class RegistryHistoryTopicFiles
{
    private readonly record struct TopicExport(
        RegistryHistoryCourse CurrentCourse,
        RegistryHistoryCourseTopics Historical,
        string CourseMapping);

    public static async Task<RegistryHistoryTopicFilesResult> Save(
        RegistryHistoryTopicDirectory directory,
        RegistryHistoryTopicsAnalysis analysis,
        CancellationToken token)
    {
        directory.EnsureCreated();
        var runId = Guid.NewGuid()
            .ToString("N");
        var files = new List<string>();
        var sources = new List<RegistryHistoryTopicFileSource>();
        var exports = SelectExports(analysis);
        foreach (var candidate in exports)
        {
            foreach (var group in candidate.Historical.Groups)
            {
                foreach (var lessons in group.Lessons.GroupBy(lesson => lesson.LessonType))
                {
                    var ordered = lessons
                        .Where(IsExportableTopic)
                        .OrderBy(lesson => lesson.Date)
                        .ToArray();
                    if (ordered.Length == 0)
                    {
                        continue;
                    }

                    var name = $"topics-{runId}-{files.Count + 1}.csv";
                    await WriteTopics(
                        directory,
                        name,
                        ordered,
                        token);
                    files.Add(name);
                    var dates = ordered.Select(lesson => lesson.Date)
                        .ToArray();
                    var source = new RegistryHistoryTopicFileSource(
                        File: name,
                        CurrentCourse: candidate.CurrentCourse.Name,
                        Semester: candidate.CurrentCourse.Semester,
                        HistoricalCourse: candidate.Historical.Course.Name,
                        Year: candidate.Historical.Year,
                        RawGroup: group.Name,
                        SourceUri: group.Uri,
                        LessonType: lessons.Key,
                        Dates: dates,
                        CourseMapping: candidate.CourseMapping);
                    sources.Add(source);
                }
            }
        }

        var inventory = $"topics-candidates-{runId}.json";
        await WriteInventory(
            directory,
            inventory,
            analysis,
            sources,
            token);
        return new(inventory, files);
    }

    private static List<TopicExport> SelectExports(RegistryHistoryTopicsAnalysis analysis)
    {
        var exports = analysis.Selection.Candidates
            .Select(candidate => new TopicExport(
                candidate.CurrentCourse,
                candidate.Historical,
                "requires-group-mapping"))
            .ToList();
        if (analysis.Historical is not { } historical)
        {
            return exports;
        }

        var ambiguous = analysis.Selection.Unresolved
            .Where(item => item.Reason == RegistryHistoryTopicMappingFailure.AmbiguousCourse)
            .DistinctBy(item => item.CurrentCourse, RegistryHistoryCourseIdentityComparer.Instance);
        foreach (var item in ambiguous)
        {
            var matches = historical
                .Where(snapshot =>
                    RegistryHistoryTopicSelection.Matches(current: item.CurrentCourse, historical: snapshot.Course))
                .ToArray();
            var latest = matches
                .Where(HasTopics)
                .OrderByDescending(snapshot => RegistryHistoryTopicSelection.AcademicYearStart(snapshot.Year))
                .FirstOrDefault()?.Year;
            if (latest is null)
            {
                continue;
            }

            var latestExports = matches
                .Where(snapshot => snapshot.Year == latest)
                .Select(snapshot => new TopicExport(
                    item.CurrentCourse,
                    snapshot,
                    "ambiguous-course"));
            exports.AddRange(latestExports);
        }
        return exports;
    }

    private static bool IsExportableTopic(RegistryHistoryTopic lesson)
    {
        if (string.IsNullOrWhiteSpace(lesson.Topic))
        {
            return false;
        }

        if (!RegistryAssessmentTopics.IsAssessment(lesson.Topic))
        {
            return true;
        }

        return false;
    }

    private static bool HasTopics(RegistryHistoryCourseTopics snapshot) =>
        snapshot.Groups.Any(group => group.Lessons.Any(IsExportableTopic));

    private static async Task WriteTopics(
        RegistryHistoryTopicDirectory directory,
        string name,
        IReadOnlyList<RegistryHistoryTopic> lessons,
        CancellationToken token)
    {
        await using var stream = directory.NewFile(name);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var writer = new StreamWriter(stream, encoding);
        await writer.WriteLineAsync("name,lessonType".AsMemory(), token);
        foreach (var lesson in lessons)
        {
            var topic = QuoteCsvField(lesson.Topic);
            var line = $"{topic},{lesson.LessonType}";
            await writer.WriteLineAsync(line.AsMemory(), token);
        }
    }

    private static string QuoteCsvField(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static async Task WriteInventory(
        RegistryHistoryTopicDirectory directory,
        string name,
        RegistryHistoryTopicsAnalysis analysis,
        IReadOnlyList<RegistryHistoryTopicFileSource> sources,
        CancellationToken token)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        await using var stream = directory.NewFile(name);
        var inventory = new
        {
            schemaVersion = 1,
            currentYear = analysis.Selection.CurrentYear,
            requiresGroupMapping = true,
            offeredYears = analysis.OfferedYears,
            current = analysis.Current,
            historical = analysis.Historical,
            sources,
            unresolved = analysis.Selection.Unresolved,
        };
        await JsonSerializer.SerializeAsync(
            stream,
            inventory,
            options,
            token);
    }
}
