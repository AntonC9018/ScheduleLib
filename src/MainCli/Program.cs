using ScheduleLib.Curriculum.Download;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using ReaderApp;
using ReaderApp.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation.TeacherCute;

Console.WriteLine("Start");

var dayNameProvider = new DayNameProvider();
var context = DocParseContext.Create(new()
{
    DayNameProvider = dayNameProvider,
    CourseNameParserConfig = new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#", "Python"],
        IgnoredFullWords = ["p/u", "pentru"],
        IgnoredShortenedWords = ["Opț"],
        IgnoredProgrammingRelatedWords = ["Programare", "limbaj"],
        MinUsefulWordLength = 3,
    }),
});

context.Schedule.ConfigureRemappings(remap =>
{
    var teach = remap.TeacherLastNameRemappings;
    teach.Add("Curmanschi", "Curmanschii");
    teach.Add("Vișnevschi", "Vișnevschii");
    teach.Add("Băț", "Beț");
    teach.Add("Spincean", "Sprîncean");
    teach.Add("Anghelova", "Anghelov");
});

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

var schedule = context.BuildSchedule();
Console.WriteLine("Schedule built");


IConfiguration config;
{
    var builder = new ConfigurationBuilder();
    builder.AddUserSecrets<Program>();
    config = builder.Build();
}

var option = Option.PerGroupAndPerTeacherPdfs;

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
        // var room = schedule.RegularLessons.Where(x => x.Lesson.Room.Id == "15:00").ToArray();
        // var group = room.Select(x => schedule.Get(x.Lesson.Group)).ToArray();
        // _ = group;
        RoomId S(RegularLesson x)
        {
            var r = x.Lesson.Room;
            if (!r.IsValid)
            {
                return r;
            }
            if (r.Id!.Contains("/"))
            {
                return r;
            }
            var updated = $"{r.Id}/4";
            return new RoomId(updated);
        }

        var rooms = schedule.RegularLessons.Select(S);
        var timeConfig = new DefaultLessonTimeConfig(context.TimeConfig);
        var allRooms = rooms.Distinct();
        var roomsToday = schedule.RegularLessons
            .Where(x => x.Date.DayOfWeek == DayOfWeek.Wednesday
                && x.Date.TimeSlot == timeConfig.T15_00
                && x.Date.Parity.IsMatch(Parity.EvenWeek))
            .Select(S);
        var freeRooms = allRooms.Except(roomsToday);
        foreach (var room in freeRooms)
        {
            Console.WriteLine($"Free room: {room.Id}");
        }
        break;
    }
}


