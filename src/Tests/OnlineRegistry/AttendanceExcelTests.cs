using ClosedXML.Excel;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
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
        AttendanceExcel.ParseAttendanceListsExcel(new()
        {
            Builder = attendanceBuilder,
            RepeatedCourseBehavior = RepeatedCourseBehavior.Error,
            Schedule = filteredSchedule,
            Workbook = workbook,
            CourseNames = unifier,
            LookupModule = builder.EnableLookupModule(),
            GroupParseContext = builder.GroupParseContext!,
        });
        var lists = attendanceBuilder.Build(missingDaysFiller: Attendance.Present);

        await Verify(lists.Select(x => new
        {
            Course = schedule.Get(x.Key.CourseId).FullName,
            x.Key.LessonType,
            Group = schedule.Get(x.Key.Groups[0]).Name,
            SubGroup = x.Key.SubGroup.Value,
            StudentNames = x.Value.StudentNames
                .OrderBy(y => y.Value)
                .Select(y => new
                {
                    Name = y.Key.ToString(),
                }),
            x.Value.Attendance,
        }));
    }
}
