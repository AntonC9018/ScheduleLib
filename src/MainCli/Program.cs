using System.Drawing;
using System.Text;
using ClosedXML.Excel;
using MainCli;
using MainCli.BuilderNew.Impl;
using MainCli.Helper;
using MainCli.Topics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.AttendanceExcel;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Curriculum.Download;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;
using WebsiteJsonSchedule;
using Directory = System.IO.Directory;
using Option = MainCli.Option;

#pragma warning disable CS8321 // Local function is declared but never used

var services = new ServiceCollection();
services.AddScheduleServices();
services.AddConfigsServices();
services.AddTaskHandlers();

services.AddOptions<ManifestDirectoriesOptions>().Configure(x =>
{
    x.Directories.Add("data/topics");
});
services.AddOptions<StudyYearOptions>().Configure(x =>
{
    x.StudyYear = 2025;
    x.Semester = Semester.Sem2;
});

IConfiguration config;
{
    var builder = new ConfigurationBuilder();
    builder.AddUserSecrets<Program>();
    config = builder.Build();
}

// Bind configs from user secrets.

var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true,
});

{
    var b = serviceProvider.GetRequiredService<ApplicationConfigBuilder>();
    b.AddDefaultConfig();
}

var cancellationToken = CancellationToken.None;
_ = cancellationToken;

var outputDirectory = new TempOutputDirectoryService("output");
outputDirectory.Initialize();

const string allTeachersOutputFile = "all_teachers_orar.xlsx";
const string freeRoomExcelFilePath = "free_rooms.xlsx";

