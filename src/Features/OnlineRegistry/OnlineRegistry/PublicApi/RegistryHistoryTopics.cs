using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace ScheduleLib.OnlineRegistry;

public sealed record RegistryHistoryTopic(DateTime Date, LessonType LessonType, string Topic);
public sealed record RegistryHistoryGroupTopics(string Name, Uri Uri, IReadOnlyList<RegistryHistoryTopic> Lessons);
public sealed record RegistryHistoryCourseTopics(string Year, RegistryHistoryCourse Course, IReadOnlyList<RegistryHistoryGroupTopics> Groups);
public sealed record RegistryHistoryTopicCandidate(RegistryHistoryCourse CurrentCourse, RegistryHistoryCourseTopics Historical);
public enum RegistryHistoryTopicMappingFailure { NoHistoricalCourse, AmbiguousCourse, NoTopics }
public sealed record RegistryHistoryUnresolvedCourse(RegistryHistoryCourse CurrentCourse, RegistryHistoryTopicMappingFailure Reason);
public sealed record RegistryHistoryTopicSelectionResult(
    string CurrentYear,
    IReadOnlyList<RegistryHistoryTopicCandidate> Candidates,
    IReadOnlyList<RegistryHistoryUnresolvedCourse> Unresolved);
public sealed record RegistryHistoryTopicsAnalysis(
    IReadOnlyList<string> OfferedYears,
    IReadOnlyList<RegistryHistoryCourseTopics> Current,
    RegistryHistoryTopicSelectionResult Selection,
    IReadOnlyList<RegistryHistoryCourseTopics>? Historical = null);

