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
        builder.AddLessonTypes(Enumerable.Repeat(LessonType.Curs, days)
            .ToArray());
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
        global::ScheduleLib.Schedule schedule2 = schedule;
        global::ScheduleLib.Builders.LookupFacade lookup = builder.Lookup(unifier);
        global::ScheduleLib.Builders.TeacherBuilderModel.NameModel name2 = name;
        var filteredSchedule = ScheduleTestHelper.FilterForTeacher(
            schedule2, lookup, name2);

        using var workbook = new XLWorkbook(ExcelFilePath);
        var attendanceBuilder = new AllStudentAttendanceListBuilder();
        global::ScheduleLib.Parsing.GroupParser.GroupParseContext groupParser = builder.GroupParseContext!;
        global::ScheduleLib.Builders.LookupFacade lookup2 = builder.Lookup(unifier);
        global::ScheduleLib.Parsing.Lesson.LessonTypeParser lessonTypeParser = LessonTypeParser.Instance;
        global::ScheduleLib.Builders.SubGroupNameRemapper subGroupRemapper = new SubGroupNameRemapper(builder);
        global::ScheduleLib.SpecializationRegistry specializationRegistry = SpecializationRegistry.Empty;
        var parser = new AttendanceListsExcelParser(
            groupParser, lookup2, lessonTypeParser, subGroupRemapper, specializationRegistry);
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
        if (blankRows > 0)
        {
            sheet.Row(1).Height = 20;
        }

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
        global::ScheduleLib.Parsing.GroupParser.GroupParseContext groupParser = builder.GroupParseContext!;
        global::ScheduleLib.Builders.LookupFacade lookup = builder.Lookup(unifier);
        global::ScheduleLib.Parsing.Lesson.LessonTypeParser lessonTypeParser = LessonTypeParser.Instance;
        global::ScheduleLib.Builders.SubGroupNameRemapper subGroupRemapper = new SubGroupNameRemapper(builder);
        global::ScheduleLib.SpecializationRegistry specializationRegistry = SpecializationRegistry.Empty;
        var parser = new AttendanceListsExcelParser(groupParser, lookup, lessonTypeParser, subGroupRemapper, specializationRegistry);
        global::OnlineRegistry.AttendanceExcel.AttendanceListsExcelParser attendanceListsExcelParser = parser;
        global::OnlineRegistry.AttendanceExcel.ParseAttendanceListsExcelParams p = new(new(), attendance, filtered, workbook);
        attendanceListsExcelParser.Parse(p);
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList collection = attendance.Build(Attendance.Present);
        global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<global::ScheduleLib.OnlineRegistry.StudentsLookupKey, global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances>> collection2 = collection;
        var list = Assert.Single(collection2);
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
        global::ScheduleLib.Parsing.GroupParser.GroupParseContext groupParser = builder.GroupParseContext!;
        global::ScheduleLib.Builders.LookupFacade lookup = builder.Lookup(unifier);
        global::ScheduleLib.Parsing.Lesson.LessonTypeParser lessonTypeParser = LessonTypeParser.Instance;
        global::ScheduleLib.Builders.SubGroupNameRemapper subGroupRemapper = new SubGroupNameRemapper(builder);
        global::ScheduleLib.SpecializationRegistry specializationRegistry = SpecializationRegistry.Empty;
        var parser = new AttendanceListsExcelParser(groupParser, lookup, lessonTypeParser, subGroupRemapper, specializationRegistry);
        parser.Parse(new(new(), attendanceBuilder, schedule.Filter(new()), workbook));
        var studentAttendance = attendanceBuilder.Build(Attendance.Present);
        var attendanceKey = new StudentsLookupKey([group], GroupPartitionKey.All, course, LessonType.Lab);
        global::System.DateTime CalculateDateTime()
        {
            global::System.DateTime dateTime2 = new(year: 2026, month: 10, day: 12);
            return dateTime2;
        }

        var lessonAttendance = studentAttendance.Get(new AttendanceLookupKey(
                    new FoundGroups
                    {
                        IsWildcard = true,
                        Value = [group]
                    }, GroupPartitionKey.All,
                    course, LessonType.Lab, dayIndex: 0, dateTime: CalculateDateTime()));
        Assert.Equal(expected: 2, actual: lessonAttendance.StudentCount);
        global::ScheduleLib.OnlineRegistry.Attendance expected2 = Attendance.NotPresent;
        global::ScheduleLib.OnlineRegistry.Attendance actual2 = lessonAttendance.Student(0);
        Assert.Equal(expected: expected2, actual: actual2);
        global::ScheduleLib.OnlineRegistry.Attendance expected3 = Attendance.MotivatedAbsent;
        global::ScheduleLib.OnlineRegistry.Attendance actual3 = lessonAttendance.Student(1);
        Assert.Equal(expected: expected3, actual: actual3);

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
            .OpenAsync(request => request.Content(html)
                .Address("https://registry.test/add"));
        global::AngleSharp.Html.Dom.IHtmlTableElement table = HtmlSearch.FindAttendanceTable(document);
        var htmlStudents = HtmlSearch.FindStudents(table);
        var dbNames = studentAttendance.StudentNames(attendanceKey);
        var remapper = StudentNameRemapHelper.Create(htmlStudents, dbNames, []);
        var attendanceForHtml = remapper.RemapToHtml(lessonAttendance.AsArray());
        AddLessonsToOnlineRegistryTaskHandler.UpdateAttendanceForRegistry(attendanceForHtml, htmlStudents);

        var lessonId = schedule.EnumerateWeeklyLessons()
            .Single().Id
            .AsAny();
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

        var submission = HtmlSearch.GetLessonForm(document)
            .GetSubmission()!;
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
        global::ScheduleLib.Parsing.GroupParser.GroupParseContext groupParser = builder.GroupParseContext!;
        global::ScheduleLib.Builders.LookupFacade lookup = builder.Lookup(unifier);
        global::ScheduleLib.Parsing.Lesson.LessonTypeParser lessonTypeParser = LessonTypeParser.Instance;
        global::ScheduleLib.Builders.SubGroupNameRemapper subGroupRemapper = new SubGroupNameRemapper(builder);
        global::ScheduleLib.SpecializationRegistry specializationRegistry = SpecializationRegistry.Empty;
        global::OnlineRegistry.AttendanceExcel.AttendanceListsExcelParser attendanceListsExcelParser = new AttendanceListsExcelParser(groupParser, lookup, lessonTypeParser, subGroupRemapper, specializationRegistry);
        return attendanceListsExcelParser;
    }

    private static AttendanceLookupKey DateKey(CourseId course, GroupId group, DateTime dateTime, int dayIndex = 0)
    {
        return new(
            new FoundGroups
            {
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
        CreateParser(builder)
            .Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        // Columns are deliberately out of order: binding follows the header dates.
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
        {
            global::ScheduleLib.CourseId course2 = course;
            global::ScheduleLib.GroupId group2 = group;
            global::System.DateTime dateTime = new(year: 2026, month: 9, day: 9);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = DateKey(course2, group2, dateTime);
            return key;
        }

        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(CalculateKey())
            .Student(0));
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey2()
        {
            global::ScheduleLib.CourseId course3 = course;
            global::ScheduleLib.GroupId group3 = group;
            global::System.DateTime dateTime2 = new(year: 2026, month: 9, day: 2);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key2 = DateKey(course3, group3, dateTime2);
            return key2;
        }

        Assert.Equal(expected: Attendance.NotApplicable, actual: list.Get(CalculateKey2())
            .Student(0));
        // A scheduled date with no attendance column leaves attendance unchanged.
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey3()
        {
            global::ScheduleLib.CourseId course4 = course;
            global::ScheduleLib.GroupId group4 = group;
            global::System.DateTime dateTime3 = new(year: 2026, month: 9, day: 16);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key3 = DateKey(course4, group4, dateTime3);
            return key3;
        }

        Assert.Equal(expected: 0, actual: list.Get(CalculateKey3()).StudentCount);
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
        CreateParser(builder)
            .Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
        {
            global::ScheduleLib.CourseId course2 = course;
            global::ScheduleLib.GroupId group2 = group;
            global::System.DateTime dateTime = new(year: 2026, month: 9, day: 2);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = DateKey(course2, group2, dateTime);
            return key;
        }

        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(CalculateKey())
            .Student(0));
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey2()
        {
            global::ScheduleLib.CourseId course3 = course;
            global::ScheduleLib.GroupId group3 = group;
            global::System.DateTime dateTime2 = new(year: 2025, month: 9, day: 2);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key2 = DateKey(course3, group3, dateTime2);
            return key2;
        }

        Assert.Equal(expected: 0, actual: list.Get(CalculateKey2()).StudentCount);
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
            CreateParser(builder)
                .Parse(new(
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
            CreateParser(builder)
                .Parse(new(
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
        CreateParser(builder)
            .Parse(new(
            new() { HeaderFormat = new() { FormatType = HeaderFormatType.Date } },
            attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        // Default date mode keeps order-based binding for compatibility.
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
        {
            global::ScheduleLib.CourseId course2 = course;
            global::ScheduleLib.GroupId group2 = group;
            global::System.DateTime dateTime = new(year: 2026, month: 10, day: 12);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = DateKey(course2, group2, dateTime);
            return key;
        }

        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(CalculateKey())
            .Student(0));
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
            global::System.Collections.Generic.Dictionary<string, string> dictionary = aliases;
            string key2 = sheet.Name;
            string value2 = $"{courseName} IA2601 (lab)";
            dictionary.Add(key: key2, value: value2);
        }
        builder.RefreshLookup();
        var filtered = builder.Build()
            .Filter(new());
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder)
            .Parse(new(
            new()
            {
                WorksheetNames = aliases,
                HeaderFormat = new() { FormatType = HeaderFormatType.Date },
                DateMode = AttendanceDateMode.ValidateAndBind,
            }, attendance, filtered, workbook));
        var lists = attendance.Build(Attendance.Present)
            .ToArray();
        // Two sheets include both lecture and lab columns; all 13 sheets import.
        Assert.Equal(expected: 15, actual: lists.Length);
        Assert.All(lists, list => Assert.True(list.Value.StudentNames.Count > 0));
        Assert.All(lists, list => Assert.Equal(expected: list.Value.Attendance.DayCount, actual: list.Value.Dates!.Value.Length));
        Assert.Equal(expected: 2, actual: lists.Count(list => list.Key.LessonType == LessonType.Curs));
        Assert.Equal(expected: 13, actual: lists.Count(list => list.Key.LessonType == LessonType.Lab));
        Assert.Contains(lists, list => list.Value.Attendance.Values.Any(day => day.AsArray()
            .Contains(Attendance.NotPresent)));
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
        CreateParser(builder)
            .Parse(new(
            new()
            {
                StudentNameColumn = 2,
                WorksheetNames = new Dictionary<string, string> { [sheet.Name] = "Synthetic course IA2601 (lab)" },
                DateMode = AttendanceDateMode.ValidateAndBind,
            }, attendance, filtered, workbook));
        var lists = attendance.Build(Attendance.Present);
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
        {
            global::ScheduleLib.CourseId course2 = course;
            global::ScheduleLib.GroupId group2 = group;
            global::System.DateTime dateTime2 = new(year: 2026, month: 9, day: 9);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = DateKey(course2, group2, dateTime2);
            return key;
        }

        Assert.Equal(expected: Attendance.NotPresent, actual: lists.Get(CalculateKey())
            .Student(0));
        global::System.DateTime CalculateDateTime()
        {
            global::System.DateTime dateTime3 = new(year: 2026, month: 9, day: 2);
            return dateTime3;
        }

        var lecture = new AttendanceLookupKey(
                    new FoundGroups
                    {
                        IsWildcard = true,
                        Value = [group]
                    }, GroupPartitionKey.All,
                    course, LessonType.Curs, dayIndex: 0, dateTime: CalculateDateTime());
        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: lists.Get(lecture)
            .Student(0));
        global::System.DateTime CalculateDateTime2()
        {
            global::System.DateTime dateTime4 = new(year: 2026, month: 9, day: 9);
            return dateTime4;
        }

        var laterLecture = new AttendanceLookupKey(
                    new FoundGroups
                    {
                        IsWildcard = true,
                        Value = [group]
                    }, GroupPartitionKey.All,
                    course, LessonType.Curs, dayIndex: 0, dateTime: CalculateDateTime2());
        Assert.Equal(expected: Attendance.NotApplicable, actual: lists.Get(laterLecture)
            .Student(0));
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey2()
        {
            global::ScheduleLib.CourseId course3 = course;
            global::ScheduleLib.GroupId group3 = group;
            global::System.DateTime dateTime5 = new(year: 2025, month: 9, day: 9);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key2 = DateKey(course3, group3, dateTime5);
            return key2;
        }

        Assert.Equal(expected: 0, actual: lists.Get(CalculateKey2()).StudentCount);
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
            string CalculateText()
            {
                string text;
                if (format == "typed")
                {
                    text = " lab";
                }
                else
                {
                    text = "";
                }

                return text;
            }

            sheet.Cell(row: 1, column: 2).Value = (string) $"{($"{date} 11:30")}{(CalculateText())}";
            string CalculateText2()
            {
                string text2;
                if (format == "typed")
                {
                    text2 = "lab ";
                }
                else
                {
                    text2 = "";
                }

                return text2;
            }

            sheet.Cell(row: 1, column: 3).Value = (string) $"{(CalculateText2())}{($"{date} 09:45")}";
        }
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Student";
        sheet.Cell(row: 2, column: 2).Value = "am";
        sheet.Cell(row: 2, column: 3).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder)
            .Parse(new(new() { DateMode = AttendanceDateMode.ValidateAndBind }, attendance, filtered, workbook));
        var list = attendance.Build(Attendance.Present);
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
        {
            global::ScheduleLib.CourseId course2 = course;
            global::ScheduleLib.GroupId group2 = group;
            global::System.DateTime dateTime = new(year: 2026, month: 9, day: 2, hour: 9, minute: 45, second: 0);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = DateKey(course2, group2, dateTime);
            return key;
        }

        Assert.Equal(expected: Attendance.NotPresent, actual: list.Get(CalculateKey())
            .Student(0));
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey2()
        {
            global::ScheduleLib.CourseId course3 = course;
            global::ScheduleLib.GroupId group3 = group;
            global::System.DateTime dateTime2 = new(year: 2026, month: 9, day: 2, hour: 11, minute: 30, second: 0);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key2 = DateKey(course3, group3, dateTime2);
            return key2;
        }

        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: list.Get(CalculateKey2())
            .Student(0));
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey3()
        {
            global::ScheduleLib.CourseId course4 = course;
            global::ScheduleLib.GroupId group4 = group;
            global::System.DateTime dateTime3 = new(year: 2026, month: 9, day: 2, hour: 13, minute: 15, second: 0);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key3 = DateKey(course4, group4, dateTime3);
            return key3;
        }

        Assert.Equal(expected: 0, actual: list.Get(CalculateKey3()).StudentCount);
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
        CreateParser(builder)
            .Parse(new(
            new() { DateMode = AttendanceDateMode.ValidateAndBind }, attendance, filtered, workbook));
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
        {
            global::ScheduleLib.CourseId course2 = course;
            global::ScheduleLib.GroupId group2 = group;
            global::System.DateTime dateTime = new(year: 2026, month: 9, day: 2);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = DateKey(course2, group2, dateTime);
            return key;
        }

        Assert.Equal(expected: 0, actual: attendance.Build(Attendance.Present)
            .Get(CalculateKey()).StudentCount);
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
            if (strayTextBeforeNameHeader)
            {
                sheet.Cell(row: 1, column: 1).Value = "Other metadata";
            }

            if (numbering)
            {
                sheet.Cell(row: 1, column: 1).Value = "Nr.";
            }

            sheet.Cell(row: 1, column: nameColumn).Value = "Nume, Prenume";
            sheet.Cell(row: 1, column: nameColumn + 2).Value = "02.09";
            sheet.Cell(row: 1, column: nameColumn + 4).Value = "09.09";
        }
        else if (numbering)
        {
            sheet.Cell(row: 1, column: 1).Value = "Nr.";
        }
        if (numbering)
        {
            sheet.Cell(row: firstStudentRow + 1, column: 1).Value = 2;
        }

        sheet.Cell(row: firstStudentRow, column: nameColumn).Value = "Synthetic Alpha";
        sheet.Cell(row: firstStudentRow + 1, column: nameColumn).Value = "Synthetic Beta";
        global::ClosedXML.Excel.IXLWorksheet iXLWorksheet = sheet;
        int row2 = firstStudentRow;
        int int32 = nameColumn;
        int int322;
        if (hasHeader)
        {
            int322 = 2;
        }
        else
        {
            int322 = 1;
        }

        int column2 = int32 + (int322);
        global::ClosedXML.Excel.IXLCell cell = iXLWorksheet.Cell(row: row2, column: column2);
        cell.Value = "a";
        global::ClosedXML.Excel.IXLWorksheet iXLWorksheet2 = sheet;
        int row3 = firstStudentRow + 1;
        int int323 = nameColumn;
        int int324;
        if (hasHeader)
        {
            int324 = 4;
        }
        else
        {
            int324 = 2;
        }

        int column3 = int323 + (int324);
        global::ClosedXML.Excel.IXLCell cell2 = iXLWorksheet2.Cell(row: row3, column: column3);
        cell2.Value = "am";
        var attendance = new AllStudentAttendanceListBuilder();
        global::OnlineRegistry.AttendanceExcel.AttendanceListsExcelParser createParser = CreateParser(builder);
        global::OnlineRegistry.AttendanceExcel.ParseAttendanceListsExcelParams p = new(new(), attendance, filtered, workbook);
        createParser.Parse(p);
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList collection = attendance.Build(Attendance.Present);
        global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<global::ScheduleLib.OnlineRegistry.StudentsLookupKey, global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances>> collection2 = collection;
        var list = Assert.Single(collection2);
        Assert.Equal(expected: 2, actual: list.Value.StudentNames.Count);
        Assert.Equal(expected: 2, actual: list.Value.Attendance.DayCount);
        Assert.Equal(expected: Attendance.NotPresent, actual: list.Value.Attendance.Day(0)
            .Student(0));
        Assert.Equal(expected: Attendance.Present, actual: list.Value.Attendance.Day(0)
            .Student(1));
        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: list.Value.Attendance.Day(1)
            .Student(1));
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
        CreateParser(builder)
            .Parse(new(
            new() { StudentNameColumn = 3 }, attendance, filtered, workbook));
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList CalculateCollection()
        {
            global::ScheduleLib.OnlineRegistry.StudentAttendanceList collection = attendance.Build(Attendance.Present);
            return collection;
        }

        Assert.Single(Assert.Single(CalculateCollection()).Value.StudentNames);
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
        CreateParser(builder)
            .Parse(new(
            new() { HeaderFormat = new() { FormatType = HeaderFormatType.IgnoreHeader } },
            attendance, filtered, workbook));
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList CalculateCollection()
        {
            global::ScheduleLib.OnlineRegistry.StudentAttendanceList collection = attendance.Build(Attendance.Present);
            return collection;
        }

        Assert.Single(Assert.Single(CalculateCollection()).Value.StudentNames);
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
        emptyTable.Range("A1:C3")
            .CreateTable();
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
        CreateParser(builder)
            .Parse(new(
            new()
            {
                HeaderFormat = new() { FormatType = format },
                DateMode = format is HeaderFormatType.Auto or HeaderFormatType.Date
                    ? AttendanceDateMode.ValidateAndBind : AttendanceDateMode.Ignore,
            }, attendance, filtered, workbook));
        var lists = attendance.Build(Attendance.Present);
        Assert.Single(lists);
        global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
        {
            global::ScheduleLib.CourseId course2 = course;
            global::ScheduleLib.GroupId group2 = group;
            global::System.DateTime dateTime = new(year: 2026, month: 9, day: 2);
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = DateKey(course2, group2, dateTime);
            return key;
        }

        Assert.Equal(expected: Attendance.NotPresent, actual: lists.Get(CalculateKey())
            .Student(0));
    }

    [Fact]
    public void EntirelyEmptyWorkbookProducesNoAttendanceListsEvenWithStrictDateHeaders()
    {
        var (builder, filtered, _, _) = CreateDateBindingFixture();
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Unmatched empty sheet");
        workbook.AddWorksheet("Unmatched formatted sheet")
            .Range("A1:D50").Style.Font.Bold = true;
        var attendance = new AllStudentAttendanceListBuilder();
        CreateParser(builder)
            .Parse(new(
            new()
            {
                HeaderFormat = new() { FormatType = HeaderFormatType.Date },
                DateMode = AttendanceDateMode.ValidateAndBind,
            }, attendance, filtered, workbook));
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList collection = attendance.Build(Attendance.Present);
        global::System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
    }

}
