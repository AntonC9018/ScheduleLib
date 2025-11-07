using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry;

public readonly record struct CourseLink(
    CourseId CourseId,
    Uri Url);

public readonly record struct GroupLink
{
    public readonly FoundGroups Groups;
    public readonly SubGroup SubGroup;
    public readonly Uri Uri;
    public readonly Uri EvaluationUri;

    public GroupLink(
        in FoundGroups groups,
        SubGroup subGroup,
        Uri uri,
        Uri evaluationUri)
    {
        Groups = groups;
        SubGroup = subGroup;
        Uri = uri;
        EvaluationUri = evaluationUri;
    }
}

public readonly record struct RemoteLessonInstance : IDateTime
{
    public required DateTime DateTime { get; init; }
    public required LessonType LessonType { get; init; }
    public required Uri EditUri { get; init; }
    public required Uri ViewUri { get; init; }
    public required Attendance[] Attendance { get; init; }
    public required string? Topic { get; init; }
}

internal readonly struct ScanCoursesParams
{
    public required IDocument Document { get; init; }
    public required Semester Semester { get; init; }
    public required Func<string, CourseId?> FindCourse { get; init; }
}

internal readonly struct ScanGroupsParams
{
    public required IDocument Document { get; init; }
    public required GroupParseContext GroupParseContext { get; init; }
    public required SearchGroupId SearchGroupId { get; init; }
}

internal delegate LessonGroups SearchGroupId(in GroupForSearch group);


internal readonly struct ScanLessonsParams
{
    public required IDocument Document { get; init; }
    public required IRegistryLessonParserErrorHandler ErrorHandler { get; init; }
    public required Func<Task<IDocument>> GetAddLessonDocument { get; init; }
}

internal readonly struct ScanLessonResult
{
    public required IEnumerable<RemoteLessonInstance> Lessons { get; init; }
    public required HtmlStudent[] Students { get; init; }
    public required bool IsValid { get; init; }
}

internal readonly record struct HtmlStudent(
    string Name,
    bool IsExpelled);

internal static class HtmlSearch
{
    internal static IEnumerable<CourseLink> ScanCoursesDocumentForLinks(ScanCoursesParams p)
    {
        var queryString = $"#nav-{SemString(p.Semester)} > div > span:nth-of-type(2) > a";

        var anchors = p.Document.QuerySelectorAll(queryString);
        foreach (var el in anchors)
        {
            var anchor = (IHtmlAnchorElement) el;
            var url = anchor.Href;
            var courseName = anchor.Text;
            if (p.FindCourse(courseName) is { } courseId)
            {
                yield return new(courseId, new(url));
            }
        }

        static string SemString(Semester session)
        {
            return session switch
            {
                Semester.Sem1 => "1",
                Semester.Sem2 => "2",
                _ => throw new InvalidOperationException("??"),
            };
        }
    }

    internal static IEnumerable<GroupLink> ScanGroupsDocumentForLinks(ScanGroupsParams p)
    {
        const string path = """form[name="lesson"] > div.row:nth-of-type(2) > div.col:nth-of-type(1) > div.row""";
        var rows = p.Document.QuerySelectorAll<IHtmlDivElement>(path);
        foreach (var row in rows)
        {
            var urls = row.QuerySelectorAll<IHtmlAnchorElement>("a").ToArray();
            if (urls.Length != 2)
            {
                throw new InvalidOperationException("Expected 2 urls");
            }

            Uri groupUri;
            SubGroup subGroup;
            FoundGroups foundGroups;
            {
                var anchor = urls[0];
                var url = anchor.Href;
                var groupName = anchor.Text;
                var groupForSearch = RegistryScraping.ParseGroupFromOnlineRegistry(p.GroupParseContext, groupName);
                if (groupForSearch.IsRepeat)
                {
                    // Not handling this yet.
                    continue;
                }
                var groups = p.SearchGroupId(groupForSearch);
                if (groups.Count == 0)
                {
                    continue;
                }
                if (groupForSearch.IsWildcard && groups.Count == 0)
                {
                    throw new InvalidOperationException("Must match a single group if not wildcard.");
                }
                subGroup = SubGroupFromString(groupForSearch);
                groupUri = new Uri(url);
                foundGroups = new()
                {
                    Value = groups,
                    IsWildcard = groupForSearch.IsWildcard,
                };

            }

            Uri evaluationUri;
            {
                var anchor = urls[1];
                var url = anchor.Href;
                evaluationUri = new Uri(url);
            }

            yield return new(
                uri: groupUri,
                evaluationUri: evaluationUri,
                groups: foundGroups,
                subGroup: subGroup);
        }
    }