/// <summary>Reads only the first lesson table's date/type and topic cells; attendance and edit links are never inspected.</summary>
public static class RegistryHistoryTopicsHtml
{
    public static IReadOnlyList<RegistryHistoryTopic> Lessons(IDocument document)
    {
        var table = document.QuerySelector<IHtmlTableElement>("table");
        if (table is null || table.Rows.Length == 0 || table.Rows[0].Cells.Length != 3
            || table.Rows[0].Cells.Any(x => x.LocalName != "th")
            || table.Rows[0].Cells[0].TextContent.Trim() != "Data"
            || table.Rows[0].Cells[1].TextContent.Trim() != "Tema"
            || table.Rows[0].Cells[2].TextContent.Trim() != "Redactare")
            throw InvalidMarkup();
        var result = new List<RegistryHistoryTopic>();
        foreach (var row in table.Rows.Skip(1))
        {
            if (row.Cells.Length != 3 || row.Cells.Take(2).Any(x => x.LocalName != "td")) throw InvalidMarkup();
            var dateCell = row.Cells[0];
            var anchors = dateCell.Children.OfType<IHtmlAnchorElement>().ToArray();
            if (anchors.Length != 1 || dateCell.Children.Length != 1) throw InvalidMarkup();
            var anchor = anchors[0];
            if (!DateTime.TryParseExact(anchor.TextContent.Trim(), "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)) throw InvalidMarkup();
            var typeText = string.Concat(dateCell.ChildNodes.Where(x => x != anchor).Select(x => x.TextContent)).Trim();
            var type = typeText switch
            {
                "laborator" => LessonType.Lab,
                "curs" => LessonType.Curs,
                "seminar" => LessonType.Seminar,
                _ => throw InvalidMarkup()
            };
            var raw = row.Cells[1].TextContent.AsSpan().TrimStart();
            var numberLength = 0;
            while (numberLength < raw.Length && char.IsAsciiDigit(raw[numberLength])) numberLength++;
            // Observed leading registry sequence number is separated by whitespace.
            if (numberLength > 0 && numberLength < raw.Length && char.IsWhiteSpace(raw[numberLength])) raw = raw[numberLength..].TrimStart();
            result.Add(new(date, type, raw.TrimEnd().ToString()));
        }
        return result;
    }

    private static RegistryHistoryException InvalidMarkup() => new("Lesson topic table layout is unsupported; inspection is required.",
        phase: RegistryHistoryPhase.Lessons, diagnostic: RegistryHistoryDiagnostic.TopicTable);
}

/// <summary>Exact course-name matching except surrounding whitespace, a trailing comma and letter case. Raw names remain in provenance.</summary>
public static class RegistryHistoryTopicSelection
{
    public static string NormalizeCourseName(string name) => name.Trim().TrimEnd(',').TrimEnd();

    public static int AcademicYearStart(string year)
    {
        if (year.Length != 7 || year[4] != '/' || !int.TryParse(year.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(year.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var end)
            || (start + 1) % 100 != end)
            throw new RegistryHistoryException("Academic-year value cannot be ordered safely; inspection is required.");
        return start;
    }

    public static bool Matches(RegistryHistoryCourse current, RegistryHistoryCourse historical) => current.Semester == historical.Semester
        && StringComparer.OrdinalIgnoreCase.Equals(NormalizeCourseName(current.Name), NormalizeCourseName(historical.Name));

    public static RegistryHistoryTopicSelectionResult Select(string currentYear, IReadOnlyList<string> offeredYears,
        IReadOnlyList<RegistryHistoryCourse> currentCourses, IReadOnlyList<RegistryHistoryCourseTopics> historical)
    {
        var currentStart = AcademicYearStart(currentYear);
        var previous = offeredYears.Distinct(StringComparer.Ordinal).Select(x => (Year: x, Start: AcademicYearStart(x)))
            .Where(x => x.Start < currentStart).OrderByDescending(x => x.Start).ToArray();
        var candidates = new List<RegistryHistoryTopicCandidate>();
        var unresolved = new List<RegistryHistoryUnresolvedCourse>();
        foreach (var course in currentCourses)
        {
            if (currentCourses.Count(x => Matches(course, x)) != 1)
            {
                unresolved.Add(new(course, RegistryHistoryTopicMappingFailure.AmbiguousCourse));
                continue;
            }
            var found = false;
            var resolved = false;
            foreach (var year in previous)
            {
                var matches = historical.Where(x => x.Year == year.Year && Matches(course, x.Course)).ToArray();
                if (matches.Length == 0) continue;
                found = true;
                if (matches.Length > 1)
                {
                    unresolved.Add(new(course, RegistryHistoryTopicMappingFailure.AmbiguousCourse));
                    resolved = true;
                    break;
                }
                if (!matches[0].Groups.Any(g => g.Lessons.Any(l => !string.IsNullOrWhiteSpace(l.Topic) && !RegistryAssessmentTopics.IsAssessment(l.Topic)))) continue;
                candidates.Add(new(course, matches[0]));
                resolved = true;
                break;
            }
            if (!resolved) unresolved.Add(new(course, found ? RegistryHistoryTopicMappingFailure.NoTopics : RegistryHistoryTopicMappingFailure.NoHistoricalCourse));
        }
        return new(currentYear, candidates, unresolved);
    }
}

/// <summary>Sequential acquisition through the history transport guard. This reader does not resolve group meanings or build import mappings.</summary>
public sealed class RegistryHistoryTopicsReader(HttpClient client, Uri index)
{
    public async Task<RegistryHistoryTopicsAnalysis> Discover(CancellationToken token)
    {
        var reader = new RegistryHistoryReader(client, index);
        using var initial = await reader.Read(index, token);
        var years = RegistryHistoryHtml.Years(initial, index);
        var currentYear = years.Single(x => x.Selected).Value;
        var currentStart = RegistryHistoryTopicSelection.AcademicYearStart(currentYear);
        // Validate every year before requesting any historical navigation.
        var previous = years.Select(x => (Year: x, Start: RegistryHistoryTopicSelection.AcademicYearStart(x.Value)))
            .Where(x => x.Start < currentStart).OrderByDescending(x => x.Start).ToArray();
        var currentCourses = RegistryHistoryHtml.Courses(initial, index);
        var current = new List<RegistryHistoryCourseTopics>();
        foreach (var course in currentCourses) current.Add(await ReadCourse(currentYear, course));
        var historical = new List<RegistryHistoryCourseTopics>();
        var pending = currentCourses.Where(course => currentCourses.Count(other => RegistryHistoryTopicSelection.Matches(course, other)) == 1).ToList();
        var ambiguousPending = currentCourses.Where(course => currentCourses.Count(other => RegistryHistoryTopicSelection.Matches(course, other)) > 1)
            .DistinctBy(course => $"{course.Semester}\0{RegistryHistoryTopicSelection.NormalizeCourseName(course.Name)}", StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var year in previous)
        {
            if (pending.Count == 0 && ambiguousPending.Count == 0) break;
            using var document = await reader.Read(year.Year.Uri, token);
            if (!RegistryHistoryHtml.Years(document, index).Any(x => x.Selected && x.Value == year.Year.Value))
                throw new RegistryHistoryException("Registry did not confirm the requested academic year; topic discovery stopped.", RegistryHistoryFailure.YearNotConfirmed,
                    RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.YearSelectionNotConfirmed);
            var yearCourses = RegistryHistoryHtml.Courses(document, index);
            var matching = yearCourses.Where(course => pending.Any(currentCourse => RegistryHistoryTopicSelection.Matches(currentCourse, course))
                || ambiguousPending.Any(currentCourse => RegistryHistoryTopicSelection.Matches(currentCourse, course))).ToArray();
            var snapshots = new List<RegistryHistoryCourseTopics>();
            foreach (var course in matching)
            {
                var snapshot = await ReadCourse(year.Year.Value, course);
                historical.Add(snapshot);
                snapshots.Add(snapshot);
            }
            pending.RemoveAll(course =>
            {
                var matches = snapshots.Where(snapshot => RegistryHistoryTopicSelection.Matches(course, snapshot.Course)).ToArray();
                return matches.Length > 1 || matches.Any(snapshot => snapshot.Groups.Any(group => group.Lessons.Any(lesson => !string.IsNullOrWhiteSpace(lesson.Topic) && !RegistryAssessmentTopics.IsAssessment(lesson.Topic))));
            });
            ambiguousPending.RemoveAll(course => snapshots.Any(snapshot => RegistryHistoryTopicSelection.Matches(course, snapshot.Course)
                && snapshot.Groups.Any(group => group.Lessons.Any(lesson => !string.IsNullOrWhiteSpace(lesson.Topic) && !RegistryAssessmentTopics.IsAssessment(lesson.Topic)))));
        }
        var offered = years.Select(x => x.Value).ToArray();
        return new(offered, current, RegistryHistoryTopicSelection.Select(currentYear, offered, currentCourses, historical), historical);

        async Task<RegistryHistoryCourseTopics> ReadCourse(string year, RegistryHistoryCourse course)
        {
            using var document = await reader.Read(course.Uri, token);
            var groups = new List<RegistryHistoryGroupTopics>();
            foreach (var group in RegistryHistoryHtml.Groups(document, course.Uri))
            {
                using var lessons = await reader.Read(group.Uri, token);
                groups.Add(new(group.Name, group.Uri, RegistryHistoryTopicsHtml.Lessons(lessons)));
            }
            return new(year, course, groups);
        }
    }
}
