using System.Net;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MemoryCookieProvider = AngleSharp.Io.MemoryCookieProvider;
using AngleSharp.Text;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Scraping.Common;
using Tests.ScheduleCommon;

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
        })
            .ToArray();
        var resultWithNames = result.Zip(
            courseNames,
            (a, b) => new
            {
                Url = a.Url.ToString(),
                a.CourseId.Id,
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
            CurrentStudyYear = new(2025),
        });

        List<GroupForSearch> groups = new();
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = groupParseContext,
            GroupPartitionResolver = new GroupPartitionResolver(SpecializationRegistry.Empty),
            SearchGroupId = (ref g) =>
            {
                groups.Add(g);
                return [new(groups.Count)];
            },
            ParseErrorHandler = c => throw new Exception("Wrapped parse error", c.Exception),
        })
            .ToArray();

        var mapped = result.Zip(
            groups,
            (a, b) => new
            {
                Uri = a.Uri.ToString(),
                b.GroupNumber,
                SubGroup = b.SubGroupName.ToString(),
                FacultyName = b.FacultyName.ToString(),
            });
        await Verify(mapped);
    }

    [Fact]
    public void GroupLinkSplitClassifiesSpecializationsSeparately()
    {
        // Explicit null: no custom registry; only built-in specializations apply.
        var resolver = new GroupPartitionResolver(null);

        var specialization = resolver.Resolve(MakeGroupForSearch("Spring"));

        var numeric = resolver.Resolve(MakeGroupForSearch("I"));
        var registryBuilder = new SpecializationRegistryBuilder();
        var futureSpecialization = new Specialization("Future track");
        registryBuilder.Set([futureSpecialization])
            .ApplyTo(_ => { });
        var future = new GroupPartitionResolver(registryBuilder.Build()).Resolve(
            MakeGroupForSearch(futureSpecialization.Value!));

        var expected2 = new GroupPartitionKey(SubGroup.All, Specialization.Spring);
        var actual2 = specialization;
        Assert.Equal(expected: expected2, actual: actual2);
        var subGroup2 = SubGroup.CreateNumeric(1);
        var specialization2 = Specialization.All;
        var expected3 = new GroupPartitionKey(subGroup2, specialization2);
        var actual3 = numeric;
        Assert.Equal(expected: expected3, actual: actual3);
        var expected4 = new GroupPartitionKey(SubGroup.All, futureSpecialization);
        var actual4 = future;
        Assert.Equal(expected: expected4, actual: actual4);

        static GroupForSearch MakeGroupForSearch(string subGroup)
        {
            return new()
            {
                UnparsedName = "I2401",
                AttendanceMode = AttendanceMode.Zi,
                Grade = new(2),
                GroupNumber = 1,
                FacultyName = "I".AsMemory(),
                QualificationType = QualificationType.Licenta,
                SubGroupName = subGroup.AsMemory(),
                Language = null,
                IsRepeat = false,
                IsWildcard = false,
                IsDual = false,
            };
        }
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
            addDoc = await browser.OpenAsync(req => req.Content(str)
                .Address("https://test.com"));
        }

        var table = HtmlSearch.FindAttendanceTable(addDoc);
        var students = HtmlSearch.FindStudents(table);
        var attendance = students.Select(x => x.IsExpelled ? Attendance.None : Attendance.Present)
            .ToArray();
        attendance[0] = Attendance.MotivatedAbsent;
        attendance[1] = Attendance.NotPresent;
        attendance[10] = Attendance.NotPresent;
        attendance[15] = Attendance.NotPresent;

        var schedule = ScheduleBuilder.Create(b =>
        {
            b.SetStudyYear(2025);
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

        var lessonId = schedule.EnumerateWeeklyLessons()
            .First().Id
            .AsAny();
        HtmlSearch.UpdateForm(new()
        {
            Schedule = schedule,
            Document = addDoc,
            ExpectedStudents = null,
            Lesson = new()
            {
                Attendance = attendance,
                Topic = "My Topic",
                DateTime = new DateTime(
                    year: 2026,
                    day: 11,
                    month: 10),
                LessonId = lessonId,
                RegistryLessonType = schedule.Get(lessonId).Lesson.Type,
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

    [Fact]
    public async Task UpdateFormMismatchIncludesExpulsionStatus()
    {
        var browser = BrowsingContext.New(Configuration.Default);
        IDocument addDoc;
        {
            await using var stream = File.OpenRead(CreateLessonHtmlPath);
            using var reader1 = new StreamReader(stream);
            var str = await reader1.ReadToEndAsync();
            addDoc = await browser.OpenAsync(req => req.Content(str)
                .Address("https://test.com"));
        }

        var table = HtmlSearch.FindAttendanceTable(addDoc);
        var students = HtmlSearch.FindStudents(table);
        var expectedStudents = students.ToArray();
        expectedStudents[0] = expectedStudents[0] with
        {
            IsExpelled = !students[0].IsExpelled,
        };
        var attendance = students.Select(x => x.IsExpelled ? Attendance.None : Attendance.Present)
            .ToArray();

        var schedule = ScheduleBuilder.Create(b =>
        {
            b.SetStudyYear(2025);
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

        var lessonId = schedule.EnumerateWeeklyLessons()
            .First().Id
            .AsAny();
        var error = Assert.Throws<RegistryFormStudentMismatchException>(() =>
        {
            HtmlSearch.UpdateForm(new()
            {
                Schedule = schedule,
                Document = addDoc,
                ExpectedStudents = expectedStudents,
                Lesson = new()
                {
                    Attendance = attendance,
                    Topic = "My Topic",
                    DateTime = new DateTime(
                        year: 2026,
                        day: 11,
                        month: 10),
                    LessonId = lessonId,
                    RegistryLessonType = schedule.Get(lessonId).Lesson.Type,
                },
            });
        });

        var expectedStudent = new RegistryFormStudent(expectedStudents[0].Name, expectedStudents[0].IsExpelled);
        var actualStudent = new RegistryFormStudent(students[0].Name, students[0].IsExpelled);
        Assert.Equal(expected: 0, actual: error.Index);
        Assert.Equal(expected: expectedStudent, actual: error.Expected);
        Assert.Equal(expected: actualStudent, actual: error.Actual);
        var expectedExpelledText = $"expelled: {expectedStudents[0].IsExpelled}";
        var actualExpelledText = $"expelled: {students[0].IsExpelled}";
        Assert.Contains(
            expectedSubstring: expectedExpelledText,
            actualString: error.Message);
        Assert.Contains(
            expectedSubstring: actualExpelledText,
            actualString: error.Message);
    }

    [Fact]
    public async Task SendFormValidationSummaryExposesErrorsAsImmutableSnapshot()
    {
        using var handler = new ValidationSummaryHandler(request =>
        {
            string html;
            if (request.Method == HttpMethod.Post)
            {
                html =
                    "<div class='validation-summary-errors'><ul><li>Missing topic</li><li>Invalid date</li></ul></div>";
            }
            else
            {
                html = "<form method='post' action='/submit'><input id='LessonDate' name='LessonDate' /></form>";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    html,
                    Encoding.UTF8,
                    "text/html"),
            };
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new NoOpAuthHandler());

        var document = await context.Browser.OpenAsync("https://registry.test/lesson");
        var error = await Assert.ThrowsAsync<RegistryFormValidationRejectedException>(() =>
            HtmlSearch.SendForm(document));

        Assert.Equal<string>(
            expected: ["Missing topic", "Invalid date"],
            actual: error.ValidationErrors);
        Assert.Contains(expectedSubstring: "Missing topic", actualString: error.Message);
        Assert.Contains(expectedSubstring: "Invalid date", actualString: error.Message);
    }
}

file sealed class NoOpAuthHandler : IAuthHandler
{
    public Task Authenticate(CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class ValidationSummaryHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(send(request));
}

file sealed class LessonFailErrorHandler : IRegistryLessonParserErrorHandler
{
    public void CustomLessonType(ReadOnlySpan<char> ch)
    {
        throw new NotImplementedException();
    }
}