    internal static SubGroup SubGroupFromString(in GroupForSearch groupForSearch)
    {
        string? subgroupName = null;
        if (!groupForSearch.SubGroupName.IsEmpty)
        {
            subgroupName = groupForSearch.SubGroupName.ToString();
        }
        var subgroup = new SubGroup(subgroupName);
        return subgroup;
    }

    internal static Uri ScanForLessonAddLink(IDocument doc)
    {
        var anchor = doc
            .QuerySelectorAll<IHtmlAnchorElement>("div > a")
            .First(a => IgnoreDiacriticsAndCaseComparer.Instance.Equals(a.TextContent, "Adaugare"));
        var href = anchor.Href;
        var uri = new Uri(href);
        return uri;
    }

    private struct AttendanceTableHelper
    {
        private readonly IHtmlTableElement? _table;

        public AttendanceTableHelper(IHtmlTableElement? table)
        {
            _table = table;
        }

        private const int attendanceStartColIndex = 3;
        private const int attendanceStartRowIndex = 1; // skip header

        public int StudentCount
        {
            get
            {
                if (_table == null)
                {
                    return 0;
                }
                return _table.Rows.Length - attendanceStartRowIndex;
            }
        }

        public int LessonCount
        {
            get
            {
                if (_table == null)
                {
                    return 0;
                }
                var headerRow = _table.Rows[0];
                var columnCount = headerRow.Cells.Length;
                return columnCount - attendanceStartColIndex;
            }
        }

        public IEnumerable<IHtmlTableCellElement> Cells(int column)
        {
            for (int i = 0; i < StudentCount; i++)
            {
                var row = _table!.Rows[attendanceStartRowIndex + i];
                var cell = row.Cells[column];
                yield return cell;
            }
        }

        public IEnumerable<IHtmlTableCellElement> DayCells(int index)
        {
            return Cells(attendanceStartColIndex + index);
        }
    }

    internal static async ValueTask<ScanLessonResult> ScanLessonsDocumentForLessonInstances(
        ScanLessonsParams p)
    {
        var tables = p.Document.QuerySelectorAll<IHtmlTableElement>("table").Take(2).ToArray();
        var lessonTable = tables[0];
        int lessonRowStart = 1;
        int lessonCount = lessonTable.Rows.Length - lessonRowStart;

        var attendanceTable = new AttendanceTableHelper(tables.Length > 1 ? tables[1] : null);
        if (attendanceTable.LessonCount != lessonCount)
        {
            return new ScanLessonResult
            {
                Lessons = [],
                Students = [],
                IsValid = false,
            };
            // throw new InvalidOperationException("Attendance and lesson count mismatch");
        }

        HtmlStudent[] studentNames = [];
        if (attendanceTable.StudentCount != 0)
        {
            studentNames = attendanceTable
                .Cells(1)
                .Select(ParseCellAsStudent)
                .ToArray();
        }
        if (studentNames.Length == 0)
        {
            // Must query this from the lesson add thing.
            var addDoc = await p.GetAddLessonDocument();
            var table = (IHtmlTableElement) addDoc.QuerySelector("table:last-of-type")!;
            studentNames = FindStudents(table);
        }

        var ret = new ScanLessonResult
        {
           Lessons = E(),
           Students = studentNames,
           IsValid = true,
        };
        return ret;

        IEnumerable<RemoteLessonInstance> E()
        {
            for (int i = 0; i < lessonCount; i++)
            {
                // NOTE: these are going to throw an invalid cast if anything is weird with the nodes.
                var lessonRow = lessonTable.Rows[lessonRowStart + i];
                var first = ProcessFirst(lessonRow.Cells[0], p.ErrorHandler);
                var topic = ExtractTopic(lessonRow.Cells[1]);
                var editUri = ProcessEdit(lessonRow.Cells[2]);
                var attendanceCells = attendanceTable.DayCells(i);
                var attendance = ExtractAttendance(attendanceCells);
                yield return new()
                {
                    EditUri = editUri,
                    ViewUri = first.ViewUri,
                    DateTime = first.DateTime,
                    LessonType = first.LessonType,
                    Attendance = attendance,
                    Topic = topic,
                };
                continue;
            }
        }

        static (LessonType LessonType, DateTime DateTime, Uri ViewUri) ProcessFirst(
            IHtmlTableCellElement cell,
            IRegistryLessonParserErrorHandler errorHandler)
        {
            var dateTimeAndTypeCell = (IHtmlTableDataCellElement) cell;
            var children = dateTimeAndTypeCell.ChildNodes;

            LessonType lessonType;
            {
                var typeNode = children[^1];
                var typeText = typeNode.TextContent;
                lessonType = ParseLessonType(typeText, errorHandler);
            }

            DateTime dateTime;
            Uri viewUri;
            {
                var anchor = children.OfType<IHtmlAnchorElement>().First();
                var dateTimeText = anchor.Text;
                var span = dateTimeText.AsSpan();
                span = span.Trim();
                const string format = "dd.MM.yyyy HH:mm";
                bool success = DateTime.TryParseExact(
                    format: format,
                    s: span,
                    provider: null,
                    style: DateTimeStyles.AssumeLocal,
                    result: out dateTime);
                if (!success)
                {
                    throw new NotSupportedException("The date time didn't parse properly");
                }

                viewUri = new(anchor.Href);
            }

            return (lessonType, dateTime, viewUri);
        }

        static string ExtractTopic(IHtmlTableCellElement cell)
        {
            var topicRaw = cell.TextContent;
            var parser = new Parser(topicRaw);

            // Number in front.
            parser.SkipWhitespace();
            parser.SkipNumbers();
            parser.SkipWhitespace();

            // Padded with space at the end.
            var t = parser.PeekSpanUntilEnd().TrimEnd();
            var topic = t.ToString();
            return topic;
        }

        static Uri ProcessEdit(
            IHtmlTableCellElement cell)
        {
            var editCell = (IHtmlTableDataCellElement) cell;
            var editAnchor = (IHtmlAnchorElement) editCell.Children[0];
            var ret = new Uri(editAnchor.Href);
            return ret;
        }

        Attendance[] ExtractAttendance(IEnumerable<IHtmlTableCellElement> attendanceValues)
        {
            var b = ArrayBuilder.Create<Attendance>(attendanceTable.StudentCount);
            foreach (var x in attendanceValues)
            {
                var t = x.TextContent.AsSpan().Trim();
                var attendance = AttendanceHelper.Parse(t);
                b.Add(attendance);
            }
            return b.Complete();
        }
    }

