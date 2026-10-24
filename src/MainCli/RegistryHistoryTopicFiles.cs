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

/// <summary>Private download candidates in existing topic CSV syntax. Group/faculty/language mappings remain unresolved, so no import manifest is emitted.</summary>
public static class RegistryHistoryTopicFiles
{
    public static Task<RegistryHistoryTopicFilesResult> Save(
        string directory,
        RegistryHistoryTopicSelectionResult selection,
        CancellationToken token)
    {
        return Save(
            directory,
            selection,
            null,
            current: null,
            historical: null,
            token);
    }

    public static Task<RegistryHistoryTopicFilesResult> Save(
        string directory,
        RegistryHistoryTopicsAnalysis analysis,
        CancellationToken token)
    {
        return Save(
            directory,
            analysis.Selection,
            analysis.OfferedYears,
            current: analysis.Current,
            historical: analysis.Historical,
            token);
    }

    private static async Task<RegistryHistoryTopicFilesResult> Save(
        string directory,
        RegistryHistoryTopicSelectionResult selection,
        IReadOnlyList<string>? offeredYears,
        IReadOnlyList<RegistryHistoryCourseTopics>? current,
        IReadOnlyList<RegistryHistoryCourseTopics>? historical,
        CancellationToken token)
    {
        directory = Path.GetFullPath(directory);
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) != ".registry-history")
        {
            throw new IOException("Private history output requires a directory named .registry-history.");
        }

        RejectSymbolicDirectories(directory);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var runId = Guid.NewGuid().ToString("N");
        var files = new List<string>();
        var sources = new List<RegistryHistoryTopicFileSource>();
        var exports =
            new List<(RegistryHistoryCourse CurrentCourse, RegistryHistoryCourseTopics Historical, string CourseMapping
                )>();
        exports.AddRange(
            selection.Candidates.Select(candidate =>
                (candidate.CurrentCourse, candidate.Historical, "requires-group-mapping")));
        if (historical is not null)
        {
            var ambiguous = selection.Unresolved
                .Where(item => item.Reason == RegistryHistoryTopicMappingFailure.AmbiguousCourse)
                .DistinctBy(
                    item =>
                        $"{item.CurrentCourse.Semester}\0{RegistryHistoryTopicSelection.NormalizeCourseName(item.CurrentCourse.Name)}",
                    StringComparer.OrdinalIgnoreCase);
            foreach (var item in ambiguous)
            {
                var matches = historical.Where(snapshot =>
                    RegistryHistoryTopicSelection.Matches(current: item.CurrentCourse, historical: snapshot.Course)).ToArray();
                var latest = matches.Where(HasTopics).OrderByDescending(snapshot =>
                        RegistryHistoryTopicSelection.AcademicYearStart(snapshot.Year))
                    .FirstOrDefault()?.Year;
                if (latest is null)
                {
                    continue;
                }

                exports.AddRange(
                    matches.Where(snapshot => snapshot.Year == latest)
                        .Select(snapshot => (item.CurrentCourse, snapshot, "ambiguous-course")));
            }
        }
        foreach (var candidate in exports)
        {
            foreach (var group in candidate.Historical.Groups)
            {
                foreach (var lessons in group.Lessons.GroupBy(x => x.LessonType))
                {
                    var ordered = lessons
                        .Where(x => !string.IsNullOrWhiteSpace(x.Topic)
                            && !RegistryAssessmentTopics.IsAssessment(x.Topic)).OrderBy(x => x.Date).ToArray();
                    if (ordered.Length == 0)
                    {
                        continue;
                    }

                    var name = $"topics-{runId}-{files.Count + 1}.csv";
                    await using (var stream = NewPrivateFile(Path.Combine(directory, name)))
                    await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        await writer.WriteLineAsync("name,lessonType".AsMemory(), token);
                        foreach (var lesson in ordered)
                        {
                            var line = (string) $"\"{lesson.Topic.Replace("\"", "\"\"")}\"," + lesson.LessonType;
                            await writer.WriteLineAsync(line.AsMemory(), token);
                        }
                    }
                    files.Add(name);
                    sources.Add(new(
                        File: name,
                        CurrentCourse: candidate.CurrentCourse.Name,
                        candidate.CurrentCourse.Semester,
                        HistoricalCourse: candidate.Historical.Course.Name,
                        Year: candidate.Historical.Year,
                        RawGroup: group.Name,
                        group.Uri,
                        lessons.Key,
                        ordered.Select(x => x.Date).ToArray(),
                        CourseMapping: candidate.CourseMapping));
                }
            }
        }

        var inventory = $"topics-candidates-{runId}.json";
        var options =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };
        options.Converters.Add(new JsonStringEnumConverter());
        await using (var stream = NewPrivateFile(Path.Combine(directory, inventory)))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new
                {
                    schemaVersion = 1,
                    currentYear = selection.CurrentYear,
                    requiresGroupMapping = true,
                    offeredYears,
                    current,
                    historical,
                    sources,
                    unresolved = selection.Unresolved,
                },
                options,
                token);
        }

        return new(inventory, files);
    }

    private static bool HasTopics(RegistryHistoryCourseTopics snapshot)
    {
        return snapshot.Groups.Any(group => group.Lessons.Any(lesson =>
            !string.IsNullOrWhiteSpace(lesson.Topic) && !RegistryAssessmentTopics.IsAssessment(lesson.Topic)));
    }

    private static FileStream NewPrivateFile(string path)
    {
        var options =
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new(path, options);
    }

    public static void ValidateDirectory(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) != ".registry-history")
        {
            throw new IOException("Private history output requires a directory named .registry-history.");
        }

        RejectSymbolicDirectories(directory);
    }

    private static void RejectSymbolicDirectories(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            bool conditionResult;
            if (current.LinkTarget is not null)
            {
                conditionResult = true;
            }
            else
            {
                if (current.Exists)
                {
                    conditionResult = (current.Attributes & FileAttributes.ReparsePoint) != 0;
                }
                else
                {
                    conditionResult = false;
                }
            }

            if (conditionResult)
            {
                throw new IOException("Private history output requires regular directories.");
            }
        }
    }
}
