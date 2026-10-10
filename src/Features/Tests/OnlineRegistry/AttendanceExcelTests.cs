using ClosedXML.Excel;
using AngleSharp;
using AngleSharp.Dom;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.ScheduleDefaults;
using Tests.ScheduleCommon;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class AttendanceExcelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAttendanceSheetRetainsHeadersWithoutInventingStudentRows(int days)
    {
        var builder = new StudentAttendanceListBuilder();
        builder.HintMaxCount(days);
        builder.AddLessonTypes(Enumerable.Repeat(LessonType.Curs, days).ToArray());
        var result = builder.Build(Attendance.Present);
        Assert.Empty(result.Names);
        Assert.Equal(days, result.Attendance.DayCount);
        Assert.Equal(0, result.Attendance.StudentCount);
        Assert.All(result.Attendance.Values, day => Assert.Empty(day.AsArray()));
        Assert.Equal(days, result.LessonTypes!.Value.Length);
    }

    private const string ExcelFilePath = "data/attendance.xlsx";

    [Fact]
    public async Task ExcelSnapshot()
    {
        var builder = await ScheduleTestHelper.CreateTestSchedule();
        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        builder.EnableLookupModule();
        unifier.Refresh(builder);

        var schedule = builder.Build();

        TeacherBuilderModel.NameModel name = default;
        {
            name.FirstName[0].Full = "Anton";
            name.LastName[0] = "Curmanschii";
        }
        var filteredSchedule = ScheduleTestHelper.FilterForTeacher(
            schedule,
            builder.Lookup(unifier),
            name);

        using var workbook = new XLWorkbook(ExcelFilePath);
        var attendanceBuilder = new AllStudentAttendanceListBuilder();
        var parser = new AttendanceListsExcelParser(
            builder.GroupParseContext!,
            builder.Lookup(unifier),
            LessonTypeParser.Instance,
            new SubGroupNameRemapper(builder),
            SpecializationRegistry.Empty);
        parser.Parse(new(
            builder: attendanceBuilder,
            parseParameters: new()
            {
                RepeatedCourseBehavior = RepeatedCourseBehavior.Error,
            },
            schedule: filteredSchedule,
            workbook: workbook));
        var lists = attendanceBuilder.Build(missingDaysFiller: Attendance.Present);

        await Verify(lists.Select(x => new
        {
            Course = schedule.Get(x.Key.CourseId).FullName,
            x.Key.LessonType,
            Group = schedule.Get(x.Key.Groups[0]).Name,
            SubGroup = x.Key.GroupPartition.SubGroup.Value,
            Specialization = x.Key.GroupPartition.Specialization.Value,
            StudentNames = x.Value.StudentNames
                .OrderBy(y => y.Value)
                .Select(y => new
                {
                    Name = y.Key.ToString(),
                }),
            Attendance = x.Value.Attendance.Values.Select(a => a.AsArray()),
        }));
    }

    [Theory]
    [InlineData(0, "lesson", "")]
    [InlineData(2, "lesson", "")]
    [InlineData(0, "text-date", "")]
    [InlineData(2, "text-date", "")]
    [InlineData(0, "native-date", "")]
    [InlineData(2, "text-date", "ФИО")]
    [InlineData(0, "native-date", "Name")]
    [InlineData(0, "lesson", "Nume")]
    public void LeadingBlankRowsPreserveHeaderAndAttendance(int blankRows, string headerFormat, string nameHeader)
    {
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2026);
        builder.EnableLookupModule();
        var group = builder.Group("IA2601");
        var course = builder.Course("Synthetic course");
        builder.RegularLesson(lesson =>
        {
            lesson.Group(group);
            lesson.Course(course);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
            lesson.Type(LessonType.Lab);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();
        var filtered = schedule.Filter(new());
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        if (blankRows > 0) sheet.Row(1).Height = 20;
        sheet.Cell(blankRows + 1, 1).Value = nameHeader;
        sheet.Cell(blankRows + 1, 2).Value = headerFormat switch
        {
            "text-date" => "02.09",
            "native-date" => new DateTime(2026, 9, 2),
            _ => "lab",
        };
        sheet.Cell(blankRows + 1, 3).Value = headerFormat switch
        {
            "text-date" => "09.09",
            "native-date" => new DateTime(2026, 9, 9),
            _ => "lab",
        };
        sheet.Cell(blankRows + 2, 1).Value = "Synthetic Student";
        sheet.Cell(blankRows + 2, 2).Value = "a";
        sheet.Cell(blankRows + 2, 3).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        unifier.Refresh(builder);
        var parser = new AttendanceListsExcelParser(builder.GroupParseContext!,
            builder.Lookup(unifier), LessonTypeParser.Instance,
            new SubGroupNameRemapper(builder), SpecializationRegistry.Empty);
        parser.Parse(new(new(), attendance, filtered, workbook));
        var list = Assert.Single(attendance.Build(Attendance.Present));
        Assert.Single(list.Value.StudentNames);
        Assert.Equal(2, list.Value.Attendance.Values.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbsenceMarksFromWorkbookAreSerializedIntoLessonForm(bool dateHeader)
    {
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2026);
        builder.EnableLookupModule();
        var group = builder.Group("IA2601");
        var course = builder.Course("Synthetic course");
        builder.RegularLesson(lesson =>
        {
            lesson.Group(group);
            lesson.Course(course);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
            lesson.Type(LessonType.Lab);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 2).Value = dateHeader ? "02.09" : "lab";
        sheet.Cell(2, 1).Value = "Synthetic Alpha";
        sheet.Cell(2, 2).Value = "a";
        sheet.Cell(3, 1).Value = "Synthetic Beta";
        sheet.Cell(3, 2).Value = "am";

        var attendanceBuilder = new AllStudentAttendanceListBuilder();
        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        unifier.Refresh(builder);
        var parser = new AttendanceListsExcelParser(builder.GroupParseContext!,
            builder.Lookup(unifier), LessonTypeParser.Instance,
            new SubGroupNameRemapper(builder), SpecializationRegistry.Empty);
        parser.Parse(new(new(), attendanceBuilder, schedule.Filter(new()), workbook));
        var studentAttendance = attendanceBuilder.Build(Attendance.Present);
        var attendanceKey = new StudentsLookupKey([group], GroupPartitionKey.All, course, LessonType.Lab);
        var lessonAttendance = studentAttendance.Get(new AttendanceLookupKey(
            new FoundGroups { IsWildcard = true, Value = [group] }, GroupPartitionKey.All,
            course, LessonType.Lab, dayIndex: 0, dateTime: new(2026, 10, 12)));
        Assert.Equal(2, lessonAttendance.StudentCount);
        Assert.Equal(Attendance.NotPresent, lessonAttendance.Student(0));
        Assert.Equal(Attendance.MotivatedAbsent, lessonAttendance.Student(1));

        const string html = """
            <form method="post">
              <input id="LessonDate" name="LessonDate" type="datetime-local" value="2026-10-12T00:00" />
              <select id="LessonMode" name="LessonMode"><option value="laborator">Lab</option></select>
              <table>
                <tr><th>n</th><th>name</th><th>frecvența/nota</th></tr>
                <tr><td>1</td><td>Synthetic Alpha</td><td><input name="StudentList[1001].NotaStr" type="text" value="" /></td></tr>
                <tr><td>2</td><td>Synthetic Beta</td><td><input name="StudentList[1002].NotaStr" type="text" value="" /></td></tr>
              </table>
            </form>
            """;
        var document = await BrowsingContext.New(Configuration.Default)
            .OpenAsync(request => request.Content(html).Address("https://registry.test/add"));
        var htmlStudents = HtmlSearch.FindStudents(HtmlSearch.FindAttendanceTable(document));
        var dbNames = studentAttendance.StudentNames(attendanceKey);
        var remapper = StudentNameRemapHelper.Create(htmlStudents, dbNames, []);
        var attendanceForHtml = remapper.RemapToHtml(lessonAttendance.AsArray());
        AddLessonsToOnlineRegistryTaskHandler.UpdateAttendanceForRegistry(attendanceForHtml, htmlStudents);

        var lessonId = schedule.EnumerateWeeklyLessons().Single().Id.AsAny();
        HtmlSearch.UpdateForm(new()
        {
            Schedule = schedule,
            Document = document,
            ExpectedStudents = htmlStudents,
            Lesson = new()
            {
                Attendance = attendanceForHtml,
                Topic = null,
                DateTime = new(2026, 10, 12),
                LessonId = lessonId,
                RegistryLessonType = LessonType.Lab,
            },
        });

        var submission = HtmlSearch.GetLessonForm(document).GetSubmission()!;
        using var reader = new StreamReader(submission.Body);
        var fields = (await reader.ReadToEndAsync())
            .Split('&')
            .Select(field => field.Split('=', 2))
            .ToDictionary(
                field => Uri.UnescapeDataString(field[0]),
                field => Uri.UnescapeDataString(field[1].Replace('+', ' ')));
        Assert.Equal("a", fields["StudentList[1001].NotaStr"]);
        Assert.Equal("am", fields["StudentList[1002].NotaStr"]);
    }

    // TODO: Add test for header format
}