    internal static IHtmlTableElement FindAttendanceTable(IDocument doc)
    {
        var table = (IHtmlTableElement) doc.QuerySelectorAll("table").Last();
        return table;
    }

    internal static IHtmlFormElement GetLessonForm(IDocument doc)
    {
        var lessonDateBox = (IHtmlInputElement) doc.GetElementById("LessonDate")!;
        return lessonDateBox.Form!;
    }

    internal static void UpdateForm(SendUpdatedFormParams p)
    {
        {
            var lessonDateBox = (IHtmlInputElement) p.Document.GetElementById("LessonDate")!;
            lessonDateBox.Value = p.Lesson.DateTime.ToString("yyyy-MM-ddTHH:mm");
            Debug.Assert(lessonDateBox.Value is not null and not "");
        }

        {
            var lessonTypeBox = (IHtmlSelectElement) p.Document.GetElementById("LessonMode")!;
            var lessonType = p.Schedule.Get(p.Lesson.LessonId).Lesson.Type;
            var lessonName = GetLessonTypeName(lessonType);
            foreach (var option in lessonTypeBox.Options)
            {
                if (lessonName is null)
                {
                    option.IsSelected = false;
                    continue;
                }
                if (option.Value.Equals(lessonName, StringComparison.Ordinal))
                {
                    option.IsSelected = true;
                    continue;
                }
                option.IsSelected = false;
            }
        }
        if (p.Lesson.Topic is { } topic)
        {
            var topicInput = (IHtmlTextAreaElement) p.Document.GetElementById("LessonTopic")!;
            topicInput.Value = topic;
        }
        if (p.Lesson.Attendance is { } attendance)
        {
            var table = FindAttendanceTable(p.Document);
            int firstIndex = 1;
            if (attendance.Length != table.Rows.Length - firstIndex)
            {
                throw new InvalidOperationException("Attendance length does not match the number of students in the HTML");
            }
            var actualStudents = FindStudents(table);

            // Find column with name frecvența/nota
            var headerRow = table.Rows[0];
            int attendanceColumnIndex = FindIndexOfAttendance();
            for (int i = 0; i < attendance.Length; i++)
            {
                var a = attendance[i];
                if (a == Attendance.None)
                {
                    continue;
                }

                var actual = actualStudents[i];

                if (p.ExpectedStudents is not null)
                {
                    var expected = p.ExpectedStudents[i];

                    string? CheckStudentsEqual()
                    {
                        Name? ParseStudent(HtmlStudent s)
                        {
                            var studentParser = new Parser(s.Name);
                            var parsedStudent = NameHelper.TryParseName(ref studentParser);
                            return parsedStudent;
                        }
                        var expected1 = ParseStudent(expected);
                        var actual1 = ParseStudent(actual);
                        if (expected1 != actual1
                            || expected.IsExpelled != actual.IsExpelled)
                        {
                            return $"Student mismatch at index {i}: expected {expected}, got {actual}";
                        }
                        return null;
                    }
                    if (CheckStudentsEqual() is { } err)
                    {
                        throw new InvalidOperationException(err);
                    }
                }

                // Has been replaced with None so should have exited already
                Debug.Assert(!actual.IsExpelled);

                var row = table.Rows[i + firstIndex];
                var cell = row.Cells[attendanceColumnIndex];
                var input = (IHtmlInputElement) cell.QuerySelector("""input:not([type="hidden"])""")!;
                input.Value = a.ToStringValue();
            }

            int FindIndexOfAttendance()
            {
                for (int i = 0; i < headerRow.Cells.Length; i++)
                {
                    var cell = headerRow.Cells[i];
                    if (cell.TextContent == "frecvența/nota")
                    {
                        return i;
                    }
                }
                throw new InvalidOperationException("Could not find attendance/grade column");
            }
        }
    }

