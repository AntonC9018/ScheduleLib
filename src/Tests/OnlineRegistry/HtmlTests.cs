using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Io;
using AngleSharp.Text;
using Moq;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class HtmlTests
{
    public const string CreateLessonHtmlPath = "data/create_lesson.html";
    public const string GroupsHtmlPath = "data/groups.html";
    public const string LessonsHtmlPath = "data/lessons.html";
    public const string LessonsListHtmlPath = "data/lessons_list.html";
    public const string EmptyLessonsListHtmlPath = "data/lessons_list_empty.html";

    private static IDocument Load(string doc)
    {
        var htmlParser = new HtmlParser();
        var stream = File.OpenRead(doc);
        using var source = new TextSource(stream, Encoding.UTF8);
        var result = htmlParser.ParseDocument(source);
        return result;
    }

    [Fact]
    public async Task CourseLinksHtmlTest()
    {
        var doc = Load(LessonsHtmlPath);
        var courseNames = new List<string>();
        var result = HtmlSearch.ScanCoursesDocumentForLinks(new()
        {
            Document = doc,
            Semester = Semester.Sem1,
            FindCourse = courseName =>
            {
                courseNames.Add(courseName);
                return new(courseNames.Count);
            },
        }).ToArray();
        var resultWithNames = result.Zip(courseNames, (a, b) => new
        {
            Url = a.Url.ToString(),
            Id = a.CourseId.Id,
            CourseName = b,
        });
        await Verify(resultWithNames);
    }

    [Fact]
    public async Task CourseAddButtonHtmlTest()
    {
        var doc = Load(LessonsListHtmlPath);
        var uri = HtmlSearch.ScanForLessonAddLink(doc);
        await Verify(uri.ToString());
    }

    [Fact]
    public async Task GroupLinksHtmlTest()
    {
        var doc = Load(GroupsHtmlPath);
        var groupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = 2025,
        });

        List<GroupForSearch> groups = new();
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = groupParseContext,
            SearchGroupId = (in GroupForSearch g) =>
            {
                groups.Add(g);
                return new(groups.Count);
            },
        }).ToArray();

        var mapped = result.Zip(groups, (a, b) => new
        {
            Uri = a.Uri.ToString(),
            b.GroupNumber,
            SubGroup = b.SubGroupName.ToString(),
            FacultyName = b.FacultyName.ToString(),
        });
        await Verify(mapped);
    }

    private sealed class ScanLessonVerifyModel
    {
        public required RemoteLessonInstance[] Lessons { get; init; }
        public required HtmlStudent[] Students { get; init; }
    }

    private static async ValueTask<ScanLessonVerifyModel> Scan(string docPath)
    {
        var doc = Load(docPath);
        var addDoc = Load(CreateLessonHtmlPath);
        var result = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
        {
            Document = doc,
            ErrorHandler = new LessonFailErrorHandler(),
            GetAddLessonDocument = () => Task.FromResult(addDoc),
        });
        return new()
        {
            Lessons = result.Lessons.ToArray(),
            Students = result.Students,
        };
    }

    [Fact]
    public async Task LessonDataHtmlTest()
    {
        var result = await Scan(LessonsListHtmlPath);
        await Verify(result);
    }

    [Fact]
    public async Task EmptyLessonDataHtmlTest()
    {
        var result = await Scan(EmptyLessonsListHtmlPath);
        await Verify(result);
    }

    [Fact]
    public async Task LessonEditFormSubmissionSnapshot()
    {
        IDocument addDoc;
        var browser = BrowsingContext.New(Configuration.Default);
        {
            await using var stream = File.OpenRead(CreateLessonHtmlPath);
            using var reader1 = new StreamReader(stream);
            var str = await reader1.ReadToEndAsync();
            addDoc = await browser.OpenAsync(req => req.Content(str).Address("https://test.com"));
        }

        var students = HtmlSearch.FindStudents(HtmlSearch.FindAttendanceTable(addDoc));
        var attendance = students.Select(x => x.IsExpelled ? Attendance.None : Attendance.Present).ToArray();
        attendance[0] = Attendance.MotivatedAbsent;
        attendance[1] = Attendance.NotPresent;
        attendance[10] = Attendance.NotPresent;
        attendance[15] = Attendance.NotPresent;

        var schedule = ScheduleBuilder.Create(b =>
        {
            b.SetStudyYear(25);
            var courseId = b.Course("My Course");
            var groupId = b.Group("I2501");
            b.RegularLesson(x =>
            {
                x.DayOfWeek(DayOfWeek.Friday);
                x.TimeSlot(TimeSlot.First);
                x.Course(courseId);
                x.Type(LessonType.Curs);
                x.Group(groupId);
            });
        });

        HtmlSearch.UpdateForm(new()
        {
            Schedule = schedule,
            Document = addDoc,
            ExpectedStudents = null,
            Lesson = new()
            {
                Attendance = attendance,
                Topic = "My Topic",
                DateTime = new DateTime(year: 2026, day: 11, month: 10),
                LessonId = schedule.EnumerateLessons().First().Id,
            },
        });

        var form = HtmlSearch.GetLessonForm(addDoc);
        var submission = form.GetSubmission()!;
        using var reader = new StreamReader(submission.Body);

        await Verify(new
        {
            Body = reader.ReadToEnd(),
            submission.Headers,
            submission.Method,
            submission.Referer,
            Target = submission.Target.ToString(),
            submission.MimeType,
        })
            .UseStrictJson();
    }
}

file sealed class LessonFailErrorHandler : IRegistryLessonParserErrorHandler
{
    public void CustomLessonType(ReadOnlySpan<char> ch)
    {
        throw new NotImplementedException();
    }
}
