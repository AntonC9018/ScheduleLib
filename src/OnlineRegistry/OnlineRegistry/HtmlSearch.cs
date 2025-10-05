using System.Diagnostics;
using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry;

internal readonly record struct CourseLink(
    CourseId CourseId,
    Uri Url);

internal readonly record struct GroupLink
{
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required Uri Uri { get; init; }
}

internal readonly record struct RemoteLessonInstance : IDateTime
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
    public required IRegistryErrorHandler ErrorHandler { get; init; }
    public required LookupModule LookupModule { private get; init; }
    public required CourseNameUnifierModule CourseNameUnifier { private get; init; }
    public required Semester Semester { get; init; }

    internal CourseId? FindCourse(string name)
    {
        var ret = CourseNameUnifier.Find(new()
        {
            Lookup = LookupModule,
            CourseName = name,
            ParseOptions = new()
            {
                IgnorePunctuation = true,
            },
        });
        return ret;
    }
}

internal readonly struct ScanGroupsParams
{
    public required IDocument Document { get; init; }
    public required GroupParseContext GroupParseContext { get; init; }
    public required Schedule Schedule { get; init; }
    public required IRegistryErrorHandler ErrorHandler { get; init; }
}


internal readonly struct ScanLessonsParams
{
    public required IDocument Document { get; init; }
    public required IRegistryErrorHandler ErrorHandler { get; init; }
    public required Func<Task<IDocument>> GetAddLessonDocument { get; init; }
}

