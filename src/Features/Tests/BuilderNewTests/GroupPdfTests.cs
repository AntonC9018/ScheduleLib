using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib;
using ScheduleLib.ScheduleDefaults;
using System.Text;

public sealed class GroupPdfTests
{
    [Fact]
    public async Task DoesNotCollapseCombinationFilesAndKeepsTheWholeGroupPdf()
    {
        var schedule = Build(withSplits: true);

        var files = await Generate(schedule);

        bool condition = new[]
        {
            "IA2401.pdf",
            "IA2401_CV-I.pdf",
            "IA2401_CV-II.pdf",
            "IA2401_DJ-I.pdf",
            "IA2401_DJ-II.pdf",
        }.SequenceEqual(files);
        Assert.True(condition);
    }

    [Fact]
    public async Task AlternativeComesFirstInCombinationFileNames()
    {
        var schedule = BuildWithAlternative();

        var files = await Generate(schedule);

        bool condition = new[]
        {
            "IA2401.pdf",
            "IA2401_A1-CV-I.pdf",
            "IA2401_A1-DJ-I.pdf",
            "IA2401_A2-CV-I.pdf",
            "IA2401_A2-DJ-I.pdf",
        }.SequenceEqual(files);
        Assert.True(condition);
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
        var subGroup2 = SubGroup.CreateNumeric(1);
        SubGroup? subGroup3 = subGroup2;
        AddLesson(subGroup: subGroup3);
        AddLesson(specialization: Specialization.CV);
        AddLesson(specialization: Specialization.DJ);
        var alternative2 = new Alternative("A1");
        Alternative? alternative3 = alternative2;
        AddLesson(alternative: alternative3);
        var alternative4 = new Alternative("A2");
        Alternative? alternative5 = alternative4;
        AddLesson(alternative: alternative5);

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

            string text2 = text.BoldPrefix;
            string text3 = $"{alternative}:";
            string value = text3;
            var comparisonType = StringComparison.Ordinal;
            bool condition;
            if (text2.StartsWith(value, comparisonType))
            {
                condition = true;
            }
            else
            {
                string text4 = text.BoldPrefix;
                string text5 = $"{alternative},";
                string value2 = text5;
                var comparisonType2 = StringComparison.Ordinal;
                condition = text4.StartsWith(value2, comparisonType2);
            }

            string userMessage = $"prefix '{text.BoldPrefix}' should start with alternative '{alternative}'";
            Assert.True(
                condition,
                userMessage);
        }
        Assert.True(seenAlternative);
    }

    [Fact]
    public void LessonPrefixPrintsAlternativeThenSpecializationThenSubgroup()
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

        var lesson = builder.RegularLesson();
        lesson.Group(group);
        lesson.Course(course);
        lesson.DayOfWeek(DayOfWeek.Monday);
        lesson.TimeSlot(TimeSlot.First);
        var lessonBuilder = lesson;
        var subGroup = SubGroup.CreateNumeric(1);
        lessonBuilder.SubGroup(subGroup);
        lesson.Specialization(Specialization.CV);
        var lessonBuilder2 = lesson;
        var alternative = new Alternative("A1");
        lessonBuilder2.Alternative(alternative);

        var schedule = builder.Build();
        var accessor = schedule.EnumerateAllLessons()
            .Single();

        var services = new LessonTextDisplayHandler.Services(
            new SubGroupNumberDisplayHandler(),
            new ParityDisplayHandler(),
            new LessonTypeDisplayHandler());
        var handler = new LessonTextDisplayHandler(services, new());

        var text = new RecordingRichText();
        handler.Handle(new()
        {
            TextDescriptor = text,
            Schedule = schedule,
            LessonTimeConfig = LessonTimeConfig.CreateDefault(),
            Lesson = accessor,
            ColumnWidth = 100,
            StringBuilder = new StringBuilder(),
        });

        Assert.Equal(expected: "A1, CV, I: ", actual: text.BoldPrefix);
    }

    private sealed class RecordingRichText : IRichText
    {
        public string BoldPrefix = "";

        public void Span(string str, bool isBold = false)
        {
            bool CalculateConditionResult()
            {
                if (!isBold)
                {
                    return false;
                }

                if (BoldPrefix == "")
                {
                    return true;
                }

                return false;
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
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

        bool condition = new[]
        {
            "IA2401.pdf",
        }.SequenceEqual(files);
        Assert.True(condition);
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
            var subGroup2 = SubGroup.CreateNumeric(1);
            SubGroup? subGroup3 = subGroup2;
            AddLesson(subGroup: subGroup3);
            var subGroup4 = SubGroup.CreateNumeric(2);
            SubGroup? subGroup5 = subGroup4;
            AddLesson(subGroup: subGroup5);
            AddLesson(specialization: Specialization.CV);
            AddLesson(specialization: Specialization.DJ);
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
        string path1 = Path.GetTempPath();
        string path2 = $"ScheduleLib-pdf-tests-{Guid.NewGuid():N}";
        var root = Path.Combine(path1, path2);
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
                Config.SpecializationRegistry,
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
            Assert.All(
                files,
                file =>
                {
                    string fileName = output.BuildPath(file);
                    var fileInfo = new FileInfo(fileName);
                    bool condition = fileInfo.Length > 0;
                    Assert.True(condition);
                });
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
