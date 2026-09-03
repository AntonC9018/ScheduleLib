using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib;

public sealed class GroupPdfTests
{
    [Fact]
    public async Task DoesNotCollapseCombinationFilesAndKeepsTheWholeGroupPdf()
    {
        var schedule = Build(withSplits: true);

        var files = await Generate(schedule);

        Assert.True(new[]
        {
            "IA2401.pdf",
            "IA2401_CV-I.pdf",
            "IA2401_CV-II.pdf",
            "IA2401_DJ-I.pdf",
            "IA2401_DJ-II.pdf",
        }.SequenceEqual(files));
    }

    [Fact]
    public async Task AGroupWithoutSplitsGetsOnlyItsWholeGroupPdf()
    {
        var schedule = Build(withSplits: false);

        var files = await Generate(schedule);

        Assert.True(new[] { "IA2401.pdf" }.SequenceEqual(files));
    }

    private static Schedule Build(bool withSplits)
    {
        var builder = new ScheduleBuilder
        {
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2025),
            }),
        };
        builder.EnableLookupModule();

        var group = builder.Group("IA2401").Id;
        var course = builder.Course("Course");

        AddLesson();
        if (withSplits)
        {
            AddLesson(subGroup: SubGroup.CreateNumeric(1));
            AddLesson(subGroup: SubGroup.CreateNumeric(2));
            AddLesson(specialization: Specializations.CV);
            AddLesson(specialization: Specializations.DJ);
        }

        return builder.Build();

        void AddLesson(
            SubGroup? subGroup = null,
            Specialization? specialization = null)
        {
            var lesson = builder.RegularLesson();
            lesson.Group(group);
            lesson.Course(course);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
            if (subGroup is { } subgroupValue)
            {
                lesson.SubGroup(subgroupValue);
            }
            if (specialization is { } specializationValue)
            {
                lesson.Specialization(specializationValue);
            }
        }
    }

    private static async Task<string[]> Generate(Schedule schedule)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ScheduleLib-pdf-tests-{Guid.NewGuid():N}");
        var output = new OutputDirectory(root);
        output.Initialize();
        try
        {
            var services = new LessonTextDisplayHandler.Services(
                new SubGroupNumberDisplayHandler(),
                new ParityDisplayHandler(),
                new LessonTypeDisplayHandler());
            var handler = new GeneratePdfsForGroupsAndTeachersTaskHandler(
                services,
                LessonTimeConfig.CreateDefault(),
                new TimeSlotDisplayHandler(),
                new DayNameProvider(),
                SpecializationRegistryHelper.CreateDefault(),
                schedule);

            await handler.Run(new()
            {
                CancellationToken = CancellationToken.None,
                OutputDirectory = output,
            });

            var files = output.FilePaths("*.pdf", new())
                .Select(x => x.Path)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            Assert.All(files, file => Assert.True(new FileInfo(output.BuildPath(file)).Length > 0));
            return files;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
