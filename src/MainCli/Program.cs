using System.Drawing;
using System.Text;
using ClosedXML.Excel;
using ScheduleLib.Curriculum.Download;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using MainCli;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.OnlineRegistry;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.ScheduleDefaults;
using WebsiteJsonSchedule;

#pragma warning disable CS8321 // Local function is declared but never used

Console.WriteLine("Start");

var dayNameProvider = new DayNameProvider();
var context = DocParseContext.Create(new()
{
    DayNameProvider = dayNameProvider,
    CourseNameParserConfig = Config.CourseNameParser,
});

context.Schedule.ConfigureRemappings(Config.ConfigureRemappings);

var cancellationToken = CancellationToken.None;
_ = cancellationToken;

const int year = 2025;
const Semester semester = Semester.Sem1;

context.Schedule.SetStudyYear(year);

string scheduleSourcesDir = @$"data\{year}_sem{semester.AsOrdinal()}";
string serializedSchedulePath = @$"data\schedule_{year}_{semester.AsOrdinal()}.json";
var schedule = await Tasks.LoadSchedule(
    context,
    scheduleSourcesDir: scheduleSourcesDir,
    serializedSchedulePath: serializedSchedulePath,
    beforeEndAction: static context =>
    {
        // TODO: This is not included in the hash
        const string fileName = @"data\Cadre didactice DI 2024-2025.xlsx";
        Tasks.OptionallyEnrichContextWithTeacherFullNames(context.Schedule, fileName);
    },
    cancellationToken);

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
    // Option.UploadDocsToDrive,
    // Option.CreateLessonsInRegistry,
    // Option.TableOfAllLabLessons,
    Option.JsonSchedulesForWebsite,
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
        var dateProvider = new ManualAllScheduledDateProvider(
            studyWeeks: Config.StudyWeeks,
            holidays: Config.HolidayPeriods);

        var credentials = Tasks.GetRegistryCredentials(
            config,
            allowUserInput: true);

        var attendance = GetAttendanceListOfCurrentTeacher();
        // var attendance = new AllStudentAttendanceListBuilder().Build();

        using var registryContext = await RegistryScraping.CreateContext(
            credentials: credentials,
            cancellationToken: cancellationToken);

        await registryContext.AddLessonsToOnlineRegistry(new()
        {
            CancellationToken = cancellationToken,
            Schedule = schedule,
            Semester = semester,
            ErrorHandler = new RegistryErrorLogger
            {
                ExtraLessonAction = ExtraLessonInstanceAction.LeaveAlone,
            },
            CourseNameUnifier = context.CourseNameUnifierModule,
            GroupParseContext = context.Schedule.GroupParseContext!,
            LookupModule = context.Schedule.LookupModule!,

            DateProvider = dateProvider,
            TimeConfig = context.TimeConfig,
            ProcessingFlags = CommandProcessingConfig.Process
                .WithLog(LessonEquationCommandTypes.All),
            SemesterIntervalProvider = Config.SemesterIntervalProvider(),
            Attendance = attendance,
            LessonTopics = new([]),
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

    case Option.TableOfAllLabLessons:
    {
        var dateProvider = new ManualAllScheduledDateProvider(
            studyWeeks: Config.StudyWeeks,
            holidays: Config.HolidayPeriods);

        var teacherId = GetCurrentTeacherId();
        var filteredSchedule = schedule.Filter(new()
        {
            TeacherFilter = new()
            {
                IncludeIds = [teacherId],
            },
            LessonFilter = new()
            {
                LessonType = LessonType.Lab,
            },
        });

        const string outputFileName = "deadlines";
        const string outputFilePath = $"{outputDirectory}/{outputFileName}.xlsx";
        Tasks.GenerateDeadlinesExcel(new()
        {
            Schedule = filteredSchedule,
            DateProvider = dateProvider,
            Semester = semester,
            TimeConfig = context.TimeConfig,
            OutputFilePath = outputFilePath,
            SemesterIntervalProvider = Config.SemesterIntervalProvider(),
            BadColor = Color.Red,
            GoodColor = Color.LightGreen,
            LessonDelayLimit = 3,
            MaxTaskRows = 40,
        });
        ExplorerHelper.TryOpenExplorerAndSelectFile(outputFilePath);
        break;
    }

    case Option.JsonSchedulesForWebsite:
    {
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
        Directory.CreateDirectory(outputDirectory);

        var services = new WebsiteJsonScheduleHelper.Services()
        {
            ParityDisplay = new(),
            LessonTypeDisplay = new(),
            SubGroupNumberDisplay = new(),
        };
        var baseFilter = FilterHelper.Builder()
            .WithLatestPeriod(schedule);
        var grouping = schedule.TeacherGrouping(baseFilter);
        foreach (var (teacher, filteredSchedule) in grouping.Filter(schedule))
        {
            var model = WebsiteJsonScheduleHelper.CreateSerializationModel(
                filteredSchedule,
                services);
            var name = teacher.Item.PersonName;
            var sb = new StringBuilder();
            sb.Append($"{outputDirectory}/");
            TeacherNameHelper.AsFileName(sb, name);
            sb.Append(".json");
            var fileName = sb.ToString();
            await using var outputFile = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            await WebsiteJsonScheduleHelper.Serialize(model, outputFile);
        }
        ExplorerHelper.TryOpenExplorerAndSelectFile(outputDirectory);
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
            DayNameProvider = dayNameProvider,
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

TeacherId GetCurrentTeacherId()
{
    var teacherId = context.Schedule.Lookup().Teacher("Anton", "Curmanschii")!.Value;
    return teacherId;
}

StudentAttendanceList GetAttendanceListOfCurrentTeacher()
{
    using var workbook = new XLWorkbook(@"C:\Users\Anton\Desktop\lipse.xlsx");
    var teacherId = GetCurrentTeacherId();
    var filteredSchedule = schedule.Filter(new()
    {
        TeacherFilter = new()
        {
            IncludeIds = [teacherId],
        },
    });
    var attendance = AttendanceExcel.ParseAttendanceListsExcel(new()
    {
        Schedule = filteredSchedule,
        Workbook = workbook,
        CourseNames = context.CourseNameUnifierModule,
        LookupModule = context.Schedule.LookupModule!,
        GroupParseContext = context.Schedule.GroupParseContext!,
    });
    return attendance;
}
