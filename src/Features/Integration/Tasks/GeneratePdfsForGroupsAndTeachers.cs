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
    private readonly SpecializationRegistry _specializationRegistry;
    private readonly Schedule _schedule;

    public readonly struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public OutputDirectory? OutputDirectory { get; init; }
        public Func<string, Func<Stream, CancellationToken, Task>, CancellationToken, Task>? PublishArtifact { get; init; }
    }

    public async ValueTask Run(RunParams p)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var artifacts = new List<(string Name, LessonTextDisplayHandler Display, ScheduleFilter Filter)>();
        {
            var textDisplayHandler = new LessonTextDisplayHandler(
                _lessonTextDisplayServices,
                new()
                {
                });

            var partitionInfoByGroup = _schedule.GetGroupPartitionInfo(_specializationRegistry);

            foreach (var g in _schedule.EnumerateGroups())
            {
                var groupFilter = new GroupFilter
                {
                    OneOfGroupIds = [g.Id],
                };
                GenerateGroupPdf(g, $"{g.Item.Name}.pdf", groupFilter);

                foreach (var combination in partitionInfoByGroup[g.Id].Combinations)
                {
                    var sb = new StringBuilder();
                    sb.Append(g.Item.Name);
                    sb.Append('_');
                    combination.AppendFileNamePart(new ListStringBuilder(sb, "-"));
                    sb.Append(".pdf");
                    var fileName = sb.ToStringAndClear();

                    var combinationFilter = combination.ToGroupFilter(g.Id);
                    GenerateGroupPdf(g, fileName, combinationFilter);
                }
            }

            void GenerateGroupPdf(
                Accessor<Group, GroupId> g,
                string fileName,
                GroupFilter groupFilter)
            {
                artifacts.Add((fileName, textDisplayHandler, new() { GroupFilter = groupFilter }));
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

                artifacts.Add((fileName, textDisplayHandler, new()
                {
                    TeacherFilter = new() { IncludeIds = [new(teacherId1)] },
                }));
            }
        }
        foreach (var artifact in artifacts)
        {
            p.CancellationToken.ThrowIfCancellationRequested();
            await GenerateWithFilter(artifact.Name, artifact.Display, artifact.Filter);
        }

        async Task GenerateWithFilter(
            string name,
            LessonTextDisplayHandler textDisplayHandler,
            ScheduleFilter filter)
        {
            var filteredSchedule = _schedule.Filter(
                filter
                    .WithLatestPeriod(_schedule)
                    .WithLessonRegularity(LessonRegularity.Weekly));
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

            if (p.PublishArtifact is { } publish)
                await publish(name, Generate, p.CancellationToken);
            else
            {
                using var outputFile = p.OutputDirectory!.OpenFile(name, FileMode.Create, FileAccess.Write);
                await Generate(outputFile, p.CancellationToken);
            }

            Task Generate(Stream stream, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                generator.GeneratePdf(stream);
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
