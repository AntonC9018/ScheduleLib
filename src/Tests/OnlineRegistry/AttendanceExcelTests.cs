using ClosedXML.Excel;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class AttendanceExcelTests
{
    private const string ExcelFilePath = "data/attendance.xlsx";
    private const string ScheduleJsonPath = "data/schedule_2025_1.json";

    [Fact]
    public async Task ExcelSnapshot()
    {
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2025);
        using var cts = TestHelper.CreateCts();
        var courseNameUnifier = new CourseNameUnifierModule(Config.CourseNameParser);

        {
            await using var scheduleJson = File.OpenRead(ScheduleJsonPath);
            var model = await ScheduleSerializer.Deserialize(scheduleJson, cts.Token);
            ScheduleSerializer.AddToBuilder(builder, model, courseNameUnifier);
        }

        var schedule = builder.Build();
        var antonId = builder.Lookup().Teacher("Curmanschii")!.Value;
        var filteredSchedule = schedule.Filter(new()
        {
            TeacherFilter =
            {
                IncludeIds = [antonId],
            },
        });

        using var workbook = new XLWorkbook(ExcelFilePath);
        var lists = AttendanceExcel.ParseAttendanceListsExcel(new()
        {
            Schedule = filteredSchedule,
            Workbook = workbook,
            CourseNames = courseNameUnifier,
            LookupModule = builder.Lookup().LookupModule,
            GroupParseContext = builder.GroupParseContext!,
        });

        await Verify(lists.Select(x => new
        {
            Course = schedule.Get(x.Key.CourseId).FullName,
            x.Key.LessonType,
            Group = schedule.Get(x.Key.GroupId).Name,
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
