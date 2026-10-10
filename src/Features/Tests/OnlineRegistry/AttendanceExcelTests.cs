using ClosedXML.Excel;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.ScheduleDefaults;
using Tests.ScheduleCommon;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class AttendanceExcelTests
{
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
    [InlineData(0)]
    [InlineData(2)]
    public void LeadingBlankRowsPreserveHeaderAndAttendance(int blankRows)
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
        sheet.Cell(blankRows + 1, 2).Value = "lab";
        sheet.Cell(blankRows + 1, 3).Value = "lab";
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

    // TODO: Add test for header format
}
