using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using ScheduleLib.Curriculum.Download;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using MainCli;
using MainCli.Helper;
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

const Semester semester = Semester.Sem1;
{
    const int year = 2025;
    context.Schedule.SetStudyYear(year);

    string dirName = @$"data\{year}_sem{(int) semester}";
    _ = dirName;
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

const string outputDirectory = "output";

const string allTeachersOutputFile = "all_teachers_orar.xlsx";
string allTeachersOutputFileFullPath = Path.GetFullPath($"{outputDirectory}/{allTeachersOutputFile}");

const string freeRoomExcelOutputPath = $"{outputDirectory}/free_rooms.xlsx";

// TODO: Use DI
var options = new Option[]
{
    // Option.AllTeachersExcel,
    // Option.PerGroupAndPerTeacherPdfs,
    // Option.FreeRooms,
    Option.UploadDocsToDrive,
};
foreach (var option in options) {

switch (option)
{
    case Option.UploadDocsToDrive:
    {
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
        Task[] tasks = [
            GenerateAllTeacherExcel(),
            GenerateFreeRoomsExcel(),
            GeneratePdfsForGroupsAndTeachers(),
        ];
        await Task.WhenAll(tasks);
        await Tasks.UploadStuffToDrive(new()
        {
            Configuration = config,
            CancellationToken = cancellationToken,
            OutputDirectory = outputDirectory,
        });
        continue;
    }
    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.AllTeachersExcel:
    {
        await GenerateAllTeacherExcel();
        ExplorerHelper.TryOpenExplorerAndSelectFile(allTeachersOutputFileFullPath);
        break;
    }

    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.PerGroupAndPerTeacherPdfs:
    {
        await GeneratePdfsForGroupsAndTeachers();
        ExplorerHelper.TryOpenExplorerAndSelectFile(outputDirectory);
        break;
    }

    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.CreateLessonsInRegistry:
    {
        HolidayPeriod[] holidayPeriods;
        // TODO: Get this from "calendar academic"
        holidayPeriods = [];

        static StudyWeek Week(int month, int day, bool isOddWeek) =>
            new(monday: new(2025, month, day), isOddWeek: isOddWeek);
        StudyWeek[] studyWeeks =
        [
            Week(month: 9,  day: 1,  isOddWeek: false),
            Week(month: 9,  day: 8,  isOddWeek: true),
            Week(month: 9,  day: 15, isOddWeek: false),
            Week(month: 9,  day: 22, isOddWeek: true),
            Week(month: 9,  day: 29, isOddWeek: false),
            Week(month: 10, day: 6,  isOddWeek: true),
            Week(month: 10, day: 13, isOddWeek: false),
            Week(month: 10, day: 20, isOddWeek: true),
            Week(month: 10, day: 27, isOddWeek: false),
            Week(month: 11, day: 3,  isOddWeek: true),
            Week(month: 11, day: 10, isOddWeek: false),
            Week(month: 11, day: 17, isOddWeek: true),
            Week(month: 11, day: 24, isOddWeek: false),
            Week(month: 12, day: 1,  isOddWeek: true),
            Week(month: 12, day: 8,  isOddWeek: false),
            Week(month: 12, day: 15, isOddWeek: true),
            Week(month: 12, day: 22, isOddWeek: false),
        ];
        var dateProvider = new ManualAllScheduledDateProvider(
            studyWeeks: studyWeeks,
            holidays: holidayPeriods);

        var credentials = Tasks.GetRegistryCredentials(
            config,
            allowUserInput: true);
        await RegistryScraping.AddLessonsToOnlineRegistry(new()
        {
            CancellationToken = cancellationToken,
            Credentials = credentials,
            Schedule = schedule,
            Semester = semester,
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
        await GenerateFreeRoomsExcel();
        ExplorerHelper.TryOpenExplorerAndSelectFile(freeRoomExcelOutputPath);
        break;
    }

    case Option.FreeHoursOfGroup:
    {
        var sb = new StringBuilder();
        Tasks.PrintFreeHoursOfGroup(new()
        {
            Groups = [ "IA2401", "I2301" ],
            Schedule = schedule,
            StringBuilder = sb,
            TimeConfig = context.TimeConfig,
            DayNameProvider = dayNameProvider,
        });
        Console.WriteLine(sb.ToStringAndClear());
        break;
    }
}

}

async Task GenerateFreeRoomsExcel()
{
    await Tasks.GenerateFreeRoomsExcel(new()
    {
        Schedule = schedule,
        TimeConfig = context.TimeConfig,
        DayNameProvider = dayNameProvider,
        OutputPath = freeRoomExcelOutputPath,
        CancellationToken = cancellationToken,
        ParityDisplay = new ParityDisplayHandler(),
        TimeSlotDisplay = new(),
    });
}

async Task GeneratePdfsForGroupsAndTeachers()
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
        OutputPath = outputDirectory,
    });
}

Task GenerateAllTeacherExcel()
{
    return Task.Run(() =>
    {
        var filteredSchedule = schedule.Filter(new()
        {
            PeriodFilter = new()
            {
                PeriodId = new(schedule.Periods.Length - 1),
                UnspecifiedIsAll = true,
            },
        });

        var timeConfig = context.TimeConfig;
        var seminarTime = new TimeOnly(hour: 15, minute: 00);
        var seminarTimeSlot = timeConfig.FindTimeSlotByStartTime(seminarTime)!.Value;
        Tasks.GenerateAllTeacherExcel(new()
        {
            DayNameProvider = new DayNameProvider(),
            StringBuilder = new(),
            LessonTypeDisplay = new(),
            ParityDisplay = new(),
            TimeSlotDisplay = new(),
            SeminarDate = (DayOfWeek.Wednesday, seminarTimeSlot),
            OutputFilePath = allTeachersOutputFileFullPath,
            Schedule = filteredSchedule,
            TimeConfig = context.TimeConfig,
        });
    });
}
