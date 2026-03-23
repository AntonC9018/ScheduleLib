using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Application.Config;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Application.Core;

[AutoConstructor]
public sealed partial class StudentAttendanceLoader
{
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<StudentAttendanceLoader>();
    }

    private readonly DataProvider<LessonAttendanceConfig> _configProvider;
    private readonly ScopeFilteredScheduleProvider _scheduleProvider;
    private readonly AttendanceListsExcelParser _parser;

    public StudentAttendanceList Load()
    {
        var attendanceConfig = _configProvider.Get();
        if (attendanceConfig is null)
        {
            return new([]);
        }

        var filteredSchedule = _scheduleProvider.Get();
        var builder = new AllStudentAttendanceListBuilder();
        foreach (var source in attendanceConfig.Sources)
        {
            if (source.FilePath == null)
            {
                throw new InvalidOperationException("Misconfigured source with a null path.");
            }
            var parseParams = new WorksheetParseParameters();
            {
                if (source.RepeatedCourseBehavior is { } x)
                {
                    parseParams.RepeatedCourseBehavior = x;
                }
            }
            {
                if (source.CellValueFormat is { } x)
                {
                    parseParams.CellValueFormat = x;
                }
            }

            using var stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            var parser = _parser;
            parser.Parse(new(
                parseParameters: parseParams,
                builder: builder,
                schedule: filteredSchedule,
                workbook: workbook));
        }

        // Maybe configure this per workbook.
        var ret = builder.Build(missingDaysFiller:
            attendanceConfig.MissingDaysFiller ?? Attendance.Present);
        return ret;
    }
}
