using System.Text;
using ClosedXML.Excel;
using MainCli;
using MainCli.BuilderNew.Impl;
using Anton.LayeredConfig.Retrieval;
using MainCli.Helper;
using MainCli.Topics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OnlineRegistry.AttendanceExcel;
using QuizModels;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Curriculum.Download;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Scraping.Common.Config;
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
services.AddStudyYear().Configure(x =>
{
    x.StudyYear = 2025;
    x.Semester = Semester.Sem2;
});
services.AddOptions<ManifestDirectoriesOptions>().Configure(x =>
{
    x.Directories.Add("data/topics/manifest.json");
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

{
    await serviceProvider.InitializeSchedule(cancellationToken);
}

var outputDirectory = new TempOutputDirectoryService("output");
outputDirectory.Initialize();

var freeRoomExcelOutputFile = outputDirectory.File("free_rooms.xlsx");
var allTeachersOutputFile = outputDirectory.File("all_teachers_orar.xlsx");

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
            allTeachersOutputFile.TryOpenInExplorer();
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
            var sp = scope.ServiceProvider;
            var attendance = GetAttendanceListOfCurrentTeacher(sp);
            var topics = await GetLessonTopicsOfCurrentTeacher(sp);


            // Passed manually, because this might be reconfigured to target another semester.
            var semester = GetCurrentSemester(sp);
            var handler = sp.GetRequiredService<AddLessonsToOnlineRegistryHandler>();

            using var registryContext = await MakeRegistryContext(sp);
            var navigator = registryContext.Navigator(sp, cancellationToken);

            await handler.Run(new()
            {
                Navigator = navigator,
                Attendance = attendance,
                LessonTopics = topics,
                Semester = semester,
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
            freeRoomExcelOutputFile.TryOpenInExplorer();
            break;
        }

        case Option.FreeHoursOfGroup:
        {
            var sb = new StringBuilder();
            var handler = scope.ServiceProvider.GetRequiredService<PrintFreeHoursOfGroupTaskHandler>();
            handler.Run(new()
            {
                Groups = [ "IA2401", "I2301" ],
                StringBuilder = sb,
            });
            Console.WriteLine(sb.ToStringAndClear());
            break;
        }

        case Option.TableOfAllLabLessons:
        {
            var outputFile = outputDirectory.File("deadlines.xlsx");
            await using var outputStream = outputFile.Open(FileMode.Create, FileAccess.Write);

            var handler = scope.ServiceProvider.GetRequiredService<GenerateDeadlinesExcelTaskHandler>();
            await handler.Run(new()
            {
                CancellationToken = cancellationToken,
                OutputStream = outputStream,
            });
            outputFile.TryOpenInExplorer();
            break;
        }

        case Option.JsonSchedulesForWebsite:
        {
            outputDirectory.Clear();

            var services1 = new WebsiteJsonScheduleHelper.Services
            {
                ParityDisplay = new(),
                LessonTypeDisplay = new(),
                SubGroupNumberDisplay = new(),
            };
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
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
                TeacherNameHelper.AsFileName(sb, name);
                sb.Append(".json");
                var fileName = sb.ToString();
                await using var outputFile = outputDirectory.OpenFile(fileName, FileMode.Create, FileAccess.Write);
                await WebsiteJsonScheduleHelper.Serialize(model, outputFile);
            }
            outputDirectory.TryOpenInExplorer();
            break;
        }

        case Option.CopyGradesFromMoodleToRegistry:
        {
            var sp = scope.ServiceProvider;
            using var registryContext = await MakeRegistryContext(sp);
            using var moodleContext = await MakeMoodleContext(sp);
            var navigator = registryContext.Navigator(sp, cancellationToken);
            var handler = sp.GetRequiredService<CopyGradesFromMoodleForTestTaskHandler>();
            var semester = GetCurrentSemester(sp);

            await handler.Run(new()
            {
                RegistryNavigator = navigator,
                CancellationToken = cancellationToken,
                MoodleContext = moodleContext,
                QuizId = "317382",
                Semester = semester,
            });
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
        var handler = sp.GetRequiredService<GenerateFreeRoomsTaskHandler>();
        await using var outputStream = freeRoomExcelOutputFile.Open(FileMode.Create, FileAccess.Write);
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
        var handler = sp.GetRequiredService<GeneratePdfsForGroupsAndTeachersTaskHandler>();
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
        var handler = sp.GetRequiredService<GenerateAllTeachersExcelTaskHandler>();
        await using var outputStream = allTeachersOutputFile.Open(FileMode.Create, FileAccess.Write);
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

    var attendanceConfig = sp.GetRequiredService<ConfigProvider>().Get(LessonAttendanceConfig.Key);
    var builder = new AllStudentAttendanceListBuilder();
    foreach (var source in attendanceConfig.Sources)
    {
        if (source.FilePath == null)
        {
            throw new InvalidOperationException("Misconfigured source with a null path.");
        }
        AttendanceExcel.ParseAttendanceListsExcel(new()
        {
            RepeatedCourseBehavior = source.RepeatedCourseBehavior ?? RepeatedCourseBehavior.Error,
            Builder = builder,
            Schedule = filteredSchedule,
            Workbook = workbook,
            CourseNames = sp.GetRequiredService<CourseNameUnifierModule>(),
            LookupModule = sp.GetRequiredService<LookupModule>(),
            GroupParseContext = sp.GetRequiredService<GroupParseContext>(),
        });
    }

    // Maybe configure this per workbook.
    var ret = builder.Build(missingDaysFiller:
        attendanceConfig.MissingDaysFiller ?? Attendance.Present);
    return ret;
}

async ValueTask<ILessonTopics> GetLessonTopicsOfCurrentTeacher(IServiceProvider sp)
{
    var filteredSchedule = sp.ScopedSchedule();
    var builder = new AllLessonTopicsDatabaseBuilder(filteredSchedule);
    var config1 = sp.GetRequiredService<ConfigProvider>().Get(LessonTopicsConfig.Key);
    foreach (var x in config1.Sources)
    {
        var source = x.Create(sp);
        await source.Configure(builder, cancellationToken);
    }
    foreach (var x in config1.FallbackProviders)
    {
        builder.FallbackProvider(x.LessonType, x.Provider);
    }
    var topics = builder.Build();
    return topics;
}

async ValueTask<RegistryScrapingContext> MakeRegistryContext(IServiceProvider sp)
{
    var credentialsResolver = sp.GetRequiredService<CredentialsResolver<BuiltRegistryConfig>>();
    var credentials = credentialsResolver.Get();
    var registryContext = await RegistryScrapingContext.Create(
        credentials: credentials,
        cancellationToken: cancellationToken);
    return registryContext;
}

async ValueTask<MoodleScrapingContext> MakeMoodleContext(IServiceProvider sp)
{
    var credentialsResolver = sp.GetRequiredService<CredentialsResolver<MoodleConfig>>();
    var credentials = credentialsResolver.Get();
    var registryContext = await MoodleScrapingContext.Create(
        credentials: credentials,
        cancellationToken: cancellationToken);
    return registryContext;
}

Semester GetCurrentSemester(IServiceProvider sp)
{
    return sp.GetRequiredService<IOptions<StudyYearOptions>>().Value.Semester;
}
