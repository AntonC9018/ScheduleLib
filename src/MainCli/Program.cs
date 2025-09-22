using System.Text;
using ScheduleLib.Curriculum.Download;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using MainCli;
using ScheduleLib.OnlineRegistry;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;

Console.WriteLine("Start");

var dayNameProvider = new DayNameProvider();
var context = DocParseContext.Create(new()
{
    DayNameProvider = dayNameProvider,
    CourseNameParserConfig = Config.CourseNameParser,
});

context.Schedule.ConfigureRemappings(Config.ConfigureRemappings);

{
    const string fileName = @"data\Cadre didactice DI 2024-2025.xlsx";
    Tasks.OptionallyEnrichContextWithTeacherFullNames(context.Schedule, fileName);
}

var cancellationToken = CancellationToken.None;
_ = cancellationToken;

const Session semester = Session.Ses1;
{
    const int year = 2025;
    context.Schedule.SetStudyYear(year);

    string dirName = @$"data\{year}_sem{(int) semester}";
    await Tasks.ParseDocumentDirIntoSchedule(
        context,
        dirName,
        cancellationToken: cancellationToken);
}

var schedule = context.Schedule.Build();
Console.WriteLine("Schedule built");


IConfiguration config;
{
    var builder = new ConfigurationBuilder();
    builder.AddUserSecrets<Program>();
    config = builder.Build();
}