internal readonly struct ScanLessonResult
{
    public required IEnumerable<RemoteLessonInstance> Lessons { get; init; }
    public required HtmlStudent[] Students { get; init; }
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
            if (courseName.Length == 0)
            {
                p.ErrorHandler.LessonWithoutName();
                continue;
            }
            if (p.FindCourse(courseName) is not { } courseId)
            {
                p.ErrorHandler.CourseNotFound(courseName);
                continue;
            }
            yield return new(courseId, new(url));
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
        const string path = """form[name="lesson"] > div.row:nth-of-type(2) > div.col:nth-of-type(1) > div.row > a:nth-of-type(1)""";
        var anchors = p.Document.QuerySelectorAll(path);
        foreach (var el in anchors)
        {
            var anchor = (IHtmlAnchorElement) el;
            var url = anchor.Href;
            var groupName = anchor.Text;
            var groupForSearch = RegistryScraping.ParseGroupFromOnlineRegistry(p.GroupParseContext, groupName);
            var groupId = FindGroupMatch(p.Schedule, groupForSearch);
            if (groupId == GroupId.Invalid)
            {
                p.ErrorHandler.GroupNotFound(groupName);
                continue;
            }

            var uri = new Uri(url);

            SubGroup SubGroup()
            {
                string? subgroupName = null;
                if (!groupForSearch.SubGroupName.IsEmpty)
                {
                    subgroupName = groupForSearch.SubGroupName.ToString();
                }
                var subgroup = new SubGroup(subgroupName);
                return subgroup;
            }

            yield return new()
            {
                Uri = uri,
                GroupId = groupId,
                SubGroup = SubGroup(),
            };
        }
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

    internal static async ValueTask<ScanLessonResult> ScanLessonsDocumentForLessonInstances(
        ScanLessonsParams p)
    {
        const string attendanceTablePath = """main > div:last-of-type table""";
        var attendanceTable = (IHtmlTableElement) (
            p.Document.QuerySelector(attendanceTablePath)
                ?? throw new InvalidOperationException("Attendance table not found in the page"));
        var headerRow = attendanceTable.Rows[0];
        var columnCount = headerRow.Cells.Length;
        int attendanceStartColIndex = 3;
        int attendanceLessonCount = columnCount - attendanceStartColIndex;
        int attendanceStartRowIndex = 1; // skip header
        int studentCount = attendanceTable.Rows.Length - attendanceStartRowIndex;

        const string lessonTablePath = """main > div:nth-of-type(3) > table""";
        var lessonTable = (IHtmlTableElement) (p.Document.QuerySelector(lessonTablePath)
            ?? throw new InvalidOperationException("Lesson table not found in the page"));
        int lessonRowStart = 1;
        int lessonCount = lessonTable.Rows.Length - lessonRowStart;

        IEnumerable<IHtmlTableCellElement> AttendanceCells(int column)
        {
            for (int i = 0; i < studentCount; i++)
            {
                var row = attendanceTable.Rows[attendanceStartRowIndex + i];
                var cell = row.Cells[column];
                yield return cell;
            }
        }

        if (attendanceLessonCount != lessonCount)
        {
            throw new InvalidOperationException("Attendance and lesson count mismatch");
        }

        var studentNames = AttendanceCells(1)
            .Select(ParseCellAsStudent)
            .ToArray();
        if (studentNames.Length == 0)
        {
            // Must query this from the lesson add thing.
            var addDoc = await p.GetAddLessonDocument();
            var table = (IHtmlTableElement) addDoc.QuerySelectorAll("table").Last();
            studentNames = FindStudents(table);
        }

        var ret = new ScanLessonResult
        {
           Lessons = E(),
           Students = studentNames,
        };
        return ret;

        IEnumerable<RemoteLessonInstance> E()
        {
            for (int i = 0; i < lessonCount; i++)
            {
                // NOTE: these are going to throw an invalid cast if anything is weird with the nodes.
                var lessonRow = lessonTable.Rows[lessonRowStart + i];
                var attendanceCells = AttendanceCells(attendanceStartColIndex + i);
                var first = ProcessFirst(lessonRow.Cells[0], p.ErrorHandler);
                var topic = ExtractTopic(lessonRow.Cells[1]);
                var editUri = ProcessEdit(lessonRow.Cells[2]);
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
            IRegistryErrorHandler errorHandler)
        {
            var dateTimeAndTypeCell = (IHtmlTableDataCellElement) cell;
            var children = dateTimeAndTypeCell.ChildNodes;

            LessonType lessonType;
            {
                var typeNode = children[^1];
                var typeText = typeNode.TextContent;
                lessonType = RegistryScraping.ParseLessonType(typeText, errorHandler);
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
            var topicRaw = cell.Text();
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
            var b = ArrayBuilder.Create<Attendance>(studentCount);
            foreach (var x in attendanceValues)
            {
                var t = x.Text();
                var attendance = AttendanceHelper.Parse(t);
                b.Add(attendance);
            }
            return b.Complete();
        }
    }

    private static GroupId FindGroupMatch(Schedule schedule, in GroupForSearch g)
    {
        var groups = schedule.Groups;
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (IsMatch(group, g))
            {
                return new(i);
            }
        }
        return GroupId.Invalid;
    }

    private static bool IsMatch(Group a, in GroupForSearch b)
    {
        bool facultyMatches = a.Faculty.Name.AsSpan().Equals(
            b.FacultyName.Span,
            StringComparison.OrdinalIgnoreCase);
        if (!facultyMatches)
        {
            return false;
        }

        if (a.GroupNumber != b.GroupNumber)
        {
            return false;
        }

        if (a.AttendanceMode != b.AttendanceMode)
        {
            return false;
        }

        if (a.QualificationType != b.QualificationType)
        {
            return false;
        }

        if (a.Grade != b.Grade)
        {
            return false;
        }

        return true;
    }

    internal static HtmlStudent ParseCellAsStudent(IHtmlTableCellElement x)
    {
        var t = x.TextContent;
        Debug.Assert(!t.EndsWith(" exmatr"));
        var i = x.QuerySelector("i.text-danger");
        bool isExtmatr = false;
        if (i != null)
        {
            isExtmatr = i.TextContent == "exmatr";
        }
        return new HtmlStudent(x.TextContent, isExtmatr);
    }

    internal static HtmlStudent[] FindStudents(IHtmlTableElement table)
    {
        int firstRow = 1;
        var ret = new HtmlStudent[table.Rows.Length - firstRow];
        for (int i = 0; i < ret.Length; i++)
        {
            var row = table.Rows[i];
            ret[i] = ParseCellAsStudent(row.Cells[1]);
        }
        return ret;
    }
}
