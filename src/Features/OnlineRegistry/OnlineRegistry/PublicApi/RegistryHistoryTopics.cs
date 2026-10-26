using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace ScheduleLib.OnlineRegistry;

public sealed record RegistryHistoryTopic(DateTime Date, LessonType LessonType, string Topic);

public sealed record RegistryHistoryGroupTopics(string Name, Uri Uri, IReadOnlyList<RegistryHistoryTopic> Lessons);

public sealed record RegistryHistoryCourseTopics(
    string Year,
    RegistryHistoryCourse Course,
    IReadOnlyList<RegistryHistoryGroupTopics> Groups);

public sealed record RegistryHistoryTopicCandidate(
    RegistryHistoryCourse CurrentCourse,
    RegistryHistoryCourseTopics Historical);

public enum RegistryHistoryTopicMappingFailure { NoHistoricalCourse, AmbiguousCourse, NoTopics }

public sealed record RegistryHistoryUnresolvedCourse(
    RegistryHistoryCourse CurrentCourse,
    RegistryHistoryTopicMappingFailure Reason);

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

        static bool HeaderMatches(string? sourceText, ReadOnlySpan<char> expectedText)
        {
            var textLength = sourceText!.Length;
            var textSpan = sourceText.AsSpan(start: 0, length: textLength);
            var trimmedText = textSpan.Trim();
            return trimmedText.SequenceEqual(expectedText);
        }

        if (table is null)
        {
            throw InvalidMarkup();
        }
        if (table.Rows.Length == 0)
        {
            throw InvalidMarkup();
        }
        var header = table.Rows[0];
        if (header.Cells.Length != 3)
        {
            throw InvalidMarkup();
        }
        if (header.Cells.Any(cell => cell.LocalName != "th"))
        {
            throw InvalidMarkup();
        }
        if (!HeaderMatches(header.Cells[0].TextContent, "Data"))
        {
            throw InvalidMarkup();
        }
        if (!HeaderMatches(header.Cells[1].TextContent, "Tema"))
        {
            throw InvalidMarkup();
        }
        if (!HeaderMatches(header.Cells[2].TextContent, "Redactare"))
        {
            throw InvalidMarkup();
        }

        var result = new List<RegistryHistoryTopic>();
        foreach (var row in table.Rows.Skip(1))
        {
            if (row.Cells.Length != 3
                || row.Cells.Take(2)
                    .Any(x => x.LocalName != "td"))
            {
                throw InvalidMarkup();
            }

            var dateCell = row.Cells[0];
            var anchors = dateCell.Children.OfType<IHtmlAnchorElement>()
                .ToArray();

            bool CalculateConditionResult()
            {
                if (anchors.Length != 1)
                {
                    return true;
                }

                if (dateCell.Children.Length != 1)
                {
                    return true;
                }

                return false;
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
            {
                throw InvalidMarkup();
            }

            var anchor = anchors[0];
            if (!DateTime.TryParseExact(s: anchor.TextContent.Trim(),
                    format: "dd.MM.yyyy HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                throw InvalidMarkup();
            }

            var typeText = string.Concat(dateCell.ChildNodes.Where(x => x != anchor)
                    .Select(x => x.TextContent))
                .Trim();
            var type = typeText switch
            {
                "laborator" => LessonType.Lab,
                "curs" => LessonType.Curs,
                "seminar" => LessonType.Seminar,
                _ => throw InvalidMarkup(),
            };
            var raw = row.Cells[1].TextContent.AsSpan()
                .TrimStart();
            var numberLength = 0;
            while (numberLength < raw.Length && char.IsAsciiDigit(raw[numberLength]))
            {
                numberLength++;
            }
            // Observed leading registry sequence number is separated by whitespace.
            bool conditionResult2;
            if (numberLength > 0)
            {
                if (numberLength < raw.Length)
                {
                    conditionResult2 = char.IsWhiteSpace(raw[numberLength]);
                }
                else
                {
                    conditionResult2 = false;
                }
            }
            else
            {
                conditionResult2 = false;
            }

            if (conditionResult2)
            {
                raw = raw[numberLength..].TrimStart();
            }

            result.Add(new(date,
                type,
                raw.TrimEnd()
                    .ToString()));
        }
        return result;
    }

    private static RegistryHistoryException InvalidMarkup()
    {
        return new("Lesson topic table layout is unsupported; inspection is required.",
            phase: RegistryHistoryPhase.Lessons,
            diagnostic: RegistryHistoryDiagnostic.TopicTable);
    }
}

/// <summary>Exact course-name matching except surrounding whitespace, a trailing comma and letter case. Raw names remain in provenance.</summary>
public static class RegistryHistoryTopicSelection
{
    /// <summary>An offered academic year that precedes the current one, ordered by its start.</summary>
    private readonly record struct PrecedingYear(string Year, int Start);

