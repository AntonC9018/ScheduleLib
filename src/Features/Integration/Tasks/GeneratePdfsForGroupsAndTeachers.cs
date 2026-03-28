using System.Text;
using AutoConstructor.Attributes;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Generation.TeacherCute;

namespace ScheduleLib.Application.Core;

[AutoConstructor]
public sealed partial class GeneratePdfsForGroupsAndTeachersTaskHandler
{
    private readonly LessonTextDisplayHandler.Services _lessonTextDisplayServices;
    private readonly LessonTimeConfig _lessonTimeConfig;
    private readonly TimeSlotDisplayHandler _timeSlotDisplay;
    private readonly DayNameProvider _dayNameProvider;
    private readonly Schedule _schedule;

    public readonly struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public required OutputDirectory OutputDirectory { get; init; }
    }

    public async ValueTask Run(RunParams p)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var tasks = new List<Task>();
        {
            var textDisplayHandler = new LessonTextDisplayHandler(
                _lessonTextDisplayServices,
                new()
                {
                });

            var subGroupsMap = _schedule.SubGroupsByGroup();

            foreach (var g in _schedule.EnumerateGroups())
            {
                foreach (var subgroup in subGroupsMap[g.Id])
                {
                    var t = Task.Run(() =>
                    {
                        var groupName = g.Item.Name;
                        var sb = new StringBuilder();
                        sb.Append(groupName);
                        if (subgroup != SubGroup.All)
                        {
                            sb.Append($"_{subgroup.Value}");
                        }
                        sb.Append(".pdf");
                        var fileName = sb.ToString();

                        var groupFilter = new GroupFilter
                        {
                            OneOfGroupIds = [g.Id],
                        };
                        if (subgroup != SubGroup.All)
                        {
                            groupFilter.SubGroups = [subgroup, SubGroup.All];
                        }

                        GenerateWithFilter(fileName, textDisplayHandler, new()
                        {
                            GroupFilter = groupFilter,
                        });
                    });
                    tasks.Add(t);
                }
            }
        }

        {
            var textDisplayHandler = new LessonTextDisplayHandler(_lessonTextDisplayServices, new()
            {
                PrintsTeacherName = false,
                PrintsGroupNames = true,
            });
            var sb = new StringBuilder();
            for (int teacherId = 0; teacherId < _schedule.Teachers.Length; teacherId++)
            {
                int teacherId1 = teacherId;

                var teacherName = _schedule.Teachers[teacherId1].PersonName;
                TeacherNameHelper.AsFileName(sb, teacherName);
                sb.Append(".pdf");

                var fileName = sb.ToStringAndClear();

                var t = Task.Run(() =>
                {
                    GenerateWithFilter(fileName, textDisplayHandler, new()
                    {
                        TeacherFilter = new()
                        {
                            IncludeIds = [new(teacherId1)],
                        },
                    });
                });
                tasks.Add(t);
            }
        }
        await Task.WhenAll(tasks);

        void GenerateWithFilter(
            string name,
            LessonTextDisplayHandler textDisplayHandler,
            in ScheduleFilter filter)
        {
            var filteredSchedule = _schedule.Filter(
                filter
                    .WithLatestPeriod(_schedule)
                    .WithAttendanceMode(AttendanceMode.Zi, AttendanceMode.Dual));
            if (filteredSchedule.IsEmpty)
            {
                return;
            }

            var generator = new Generator(new()
            {
                StringBuilder = new(),
                DayNameProvider = _dayNameProvider,
                LessonTextDisplayHandler = textDisplayHandler,
                LessonTimeConfig = _lessonTimeConfig,
                TimeSlotDisplay = _timeSlotDisplay,
            }, filteredSchedule);

            using var outputFile = p.OutputDirectory.OpenFile(name, FileMode.Create, FileAccess.Write);
            // This doesn't have an async overload.
            generator.GeneratePdf(outputFile);
        }
    }
}
