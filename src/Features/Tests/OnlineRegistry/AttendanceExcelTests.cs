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
        Assert.Equal(expected: days, actual: result.Attendance.DayCount);
        Assert.Equal(expected: 0, actual: result.Attendance.StudentCount);
        Assert.All(result.Attendance.Values, day => Assert.Empty(day.AsArray()));
        Assert.Equal(expected: days, actual: result.LessonTypes!.Value.Length);
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
        sheet.Cell(row: blankRows + 1, column: 1).Value = nameHeader;
        sheet.Cell(row: blankRows + 1, column: 2).Value = headerFormat switch
        {
            "text-date" => "02.09",
            "native-date" => new DateTime(year: 2026, month: 9, day: 2),
            _ => "lab",
        };
        sheet.Cell(row: blankRows + 1, column: 3).Value = headerFormat switch
        {
            "text-date" => "09.09",
            "native-date" => new DateTime(year: 2026, month: 9, day: 9),
            _ => "lab",
        };
        sheet.Cell(row: blankRows + 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: blankRows + 2, column: 2).Value = "a";
        sheet.Cell(row: blankRows + 2, column: 3).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        unifier.Refresh(builder);
        var parser = new AttendanceListsExcelParser(builder.GroupParseContext!,
            builder.Lookup(unifier), LessonTypeParser.Instance,
            new SubGroupNameRemapper(builder), SpecializationRegistry.Empty);
        parser.Parse(new(new(), attendance, filtered, workbook));
        var list = Assert.Single(attendance.Build(Attendance.Present));
        Assert.Single(list.Value.StudentNames);
        Assert.Equal(expected: 2, actual: list.Value.Attendance.Values.Count());
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
        sheet.Cell(row: 1, column: 2).Value = dateHeader ? "02.09" : "lab";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Alpha";
        sheet.Cell(row: 2, column: 2).Value = "a";
        sheet.Cell(row: 3, column: 1).Value = "Synthetic Beta";
        sheet.Cell(row: 3, column: 2).Value = "am";

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
            new FoundGroups {
                IsWildcard = true,
                Value = [group]
            }, GroupPartitionKey.All,
            course, LessonType.Lab, dayIndex: 0, dateTime: new(year: 2026, month: 10, day: 12)));
        Assert.Equal(expected: 2, actual: lessonAttendance.StudentCount);
        Assert.Equal(expected: Attendance.NotPresent, actual: lessonAttendance.Student(0));
        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: lessonAttendance.Student(1));

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
                DateTime = new(year: 2026, month: 10, day: 12),
                LessonId = lessonId,
                RegistryLessonType = LessonType.Lab,
            },
        });

        var submission = HtmlSearch.GetLessonForm(document).GetSubmission()!;
        using var reader = new StreamReader(submission.Body);
        string ProjectValue(string[] @field)
        {
            string operationValue = field[1].Replace('+', ' ');
            string operationValue2 = operationValue;
            string operationValue3 = Uri.UnescapeDataString(operationValue2);
            return operationValue3;
        }

        var fields = (await reader.ReadToEndAsync())
            .Split('&')
            .Select(field => field.Split('=', 2))
            .ToDictionary(
                keySelector: field => Uri.UnescapeDataString(field[0]),
                elementSelector: ProjectValue);
        Assert.Equal(expected: "a", actual: fields["StudentList[1001].NotaStr"]);
        Assert.Equal(expected: "am", actual: fields["StudentList[1002].NotaStr"]);
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

    private static AttendanceLookupKey DateKey(CourseId course, GroupId group, DateTime dateTime, int dayIndex = 0)
    {
        return new(
            new FoundGroups {
                IsWildcard = true,
                Value = [group]
            }, GroupPartitionKey.All,
            course, LessonType.Lab, dayIndex: dayIndex, dateTime: dateTime);
    }

    [Fact]
    public void DateValidationBindsByDateRatherThanColumnOrder()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(row: 1, column: 2).Value = "09.09";
        sheet.Cell(row: 1, column: 3).Value = "02.09";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "a";
        sheet.Cell(row: 2, column: 3).Value = "na";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        // Columns are deliberately out of order: binding follows the header dates.
        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(DateKey(course, group, new(year: 2026, month: 9, day: 9))).Student(0));
        Assert.Equal(expected: Attendance.NotApplicable, actual: list.Get(DateKey(course, group, new(year: 2026, month: 9, day: 2))).Student(0));
        // A scheduled date with no attendance column leaves attendance unchanged.
        Assert.Equal(expected: 0, actual: list.Get(DateKey(course, group, new(year: 2026, month: 9, day: 16))).StudentCount);
    }

    [Fact]
    public void DateValidationMatchesFullDatesExactly()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(row: 1, column: 2).Value = "02.09.2026";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(DateKey(course, group, new(year: 2026, month: 9, day: 2))).Student(0));
        Assert.Equal(expected: 0, actual: list.Get(DateKey(course, group, new(year: 2025, month: 9, day: 2))).StudentCount);
    }

    [Fact]
    public void DateValidationRequiresADateInEveryColumn()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(row: 1, column: 2).Value = "02.09";
        sheet.Cell(row: 1, column: 3).Value = "not-a-date";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        var error = Assert.ThrowsAny<Exception>(() =>
            CreateParser(builder).Parse(new(
                new() { DateMode = AttendanceDateMode.ValidateAndBind },
                attendance, filtered, workbook)));
        Assert.Contains(expectedSubstring: "Synthetic course IA2601", actualString: error.Message);
    }

    [Fact]
    public void DateValidationRejectsNonDateHeaders()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(row: 1, column: 2).Value = "lab";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "a";
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
        sheet.Cell(row: 1, column: 2).Value = "02.09";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { HeaderFormat = new() { FormatType = HeaderFormatType.Date } },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        // Default date mode keeps order-based binding for compatibility.
        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(DateKey(course, group, new(year: 2026, month: 10, day: 12))).Student(0));
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
            aliases.Add(key: sheet.Name, value: $"{courseName} IA2601 (lab)");
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
        Assert.Equal(expected: 15, actual: lists.Length);
        Assert.All(lists, list => Assert.True(list.Value.StudentNames.Count > 0));
        Assert.All(lists, list => Assert.Equal(expected: list.Value.Attendance.DayCount, actual: list.Value.Dates!.Value.Length));
        Assert.Equal(expected: 2, actual: lists.Count(list => list.Key.LessonType == LessonType.Curs));
        Assert.Equal(expected: 13, actual: lists.Count(list => list.Key.LessonType == LessonType.Lab));
        Assert.Contains(lists, list => list.Value.Attendance.Values.Any(day => day.AsArray().Contains(Attendance.NotPresent)));
    }

    [Fact]
    public void NumberedColumnsBindNativeDatesAndSeparateLectureFromLab()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("group shorthand");
        sheet.Cell(row: 1, column: 1).Value = "Nr.";
        sheet.Cell(row: 1, column: 2).Value = "Nume, Prenume";
        sheet.Cell(row: 1, column: 3).Value = new DateTime(year: 2026, month: 9, day: 9);
        sheet.Cell(row: 1, column: 4).Value = "02.09 curs";
        sheet.Cell(row: 1, column: 5).Value = "curs 09.09";
        sheet.Cell(row: 2, column: 1).Value = 1;
        sheet.Cell(row: 2, column: 2).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 3).Value = "a";
        sheet.Cell(row: 2, column: 4).Value = "am";
        sheet.Cell(row: 2, column: 5).Value = "na";
        sheet.Cell(row: 2, column: 6).Value = "ignored outside header";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new()
            {
                StudentNameColumn = 2,
                WorksheetNames = new Dictionary<string, string> { [sheet.Name] = "Synthetic course IA2601 (lab)" },
                DateMode = AttendanceDateMode.ValidateAndBind,
            }, attendance, filtered, workbook));
        var lists = attendance.Build(Attendance.Present);
        Assert.Equal(expected: Attendance.NotPresent, actual: lists.Get(DateKey(course, group, new(year: 2026, month: 9, day: 9))).Student(0));
        var lecture = new AttendanceLookupKey(
            new FoundGroups {
                IsWildcard = true,
                Value = [group]
            }, GroupPartitionKey.All,
            course, LessonType.Curs, dayIndex: 0, dateTime: new(year: 2026, month: 9, day: 2));
        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: lists.Get(lecture).Student(0));
        var laterLecture = new AttendanceLookupKey(
            new FoundGroups {
                IsWildcard = true,
                Value = [group]
            }, GroupPartitionKey.All,
            course, LessonType.Curs, dayIndex: 0, dateTime: new(year: 2026, month: 9, day: 9));
        Assert.Equal(expected: Attendance.NotApplicable, actual: lists.Get(laterLecture).Student(0));
        Assert.Equal(expected: 0, actual: lists.Get(DateKey(course, group, new(year: 2025, month: 9, day: 9))).StudentCount);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("full-year")]
    [InlineData("native")]
    [InlineData("typed")]
    public void DateValidationDistinguishesTwoLabsOnTheSameDayByTime(string format)
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        if (format == "native")
        {
            sheet.Cell(row: 1, column: 2).Value = new DateTime(year: 2026, month: 9, day: 2, hour: 11, minute: 30, second: 0);
            sheet.Cell(row: 1, column: 3).Value = new DateTime(year: 2026, month: 9, day: 2, hour: 9, minute: 45, second: 0);
        }
        else
        {
            var date = format == "full-year" ? "02.09.2026" : "02.09";
            sheet.Cell(row: 1, column: 2).Value = (string) $"{($"{date} 11:30")}{(format == "typed" ? " lab" : "")}";
            sheet.Cell(row: 1, column: 3).Value = (string) $"{(format == "typed" ? "lab " : "")}{($"{date} 09:45")}";
        }
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "am";
        sheet.Cell(row: 2, column: 3).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(new() { DateMode = AttendanceDateMode.ValidateAndBind }, attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(DateKey(course, group, new(year: 2026, month: 9, day: 2, hour: 9, minute: 45, second: 0))).Student(0));
        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: list.Get(DateKey(course, group, new(year: 2026, month: 9, day: 2, hour: 11, minute: 30, second: 0))).Student(0));
        Assert.Equal(expected: 0, actual: list.Get(DateKey(course, group, new(year: 2026, month: 9, day: 2, hour: 13, minute: 15, second: 0))).StudentCount);
    }

    [Fact]
    public void DuplicateDatesLeaveAttendanceUnchanged()
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(row: 1, column: 2).Value = "02.09";
        sheet.Cell(row: 1, column: 3).Value = "02.09";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "a";
        sheet.Cell(row: 2, column: 3).Value = "am";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind }, attendance, filtered, workbook));
        Assert.Equal(expected: 0, actual: attendance.Build(Attendance.Present).Get(DateKey(course, group, new(year: 2026, month: 9, day: 2))).StudentCount);
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
            if (strayTextBeforeNameHeader) sheet.Cell(row: 1, column: 1).Value = "Other metadata";
            if (numbering) sheet.Cell(row: 1, column: 1).Value = "Nr.";
            sheet.Cell(row: 1, column: nameColumn).Value = "Nume, Prenume";
            sheet.Cell(row: 1, column: nameColumn + 2).Value = "02.09";
            sheet.Cell(row: 1, column: nameColumn + 4).Value = "09.09";
        }
        else if (numbering)
        {
            sheet.Cell(row: 1, column: 1).Value = "Nr.";
        }
        if (numbering) sheet.Cell(row: firstStudentRow + 1, column: 1).Value = 2;
        sheet.Cell(row: firstStudentRow, column: nameColumn).Value = "Synthetic Alpha";
        sheet.Cell(row: firstStudentRow + 1, column: nameColumn).Value = "Synthetic Beta";
        sheet.Cell(row: firstStudentRow, column: nameColumn + (hasHeader ? 2 : 1)).Value = "a";
        sheet.Cell(row: firstStudentRow + 1, column: nameColumn + (hasHeader ? 4 : 2)).Value = "am";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(new(), attendance, filtered, workbook));
        var list = Assert.Single(attendance.Build(Attendance.Present));
        Assert.Equal(expected: 2, actual: list.Value.StudentNames.Count);
        Assert.Equal(expected: 2, actual: list.Value.Attendance.DayCount);
        Assert.Equal(expected: Attendance.NotPresent, actual: list.Value.Attendance.Day(0).Student(0));
        Assert.Equal(expected: Attendance.Present, actual: list.Value.Attendance.Day(0).Student(1));
        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: list.Value.Attendance.Day(1).Student(1));
    }

    [Fact]
    public void ExplicitNameColumnOverridesHeaderDetection()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601");
        sheet.Cell(row: 1, column: 1).Value = "Name";
        sheet.Cell(row: 1, column: 3).Value = "Nume";
        sheet.Cell(row: 1, column: 4).Value = "lab";
        sheet.Cell(row: 2, column: 1).Value = "Ignored Student";
        sheet.Cell(row: 2, column: 3).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 4).Value = "a";
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
        sheet.Cell(row: 1, column: 1).Value = "Nr.";
        sheet.Cell(row: 1, column: 2).Value = "Name";
        sheet.Cell(row: 1, column: 3).Value = "arbitrary header";
        sheet.Cell(row: 2, column: 1).Value = 1;
        sheet.Cell(row: 2, column: 2).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 3).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new() { HeaderFormat = new() { FormatType = HeaderFormatType.IgnoreHeader } },
            attendance, filtered, workbook));
        Assert.Single(Assert.Single(attendance.Build(Attendance.Present)).Value.StudentNames);
    }

    [Theory]
    [InlineData(HeaderFormatType.Auto)]
    [InlineData(HeaderFormatType.Date)]
    [InlineData(HeaderFormatType.LessonType)]
    [InlineData(HeaderFormatType.IgnoreHeader)]
    [InlineData(HeaderFormatType.Nothing)]
    public void EmptyWorksheetsAreSkippedBeforeHeaderValidationAndLessonLookup(HeaderFormatType format)
    {
        var (builder, filtered, course, group) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Unmatched empty sheet");
        var formatted = workbook.AddWorksheet("Unmatched formatted sheet");
        formatted.Range("A1:Z100").Style.Fill.BackgroundColor = XLColor.Yellow;
        formatted.Column(1).Width = 30;
        var whitespace = workbook.AddWorksheet("Unmatched whitespace sheet");
        whitespace.Cell(row: 1, column: 1).Value = "   ";
        whitespace.Cell(row: 2, column: 3).Value = "\t";
        var emptyTable = workbook.AddWorksheet("Unmatched empty table");
        emptyTable.Range("A1:C3").CreateTable();
        var valid = workbook.AddWorksheet("Synthetic course IA2601");
        int studentRow = format == HeaderFormatType.Nothing ? 1 : 2;
        if (studentRow == 2)
        {
            valid.Cell(row: 1, column: 1).Value = "Name";
            valid.Cell(row: 1, column: 2).Value = format switch
            {
                HeaderFormatType.LessonType => "lab",
                HeaderFormatType.IgnoreHeader => "ignored title",
                _ => "02.09",
            };
        }
        valid.Cell(row: studentRow, column: 1).Value = "Synthetic Student";
        valid.Cell(row: studentRow, column: 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new()
            {
                HeaderFormat = new() { FormatType = format },
                DateMode = format is HeaderFormatType.Auto or HeaderFormatType.Date
                    ? AttendanceDateMode.ValidateAndBind : AttendanceDateMode.Ignore,
            }, attendance, filtered, workbook));
        var lists = attendance.Build(Attendance.Present);
        Assert.Single(lists);
        Assert.Equal(expected: Attendance.NotPresent, actual: lists.Get(DateKey(course, group, new(year: 2026, month: 9, day: 2))).Student(0));
    }

    [Fact]
    public void EntirelyEmptyWorkbookProducesNoAttendanceListsEvenWithStrictDateHeaders()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Unmatched empty sheet");
        workbook.AddWorksheet("Unmatched formatted sheet").Range("A1:D50").Style.Font.Bold = true;
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder).Parse(new(
            new()
            {
                HeaderFormat = new() { FormatType = HeaderFormatType.Date },
                DateMode = AttendanceDateMode.ValidateAndBind,
            }, attendance, filtered, workbook));
        Assert.Empty(attendance.Build(Attendance.Present));
    }

}