    public static string NormalizeCourseName(string name)
    {
        return name.Trim()
            .TrimEnd(',')
            .TrimEnd();
    }

    public static int AcademicYearStart(string year)
    {
        if (year.Length != 7)
        {
            throw new RegistryHistoryException("Academic-year value cannot be ordered safely; inspection is required.");
        }

        if (year[4] != '/')
        {
            throw new RegistryHistoryException("Academic-year value cannot be ordered safely; inspection is required.");
        }

        bool conditionValue = int.TryParse(year.AsSpan(start: 0, length: 4),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var start);
        if (!conditionValue)
        {
            throw new RegistryHistoryException("Academic-year value cannot be ordered safely; inspection is required.");
        }

        bool conditionValue2 = int.TryParse(year.AsSpan(start: 5, length: 2),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var end);
        if (!conditionValue2)
        {
            throw new RegistryHistoryException("Academic-year value cannot be ordered safely; inspection is required.");
        }

        if ((start + 1) % 100 != end)
        {
            throw new RegistryHistoryException("Academic-year value cannot be ordered safely; inspection is required.");
        }

        return start;
    }

    public static bool Matches(RegistryHistoryCourse current, RegistryHistoryCourse historical)
    {
        return RegistryHistoryCourseIdentityComparer.Instance.Equals(current, historical);
    }

    public static RegistryHistoryTopicSelectionResult Select(
        string currentYear,
        IReadOnlyList<string> offeredYears,
        IReadOnlyList<RegistryHistoryCourse> currentCourses,
        IReadOnlyList<RegistryHistoryCourseTopics> historical)
    {
        var currentStart = AcademicYearStart(currentYear);
        var previous = offeredYears.Distinct(StringComparer.Ordinal)
            .Select(x => new PrecedingYear(x, AcademicYearStart(x)))
            .Where(x => x.Start < currentStart)
            .OrderByDescending(x => x.Start)
            .ToArray();
        var candidates = new List<RegistryHistoryTopicCandidate>();
        var unresolved = new List<RegistryHistoryUnresolvedCourse>();
        foreach (var course in currentCourses)
        {
            if (currentCourses.Count(x => Matches(current: course, historical: x)) != 1)
            {
                var list = unresolved;
                RegistryHistoryUnresolvedCourse item = new(course,
                    RegistryHistoryTopicMappingFailure.AmbiguousCourse);
                list.Add(item);
                continue;
            }
            var found = false;
            var resolved = false;
            foreach (var year in previous)
            {
                var matches = historical
                    .Where(x => x.Year == year.Year && Matches(current: course, historical: x.Course))
                    .ToArray();
                if (matches.Length == 0)
                {
                    continue;
                }

                found = true;
                if (matches.Length > 1)
                {
                    var list2 = unresolved;
                    RegistryHistoryUnresolvedCourse item2 = new(course,
                        RegistryHistoryTopicMappingFailure.AmbiguousCourse);
                    list2.Add(item2);
                    resolved = true;
                    break;
                }
                if (!matches[0].Groups.Any(g => g.Lessons.Any(l =>
                    {
                        if (!!string.IsNullOrWhiteSpace(l.Topic))
                        {
                            return false;
                        }
                        if (!!RegistryAssessmentTopics.IsAssessment(l.Topic))
                        {
                            return false;
                        }
                        return true;
                    })))
                {
                    continue;
                }

                var list3 = candidates;
                RegistryHistoryTopicCandidate item3 = new(course, matches[0]);
                list3.Add(item3);
                resolved = true;
                break;
            }
            if (!resolved)
            {
                var list4 = unresolved;
                var currentCourse = course;
                RegistryHistoryTopicMappingFailure reason;
                if (found)
                {
                    reason = RegistryHistoryTopicMappingFailure.NoTopics;
                }
                else
                {
                    reason = RegistryHistoryTopicMappingFailure.NoHistoricalCourse;
                }

                RegistryHistoryUnresolvedCourse item4 = new(currentCourse, reason);
                list4.Add(item4);
            }
        }
        return new(currentYear,
            candidates,
            unresolved);
    }
}

/// <summary>Sequential acquisition through the history transport guard. This reader does not resolve group meanings or build import mappings.</summary>
public sealed class RegistryHistoryTopicsReader(HttpClient client, Uri index)
{
    /// <summary>An index year link that precedes the current academic year, ordered by its start.</summary>
    private readonly record struct PrecedingYearLink(RegistryHistoryYear Year, int Start);