// var option = Option.FreeRooms;
foreach (var option in new Option[] { Option.AllTeachersExcel }) {

switch (option)
{
    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.AllTeachersExcel:
    {
        const string outputFile = "all_teachers_orar.xlsx";
        string outputFileFullPath = Path.GetFullPath(outputFile);

        var timeConfig = new DefaultLessonTimeConfig(context.TimeConfig);

        var filteredSchedule = schedule.Filter(new()
        {
            PeriodFilter = new()
            {
                PeriodId = new(schedule.Periods.Length - 1),
                UnspecifiedIsAll = true,
            },
        });

        Tasks.GenerateAllTeacherExcel(new()
        {
            DayNameProvider = new DayNameProvider(),
            StringBuilder = new(),
            LessonTypeDisplay = new(),
            ParityDisplay = new(),
            TimeSlotDisplay = new(),
            SeminarDate = (DayOfWeek.Wednesday, timeConfig.T15_00),
            OutputFilePath = outputFileFullPath,
            Schedule = filteredSchedule,
            TimeConfig = context.TimeConfig,
        });

        ExplorerHelper.TryOpenExplorerAndSelectFile(outputFileFullPath);
        break;
    }

    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.PerGroupAndPerTeacherPdfs:
    {
        await Tasks.GeneratePdfForGroupsAndTeachers(new()
        {
            LessonTextDisplayServices = new()
            {
                ParityDisplay = new(),
                LessonTypeDisplay = new(),
                SubGroupNumberDisplay = new(),
            },
            Schedule = schedule,
            LessonTimeConfig = context.TimeConfig,
            TimeSlotDisplay = new(),
            DayNameProvider = dayNameProvider,
            OutputPath = "output",
        });
        break;
    }

    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.CreateLessonsInRegistry:
    {
        HolidayPeriod[] holidayPeriods;
        // TODO: Get this from "calendar academic"
        holidayPeriods = [];

        var dateProvider = Tasks.CreateDateProviderFromWeekParityExcel(new()
        {
            InputPath = @"data\Paritate.docx",
            Holidays = holidayPeriods,
        });
        var credentials = Tasks.GetRegistryCredentials(
            config,
            allowUserInput: true);
        await RegistryScraping.AddLessonsToOnlineRegistry(new()
        {
            CancellationToken = cancellationToken,
            Credentials = credentials,
            Schedule = schedule,
            Session = semester,
            ErrorHandler = new RegistryErrorLogger(),
            CourseNameUnifier = context.CourseNameUnifierModule,
            GroupParseContext = context.Schedule.GroupParseContext!,
            LookupModule = context.Schedule.LookupModule!,
            DateProvider = dateProvider,
            TimeConfig = context.TimeConfig,
            ProcessingFlags = CommandProcessingConfig.Process
                .WithDryRun(LessonEquationCommandTypes.Create | LessonEquationCommandTypes.Delete),
        });
        break;
    }

    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.PullCurriculaFromOneDrive:
    {
        var c = config.GetMicrosoftGraphAuth();
        await CurriculaDownloadTasks.PullCurriculaToDisk(c, cancellationToken);
        break;
    }

    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.FreeRooms:
    {
        await Tasks.GenerateFreeRoomsExcel(new()
        {
            Schedule = schedule,
            TimeConfig = context.TimeConfig,
            DayNameProvider = dayNameProvider,
            OutputPath = "output/free_rooms.xlsx",
            CancellationToken = cancellationToken,
            ParityDisplay = new ParityDisplayHandler(),
            TimeSlotDisplay = new(),
        });
        break;
    }

    case Option.FreeHoursOfGroup:
    {
        var sb = new StringBuilder();
        foreach (var parity in new[]{Parity.EvenWeek, Parity.OddWeek})
        {
            foreach (var group in new[] { "IA2401", "I2301" })
            {
                foreach (var isOptional in new[] { true, false })
                {
                    var displayHandler = new TimeSlotDisplayHandler();
                    var groupId = schedule.Groups
                        .WithIndex()
                        .Where(x => x.Item.Name == group)
                        .Select(x => new GroupId(x.Index))
                        .Single();
                    var lessons = schedule.RegularLessons
                        .Where(x => x.Lesson.Groups.Contains(groupId) && x.Date.Parity.IsMatch(parity))
                        .Where(x =>
                        {
                            if (!isOptional)
                            {
                                return true;
                            }
                            var sg = x.Lesson.SubGroup;
                            if (sg == SubGroup.All)
                            {
                                return true;
                            }
                            if (sg.Value == "opțional")
                            {
                                return true;
                            }
                            return false;
                        });

                    var allTimes = context.TimeConfig.TimeSlots
                        .SelectMany(x => new[]
                            {
                                DayOfWeek.Monday,
                                DayOfWeek.Tuesday,
                                DayOfWeek.Wednesday,
                                DayOfWeek.Thursday,
                                DayOfWeek.Friday,
                            }
                            .Select(y => (Day: y, Time: x)));

                    var usedTimes = lessons.Select(x => (Day: x.Date.DayOfWeek, Time: x.Date.TimeSlot));
                    var unusedTimes = allTimes.Except(usedTimes);

                    var orderedTimes = unusedTimes.OrderBy(x => (x.Day, x.Time));
                    var byDay = orderedTimes
                        .GroupBy(x => x.Day)
                        .Select(x => (Day: x.Key, Times: MergeConsecutive(x.Select(y => y.Time))));

                    var parityDisplay = new ParityDisplayHandler();
                    sb.AppendLine($"paritatea: {parityDisplay.Get(parity)}, grupa: {group}, optional?: {isOptional}");
                    foreach (var day in byDay)
                    {
                        sb.Append(dayNameProvider.GetDayName(day.Day));
                        sb.Append(":");

                        var listBuilder = new ListStringBuilder(sb, ',');
                        foreach (var time in day.Times)
                        {
                            var start = time.Start;
                            var end = time.EndInclusive;
                            var startTime = context.TimeConfig.GetTimeSlotInterval(start).Start;
                            var endTime = context.TimeConfig.GetTimeSlotInterval(end).End;
                            var duration = endTime - startTime;
                            var intervalStr = displayHandler.IntervalDisplay(new TimeSlotInterval(startTime, duration));
                            listBuilder.Append(intervalStr);
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine();
                    continue;


                    IEnumerable<(TimeSlot Start, TimeSlot EndInclusive)> MergeConsecutive(IEnumerable<TimeSlot> x)
                    {
                        using var e = x.GetEnumerator();
                        if (!e.MoveNext())
                        {
                            yield break;
                        }
                        var start = e.Current;
                        var prev = start;
                        while (true)
                        {
                            if (!e.MoveNext())
                            {
                                yield return (start, prev);
                                yield break;
                            }
                            var c = e.Current;
                            if (c.Index - prev.Index > 1)
                            {
                                yield return (start, prev);
                                start = c;
                            }
                            prev = c;
                        }
                    }
                }
            }
        }
        Console.WriteLine(sb.ToStringAndClear());

        break;
    }
}


}
