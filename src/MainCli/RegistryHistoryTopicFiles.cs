using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

public sealed record RegistryHistoryTopicFilesResult(string InventoryFile, IReadOnlyList<string> CsvFiles);
public sealed record RegistryHistoryTopicFileSource(
    string File, string CurrentCourse, int Semester, string HistoricalCourse, string Year,
    string RawGroup, Uri SourceUri, LessonType LessonType, IReadOnlyList<DateTime> Dates);

/// <summary>Private download candidates in existing topic CSV syntax. Group/faculty/language mappings remain unresolved, so no import manifest is emitted.</summary>
public static class RegistryHistoryTopicFiles
{
    public static Task<RegistryHistoryTopicFilesResult> Save(string directory, RegistryHistoryTopicSelectionResult selection, CancellationToken token) =>
        Save(directory, selection, null, null, null, token);

    public static Task<RegistryHistoryTopicFilesResult> Save(string directory, RegistryHistoryTopicsAnalysis analysis, CancellationToken token) =>
        Save(directory, analysis.Selection, analysis.OfferedYears, analysis.Current, analysis.Historical, token);

    private static async Task<RegistryHistoryTopicFilesResult> Save(string directory, RegistryHistoryTopicSelectionResult selection,
        IReadOnlyList<string>? offeredYears, IReadOnlyList<RegistryHistoryCourseTopics>? current, IReadOnlyList<RegistryHistoryCourseTopics>? historical, CancellationToken token)
    {
        directory = Path.GetFullPath(directory);
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) != ".registry-history")
            throw new IOException("Private history output requires a directory named .registry-history.");
        RejectSymbolicDirectories(directory);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var runId = Guid.NewGuid().ToString("N");
        var files = new List<string>();
        var sources = new List<RegistryHistoryTopicFileSource>();
        foreach (var candidate in selection.Candidates)
        foreach (var group in candidate.Historical.Groups)
        foreach (var lessons in group.Lessons.GroupBy(x => x.LessonType))
        {
            var ordered = lessons.OrderBy(x => x.Date).ToArray();
            var name = $"topics-{runId}-{files.Count + 1}.csv";
            await using (var stream = NewPrivateFile(Path.Combine(directory, name)))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync("name,lessonType".AsMemory(), token);
                foreach (var lesson in ordered)
                {
                    var line = "\"" + lesson.Topic.Replace("\"", "\"\"") + "\"," + lesson.LessonType;
                    await writer.WriteLineAsync(line.AsMemory(), token);
                }
            }
            files.Add(name);
            sources.Add(new(name, candidate.CurrentCourse.Name, candidate.CurrentCourse.Semester,
                candidate.Historical.Course.Name, candidate.Historical.Year, group.Name, group.Uri,
                lessons.Key, ordered.Select(x => x.Date).ToArray()));
        }
        var inventory = $"topics-candidates-{runId}.json";
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        await using (var stream = NewPrivateFile(Path.Combine(directory, inventory)))
            await JsonSerializer.SerializeAsync(stream, new
            {
                schemaVersion = 1, currentYear = selection.CurrentYear, requiresGroupMapping = true,
                offeredYears, current, historical, sources, unresolved = selection.Unresolved
            }, options, token);
        return new(inventory, files);
    }

    private static FileStream NewPrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new(path, options);
    }

    public static void ValidateDirectory(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) != ".registry-history")
            throw new IOException("Private history output requires a directory named .registry-history.");
        RejectSymbolicDirectories(directory);
    }

    private static void RejectSymbolicDirectories(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.LinkTarget is not null || current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Private history output requires regular directories.");
    }
}