    public async Task<RegistryHistoryTopicsAnalysis> Discover(CancellationToken token)
    {
        var reader = new RegistryHistoryReader(client, index);
        using var initial = await reader.Read(index, token);
        var years = RegistryHistoryHtml.Years(initial, index);
        var currentYear = years.Single(x => x.Selected).Value;
        var currentStart = RegistryHistoryTopicSelection.AcademicYearStart(currentYear);
        // Validate every year before requesting any historical navigation.
        var previous = years
            .Select(x => new PrecedingYearLink(x, RegistryHistoryTopicSelection.AcademicYearStart(x.Value)))
            .Where(x => x.Start < currentStart)
            .OrderByDescending(x => x.Start)
            .ToArray();
        var currentCourses = RegistryHistoryHtml.Courses(initial, index);
        var current = new List<RegistryHistoryCourseTopics>();
        foreach (var course in currentCourses)
        {
            current.Add(await ReadCourse(currentYear, course));
        }

        var historical = new List<RegistryHistoryCourseTopics>();
        var pending = currentCourses.Where(course =>
                currentCourses.Count(other => RegistryHistoryTopicSelection.Matches(current: course, historical: other))
                == 1)
            .ToList();
        var ambiguousPending = currentCourses.Where(course =>
                currentCourses.Count(other => RegistryHistoryTopicSelection.Matches(current: course, historical: other))
                > 1)
            .Distinct(RegistryHistoryCourseIdentityComparer.Instance)
            .ToList();
        foreach (var year in previous)
        {
            bool CalculateConditionResult()
            {
                if (pending.Count != 0)
                {
                    return false;
                }

                if (ambiguousPending.Count == 0)
                {
                    return true;
                }

                return false;
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
            {
                break;
            }

            using var document = await reader.Read(year.Year.Uri, token);
            if (!RegistryHistoryHtml.Years(document, index)
                    .Any(x => x.Selected && x.Value == year.Year.Value))
            {
                throw new RegistryHistoryException(
                    "Registry did not confirm the requested academic year; topic discovery stopped.",
                    RegistryHistoryFailure.YearNotConfirmed,
                    RegistryHistoryPhase.Index,
                    diagnostic: RegistryHistoryDiagnostic.YearSelectionNotConfirmed);
            }

            var yearCourses = RegistryHistoryHtml.Courses(document, index);
            var matching = yearCourses.Where(course =>
                    pending.Any(currentCourse =>
                        RegistryHistoryTopicSelection.Matches(current: currentCourse, historical: course))
                    || ambiguousPending.Any(currentCourse =>
                        RegistryHistoryTopicSelection.Matches(current: currentCourse, historical: course)))
                .ToArray();
            var snapshots = new List<RegistryHistoryCourseTopics>();
            foreach (var course in matching)
            {
                var snapshot = await ReadCourse(year.Year.Value, course);
                historical.Add(snapshot);
                snapshots.Add(snapshot);
            }
            pending.RemoveAll(course =>
            {
                var matches = snapshots.Where(snapshot =>
                        RegistryHistoryTopicSelection.Matches(current: course, historical: snapshot.Course))
                    .ToArray();
                return matches.Length > 1
                    || matches.Any(snapshot => snapshot.Groups.Any(group => group.Lessons.Any(lesson =>
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
                    })));
            });
            ambiguousPending.RemoveAll(course => snapshots.Any(snapshot =>
                RegistryHistoryTopicSelection.Matches(current: course, historical: snapshot.Course)
                && snapshot.Groups.Any(group => group.Lessons.Any(lesson =>
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
                }))));
        }
        var offered = years.Select(x => x.Value)
            .ToArray();
        IReadOnlyList<string> offeredYears = offered;
        IReadOnlyList<RegistryHistoryCourseTopics>
            current2 = current;
        var selection =
            RegistryHistoryTopicSelection.Select(currentYear,
                offered,
                currentCourses,
                historical);
        IReadOnlyList<RegistryHistoryCourseTopics>
            historical2 = historical;
        RegistryHistoryTopicsAnalysis registryHistoryTopicsAnalysis =
            new(offeredYears,
                Current: current2,
                selection,
                Historical: historical2);
        return registryHistoryTopicsAnalysis;

        async Task<RegistryHistoryCourseTopics> ReadCourse(string year, RegistryHistoryCourse course)
        {
            using var document = await reader.Read(course.Uri, token);
            var groups = new List<RegistryHistoryGroupTopics>();
            foreach (var group in RegistryHistoryHtml.Groups(document, course.Uri))
            {
                using var lessons = await reader.Read(group.Uri, token);
                var
                    list = groups;
                string name = group.Name;
                var uri = group.Uri;
                var lessons2 =
                    RegistryHistoryTopicsHtml.Lessons(lessons);
                RegistryHistoryGroupTopics item = new(name,
                    uri,
                    lessons2);
                list.Add(item);
            }
            return new(year,
                course,
                groups);
        }
    }
}
