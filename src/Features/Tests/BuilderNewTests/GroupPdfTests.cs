using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib;
using System.Text;

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
    public async Task AlternativeComesFirstInCombinationFileNames()
    {
        var schedule = BuildWithAlternative();

        var files = await Generate(schedule);

        Assert.True(new[]
        {
            "IA2401.pdf",
            "IA2401_A1-CV-I.pdf",
            "IA2401_A1-DJ-I.pdf",
            "IA2401_A2-CV-I.pdf",
            "IA2401_A2-DJ-I.pdf",
        }.SequenceEqual(files));
    }

    private static Schedule BuildWithAlternative()
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
        AddLesson(subGroup: SubGroup.CreateNumeric(1));
        AddLesson(specialization: Specializations.CV);
        AddLesson(specialization: Specializations.DJ);
        AddLesson(alternative: new Alternative("A1"));
        AddLesson(alternative: new Alternative("A2"));

        return builder.Build();

        void AddLesson(
            SubGroup? subGroup = null,
            Specialization? specialization = null,
            Alternative? alternative = null)
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
            if (alternative is { } alternativeValue)
            {
                lesson.Alternative(alternativeValue);
            }
        }
    }

    [Fact]
    public void LessonPrefixIncludesAlternativeFirst()
    {
        var schedule = BuildWithAlternative();

        var services = new LessonTextDisplayHandler.Services(
            new SubGroupNumberDisplayHandler(),
            new ParityDisplayHandler(),
            new LessonTypeDisplayHandler());
        var handler = new LessonTextDisplayHandler(services, new());

        var seenAlternative = false;
        foreach (var lesson in schedule.EnumerateAllLessons())
        {
            if (lesson.Lesson.Alternative.Value is not { } alternative)
            {
                continue;
            }
            seenAlternative = true;

            var text = new RecordingRichText();
            handler.Handle(new()
            {
                TextDescriptor = text,
                Schedule = schedule,
                LessonTimeConfig = LessonTimeConfig.CreateDefault(),
                Lesson = lesson,
                ColumnWidth = 100,
                StringBuilder = new StringBuilder(),
            });

            Assert.True(
                text.BoldPrefix.StartsWith(alternative + ":", StringComparison.Ordinal)
                    || text.BoldPrefix.StartsWith(alternative + ",", StringComparison.Ordinal),
                $"prefix '{text.BoldPrefix}' should start with alternative '{alternative}'");
        }
        Assert.True(seenAlternative);
    }

    private sealed class RecordingRichText : IRichText
    {
        public string BoldPrefix = "";
        public void Span(string str, bool isBold = false)
        {
            if (isBold && BoldPrefix == "")
            {
                BoldPrefix = str;
            }
        }
        public void Line(string str)
        {
        }
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