// TODO: Use DI
var options = new Option[]
{
    Option.CreateLessonsInRegistry,
    // Option.AllTeachersExcel,
    // Option.PerGroupAndPerTeacherPdfs,
    // Option.FreeRooms,
    // Option.CreateLessonsInRegistry,
    // Option.TableOfAllLabLessons,
    // Option.JsonSchedulesForWebsite,
    // Option.CopyGradesFromMoodleToRegistry,
};
foreach (var option in options)
{
    await using var scope = serviceProvider.CreateMarkerScope(x =>
    {
        x.TeacherName = NameHelper.Parse("Curmanschii Anton");
    });

    switch (option)
    {
        case Option.UploadDocsToDrive:
        {
            outputDirectory.Clear();

            Task[] tasks = [
                GenerateAllTeacherExcel(scope.ServiceProvider),
                GenerateFreeRoomsExcel(scope.ServiceProvider),
                GeneratePdfsForGroupsAndTeachers(scope.ServiceProvider),
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
            await GenerateAllTeacherExcel(scope.ServiceProvider);
            outputDirectory.TryOpenFileInExplorer(allTeachersOutputFile);
            break;
        }

        // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
        case Option.PerGroupAndPerTeacherPdfs:
        {
            await GeneratePdfsForGroupsAndTeachers(scope.ServiceProvider);
            outputDirectory.TryOpenInExplorer();
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
            ILessonTopics topics;
            {
                string manifestPath = Path.GetFullPath(@"data\topics\anton\manifest.json");
                var builder = await AllLessonTopicsDatabaseBuilder.Parse(
                    manifestPath,
                    context.Schedule.Lookup(),
                    schedule,
                    cancellationToken);
                // builder.FallbackProvider(LessonType.Lab, new LabAutoNumberingNameProvider());
                builder.FallbackProvider(LessonType.Lab, new NoNameProvider());
                topics = builder.Build();
            }
#if false
        {
            topics = new LessonTopicsFromDatabase([]);
        }
#endif

            using var registryContext = await RegistryScrapingContext.Create(
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
                EquationCommandsDerivation = new AnyDayDerivation(),
                DateProvider = dateProvider,
                TimeConfig = context.TimeConfig,
                ProcessingFlags = CommandProcessingConfig.Process
                    .WithLog(LessonEquationCommandTypes.All),
                SemesterIntervalProvider = Config.SemesterIntervalProvider(),
                Attendance = attendance,
                LessonTopics = topics,
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
            await GenerateFreeRoomsExcel(scope.ServiceProvider);
            ExplorerHelper.TryOpenExplorerAndSelectFile(freeRoomExcelFilePath);
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

            var teacherId = scope.ServiceProvider.GetRequiredService<CurrentTeacherIdProvider>().Get();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            var filteredSchedule = schedule.Filter(
                FilterHelper.Builder()
                    .WithTeacher(teacherId)
                    .WithLessonType(LessonType.Lab));

            const string outputFileName = "deadlines.xlsx";
            await using var outputFile = outputDirectory.File(outputFileName, FileMode.Create, FileAccess.Write);
            Tasks.GenerateDeadlinesExcel(new()
            {
                Schedule = filteredSchedule,
                DateProvider = dateProvider,
                Semester = semester,
                TimeConfig = context.TimeConfig,
                OutputFilePath = outputFilePath,
                SemesterIntervalProvider = Config.SemesterIntervalProvider(),
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

            var services1 = new WebsiteJsonScheduleHelper.Services
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
                    services1);
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

        case Option.CopyGradesFromMoodleToRegistry:
        {
            // TODO: REALLY move to service provider.
            await Tasks.CopyGradesFromMoodleForTest(
                config,
                context.CourseNameUnifierModule,
                context.Schedule.LookupModule!,
                schedule,
                context.Schedule.GroupParseContext!,
                semester,
                "317382",
                cancellationToken);
            break;
        }
        // case Option.Query:
        // {
        //     var res = schedule.RegularLessons
        //         .Where(x =>
        //             x.Date.TimeSlot == context.TimeConfig.FindTimeSlotByStartTime(new TimeOnly(hour: 16, minute: 45))
        //             && x.Date.DayOfWeek == DayOfWeek.Wednesday)
        //         .Where(x =>
        //             x.Lesson.Room.Id == "350/4")
        //         .Where(x =>
        //             x.Date.Period == schedule.LatestPeriodId())
        //         .Select(x => new
        //             {
        //                 x,
        //                 teacher = schedule.Get(x.Lesson.Teachers[0]),
        //             })
        //         .ToArray();
        //
        //     var t = res;
        //     _ = t;
        //     break;
        // }
    }
}

Task GenerateFreeRoomsExcel(IServiceProvider sp)
{
    return Task.Run(async () =>
    {
        var handler = sp.GetRequiredService<GenerateFreeRoomsHandler>();
        await using var outputStream = outputDirectory.File(freeRoomExcelFilePath, FileMode.Create, FileAccess.Write);
        await handler.Run(new()
        {
            CancellationToken = cancellationToken,
            OutputStream = outputStream,
        });
    });
}

Task GeneratePdfsForGroupsAndTeachers(IServiceProvider sp)
{
    return Task.Run(async () =>
    {
        var handler = sp.GetRequiredService<GeneratePdfsForGroupsAndTeachersHandler>();
        outputDirectory.Clear();

        await handler.Run(new()
        {
            CancellationToken = cancellationToken,
            OutputDirectory = outputDirectory,
        });
    });
}

Task GenerateAllTeacherExcel(IServiceProvider sp)
{
    return Task.Run(async () =>
    {
        var schedule = sp.LatestPeriodSchedule();
        var handler = sp.GetRequiredService<GenerateAllTeachersExcelHandler>();
        await using var outputStream = outputDirectory.File(allTeachersOutputFile, FileMode.Create, FileAccess.Write);
        await handler.Run(new()
        {
            Schedule = schedule,
            CancellationToken = cancellationToken,
            OutputDirectory = outputStream,
        });
    });
}

StudentAttendanceList GetAttendanceListOfCurrentTeacher(IServiceProvider sp)
{
    var filteredSchedule = sp.ScopedSchedule();
    using var workbook = new XLWorkbook();

    // var attendanceConfig = sp.GetRequiredService<ConfigProvider>().Get(LessonAttendanceConfig.Key);
    // attendanceConfig.Sources

    var attendance = AttendanceExcel.ParseAttendanceListsExcel(new()
    {
        Schedule = filteredSchedule,
        Workbook = workbook,
        CourseNames = sp.GetRequiredService<CourseNameUnifierModule>(),
        LookupModule = sp.GetRequiredService<LookupModule>(),
        GroupParseContext = sp.GetRequiredService<GroupParseContext>(),
    });
    return attendance;
}