    internal static async Task SendForm(IDocument doc)
    {
        var form = GetLessonForm(doc);
        var ret = await form.SubmitAsync();
        var validationErrors = ret.QuerySelectorAll<IHtmlDivElement>(".validation-summary-errors")
            .SelectMany(x => x.Children)
            .SelectMany(x => x.Children)
            .Select(x => x.Text())
            .ToArray();
        if (validationErrors.Length != 0)
        {
            throw new InvalidOperationException($"Validation errors: {string.Concat("\n", validationErrors)}");
        }
    }


    private static HtmlStudent ParseCellAsStudent(IHtmlTableCellElement x)
    {
        var t = x.ChildNodes
            .FirstOrDefault(a => a.NodeType == NodeType.Text && a.TextContent.AsSpan().Trim().Length > 0)
            ?? x.Children[0];
        var text = t.TextContent.AsSpan().Trim();
        Debug.Assert(!text.EndsWith(" exmatr"));
        var i = x.QuerySelector("i.text-danger");
        bool isExtmatr = false;
        if (i != null)
        {
            isExtmatr = i.TextContent.AsSpan().Trim().SequenceEqual("exmatr");
        }
        return new HtmlStudent(text.ToString(), isExtmatr);
    }

    internal static HtmlStudent[] FindStudents(IHtmlTableElement table)
    {
        const int firstRow = 1;
        var ret = new HtmlStudent[table.Rows.Length - firstRow];
        for (int i = 0; i < ret.Length; i++)
        {
            var row = table.Rows[i + firstRow];
            ret[i] = ParseCellAsStudent(row.Cells[1]);
        }
        return ret;
    }

    // Intentionally duplicated, because the strings are actually different.
    internal static LessonType ParseLessonType(
        string s,
        IRegistryLessonParserErrorHandler errorHandler)
    {
        var parser = new Parser(s);
        parser.SkipWhitespace();
        if (parser.IsEmpty)
        {
            return LessonType.Unspecified;
        }
        var bparser = parser.BufferedView();
        _ = bparser.SkipNotWhitespace();
        var lessonTypeSpan = parser.PeekSpanUntilPosition(bparser.Position);
        var lessonType = Get(lessonTypeSpan);
        if (lessonType == LessonType.Custom)
        {
            errorHandler.CustomLessonType(lessonTypeSpan);
        }

        parser.MoveTo(bparser.Position);

        parser.SkipWhitespace();
        if (!parser.IsEmpty)
        {
            throw new NotSupportedException("Lesson type not parsed fully.");
        }
        return lessonType;

        LessonType Get(ReadOnlySpan<char> str)
        {
            static bool Equal(
                ReadOnlySpan<char> str,
                string literal)
            {
                return str.Equals(
                    literal.AsSpan(),
                    StringComparison.Ordinal);
            }

            for (var i = 0; i < LessonTypeNames.Length; i++)
            {
                if (Equal(str, LessonTypeNames[i]))
                {
                    return (LessonType) i;
                }
            }
            return LessonType.Custom;
        }
    }

    internal static string? GetLessonTypeName(LessonType type)
    {
        if (LessonTypeNames.Length <= (int) type)
        {
            return null;
        }
        return LessonTypeNames[(int) type];
    }


    private static readonly ImmutableArray<string> LessonTypeNames = CreateLessonTypeNames();
    private static ImmutableArray<string> CreateLessonTypeNames()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var ret = ImmutableArray.CreateBuilder<string>();
        ret.Capacity = 3;
        ret.Count = 3;

        Set(LessonType.Lab, "laborator");
        Set(LessonType.Curs, "curs");
        Set(LessonType.Seminar, "seminar");

        Debug.Assert(ret.All(x => x != null));

        return ret.ToImmutable();

        void Set(LessonType t, string value)
        {
            ret[(int) t] = value;
        }
    }
}

internal readonly struct SendUpdatedFormParams
{
    public required IDocument Document { get; init; }
    public required LessonInstance Lesson { get; init; }
    // public required HttpClient HttpClient { get; init; }
    // public required Uri Target { get; init; }
    public required Schedule Schedule { get; init; }
    public required HtmlStudent[]? ExpectedStudents { get; init; }
}

