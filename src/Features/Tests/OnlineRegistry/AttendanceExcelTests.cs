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

    private static (ScheduleBuilder Builder, FilteredSchedule Filtered, CourseId Course, GroupId Group) CreateDateBindingFixture()
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
        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        unifier.Refresh(builder);
        return (builder, schedule.Filter(new()), course, group);
    }

    private static AttendanceListsExcelParser CreateParser(ScheduleBuilder builder)
    {
        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        unifier.Refresh(builder);
        return new AttendanceListsExcelParser(builder.GroupParseContext!,
            builder.Lookup(unifier), LessonTypeParser.Instance,
            new SubGroupNameRemapper(builder), SpecializationRegistry.Empty);
    }

    private static AttendanceLookupKey DateKey(CourseId course, GroupId group, DateTime dateTime, int dayIndex = 0) => new(
        new FoundGroups { IsWildcard = true, Value = [group] }, GroupPartitionKey.All,
        course, LessonType.Lab, dayIndex: dayIndex, dateTime: dateTime);

    [Fact]
    public void DateValidationBindsByDateRatherThanColumnOrder()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 2).Value = "09.09";
        sheet.Cell(1, 3).Value = "02.09";
        sheet.Cell(2, 1).Value = "Synthetic Student";
        sheet.Cell(2, 2).Value = "a";
        sheet.Cell(2, 3).Value = "na";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        // Columns are deliberately out of order: binding follows the header dates.
        Assert.Equal(Attendance.NotPresent, list.Get(DateKey(course, group, new(2026, 9, 9))).Student(0));
        Assert.Equal(Attendance.NotApplicable, list.Get(DateKey(course, group, new(2026, 9, 2))).Student(0));
        // A scheduled date with no attendance column leaves attendance unchanged.
        Assert.Equal(0, list.Get(DateKey(course, group, new(2026, 9, 16))).StudentCount);
    }

    [Fact]
    public void DateValidationMatchesFullDatesExactly()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 2).Value = "02.09.2026";
        sheet.Cell(2, 1).Value = "Synthetic Student";
        sheet.Cell(2, 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        Assert.Equal(Attendance.NotPresent, list.Get(DateKey(course, group, new(2026, 9, 2))).Student(0));
        Assert.Equal(0, list.Get(DateKey(course, group, new(2025, 9, 2))).StudentCount);
    }

    [Fact]
    public void DateValidationRequiresADateInEveryColumn()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 2).Value = "02.09";
        sheet.Cell(1, 3).Value = "not-a-date";
        sheet.Cell(2, 1).Value = "Synthetic Student";
        sheet.Cell(2, 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        var error = Assert.ThrowsAny<Exception>(() =>
            CreateParser(builder).Parse(new(
                new() { DateMode = AttendanceDateMode.ValidateAndBind },
                attendance, filtered, workbook)));
        Assert.Contains("Synthetic course IA2601", error.Message);
    }

    [Fact]
    public void DateValidationRejectsNonDateHeaders()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 2).Value = "lab";
        sheet.Cell(2, 1).Value = "Synthetic Student";
        sheet.Cell(2, 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        Assert.ThrowsAny<Exception>(() =>
            CreateParser(builder).Parse(new(
                new() { DateMode = AttendanceDateMode.ValidateAndBind },
                attendance, filtered, workbook)));
    }

    [Fact]
    public void ExplicitDateHeaderFormatIsHonored()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 2).Value = "02.09";
        sheet.Cell(2, 1).Value = "Synthetic Student";
        sheet.Cell(2, 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { HeaderFormat = new() { FormatType = HeaderFormatType.Date } },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        // Default date mode keeps order-based binding for compatibility.
        Assert.Equal(Attendance.NotPresent, list.Get(DateKey(course, group, new(2026, 10, 12))).Student(0));
    }
    [Fact]
    public void NumberedWorkbookLayoutImportsEverySheetAndMixedDateHeaders()
    {
        using var workbook = new XLWorkbook("data/numbered-attendance.xlsx");
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2026);
        builder.EnableLookupModule();
        var group = builder.Group("IA2601");
        var aliases = new Dictionary<string, string>();
        foreach (var sheet in workbook.Worksheets)
        {
            var courseName = $"Synthetic course {sheet.Position}";
            var course = builder.Course(courseName);
            builder.RegularLesson(lesson =>
            {
                lesson.Group(group);
                lesson.Course(course);
                lesson.DayOfWeek(DayOfWeek.Monday);
                lesson.TimeSlot(TimeSlot.First);
                lesson.Type(LessonType.Lab);
            });
            aliases.Add(sheet.Name, $"{courseName} IA2601 (lab)");
        }
        builder.RefreshLookup();
        var filtered = builder.Build().Filter(new());
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new()
            {
                WorksheetNames = aliases,
                HeaderFormat = new() { FormatType = HeaderFormatType.Date },
                DateMode = AttendanceDateMode.ValidateAndBind,
            }, attendance, filtered, workbook));
        var lists = attendance.Build(Attendance.Present).ToArray();
        // Two sheets include both lecture and lab columns; all 13 sheets import.
        Assert.Equal(15, lists.Length);
        Assert.All(lists, list => Assert.True(list.Value.StudentNames.Count > 0));
        Assert.All(lists, list => Assert.Equal(list.Value.Attendance.DayCount, list.Value.Dates!.Value.Length));
        Assert.Equal(2, lists.Count(list => list.Key.LessonType == LessonType.Curs));
        Assert.Equal(13, lists.Count(list => list.Key.LessonType == LessonType.Lab));
        Assert.Contains(lists, list => list.Value.Attendance.Values.Any(day => day.AsArray().Contains(Attendance.NotPresent)));
    }

    [Fact]
    public void NumberedColumnsBindNativeDatesAndSeparateLectureFromLab()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("group shorthand");
        sheet.Cell(1, 1).Value = "Nr.";
        sheet.Cell(1, 2).Value = "Nume, Prenume";
        sheet.Cell(1, 3).Value = new DateTime(2026, 9, 9);
        sheet.Cell(1, 4).Value = "02.09 curs";
        sheet.Cell(1, 5).Value = "curs 09.09";
        sheet.Cell(2, 1).Value = 1;
        sheet.Cell(2, 2).Value = "Synthetic Student";
        sheet.Cell(2, 3).Value = "a";
        sheet.Cell(2, 4).Value = "am";
        sheet.Cell(2, 5).Value = "na";
        sheet.Cell(2, 6).Value = "ignored outside header";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new()
            {
                StudentNameColumn = 2,
                WorksheetNames = new Dictionary<string, string> { [sheet.Name] = "Synthetic course IA2601 (lab)" },
                DateMode = AttendanceDateMode.ValidateAndBind,
            }, attendance, filtered, workbook));
        var lists = attendance.Build(Attendance.Present);
        Assert.Equal(Attendance.NotPresent, lists.Get(DateKey(course, group, new(2026, 9, 9))).Student(0));
        var lecture = new AttendanceLookupKey(
            new FoundGroups { IsWildcard = true, Value = [group] }, GroupPartitionKey.All,
            course, LessonType.Curs, dayIndex: 0, dateTime: new(2026, 9, 2));
        Assert.Equal(Attendance.MotivatedAbsent, lists.Get(lecture).Student(0));
        var laterLecture = new AttendanceLookupKey(
            new FoundGroups { IsWildcard = true, Value = [group] }, GroupPartitionKey.All,
            course, LessonType.Curs, dayIndex: 0, dateTime: new(2026, 9, 9));
        Assert.Equal(Attendance.NotApplicable, lists.Get(laterLecture).Student(0));
        Assert.Equal(0, lists.Get(DateKey(course, group, new(2025, 9, 9))).StudentCount);
    }

    [Fact]
    public void DuplicateDatesLeaveAttendanceUnchanged()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 2).Value = "02.09";
        sheet.Cell(1, 3).Value = "02.09";
        sheet.Cell(2, 1).Value = "Synthetic Student";
        sheet.Cell(2, 2).Value = "a";
        sheet.Cell(2, 3).Value = "am";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind }, attendance, filtered, workbook));
        Assert.Equal(0, attendance.Build(Attendance.Present).Get(DateKey(course, group, new(2026, 9, 2))).StudentCount);
    }

    [Theory]
    [InlineData(1, false, false, true)]
    [InlineData(3, true, false, true)]
    [InlineData(4, false, true, true)]
    [InlineData(3, false, false, false)]
    [InlineData(2, true, false, false)]
    public void AutomaticNameColumnSkipsMetadataAndEmptyColumns(
        int nameColumn, bool numbering, bool strayTextBeforeNameHeader, bool hasHeader)
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Column(1).Width = 20; // Formatting does not make an empty column populated.
        int firstStudentRow = hasHeader ? 2 : 1;
        if (hasHeader)
        {
            if (strayTextBeforeNameHeader) sheet.Cell(1, 1).Value = "Other metadata";
            if (numbering) sheet.Cell(1, 1).Value = "Nr.";
            sheet.Cell(1, nameColumn).Value = "Nume, Prenume";
            sheet.Cell(1, nameColumn + 2).Value = "02.09";
            sheet.Cell(1, nameColumn + 4).Value = "09.09";
        }
        else if (numbering)
        {
            sheet.Cell(1, 1).Value = "Nr.";
        }
        if (numbering) sheet.Cell(firstStudentRow + 1, 1).Value = 2;
        sheet.Cell(firstStudentRow, nameColumn).Value = "Synthetic Alpha";
        sheet.Cell(firstStudentRow + 1, nameColumn).Value = "Synthetic Beta";
        sheet.Cell(firstStudentRow, nameColumn + (hasHeader ? 2 : 1)).Value = "a";
        sheet.Cell(firstStudentRow + 1, nameColumn + (hasHeader ? 4 : 2)).Value = "am";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(new(), attendance, filtered, workbook));
        var list = Assert.Single(attendance.Build(Attendance.Present));
        Assert.Equal(2, list.Value.StudentNames.Count);
        Assert.Equal(2, list.Value.Attendance.DayCount);
        Assert.Equal(Attendance.NotPresent, list.Value.Attendance.Day(0).Student(0));
        Assert.Equal(Attendance.Present, list.Value.Attendance.Day(0).Student(1));
        Assert.Equal(Attendance.MotivatedAbsent, list.Value.Attendance.Day(1).Student(1));
    }

    [Fact]
    public void ExplicitNameColumnOverridesHeaderDetection()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 1).Value = "Name";
        sheet.Cell(1, 3).Value = "Nume";
        sheet.Cell(1, 4).Value = "lab";
        sheet.Cell(2, 1).Value = "Ignored Student";
        sheet.Cell(2, 3).Value = "Synthetic Student";
        sheet.Cell(2, 4).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { StudentNameColumn = 3 }, attendance, filtered, workbook));
        Assert.Single(Assert.Single(attendance.Build(Attendance.Present)).Value.StudentNames);
    }

    [Fact]
    public void IgnoredHeaderStillDetectsAndSkipsNameAndNumberHeaders()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(1, 1).Value = "Nr.";
        sheet.Cell(1, 2).Value = "Name";
        sheet.Cell(1, 3).Value = "arbitrary header";
        sheet.Cell(2, 1).Value = 1;
        sheet.Cell(2, 2).Value = "Synthetic Student";
        sheet.Cell(2, 3).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { HeaderFormat = new() { FormatType = HeaderFormatType.IgnoreHeader } },
            attendance, filtered, workbook));
        Assert.Single(Assert.Single(attendance.Build(Attendance.Present)).Value.StudentNames);
    }

}
