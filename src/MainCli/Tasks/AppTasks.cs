using System.Text;
using Anton.LayeredConfig.Retrieval;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OnlineRegistry.AttendanceExcel;
using QuizModels;
using ScheduleLib.Application.Core.Config.Impl.Impl;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Application.Core.Topics;
using ScheduleLib.Builders;
using ScheduleLib.Curriculum.Download;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Scraping.Common.Config;
using WebsiteJsonSchedule;

namespace ScheduleLib.Application.Core;

public enum AppTask
{
    UploadDocsToDrive,
    AllTeachersExcel,
    PerGroupAndPerTeacherPdfs,
    CreateLessonsInRegistry,
    PullCurriculaFromOneDrive,
    FreeRooms,
    FreeHoursOfGroup,
    TableOfAllLabLessons,
    JsonSchedulesForWebsite,
    CopyGradesFromMoodleToRegistry,
    UpdateCalendar,
}

public static class AppTasks
{
    public static async Task ExecuteMenu(AppTasksExecutionContext context)
    {
        await context.RootServiceProvider.InitializeSchedule(context.CancellationToken);
        context.OutputDirectory.Initialize(clear: true);

        foreach (var option in context.SelectedOptions)
        {
            await using var scope = context.RootServiceProvider.CreateMarkerScope(x =>
            {
                x.TeacherName = context.TeacherName;
            });
            await ExecuteTask(new()
            {
                Context = context,
                AppTask = option,
                Services = scope.ServiceProvider,
            });
        }
    }

    public static async Task ExecuteTask(TaskExecutionContext c)
    {
        switch (c.AppTask)
        {
            case AppTask.UploadDocsToDrive:
            {
                Task[] tasks = [
                    GenerateAllTeacherExcel(c),
                    GenerateFreeRoomsExcel(c),
                    GeneratePdfsForGroupsAndTeachers(c),
                ];
                await Task.WhenAll(tasks);
                var handler = c.Services.GetRequiredService<SyncDriveFolderTaskHandler>();
                await handler.Run(new()
                {
                    FilesProvider = new OutputDirectoryFilesProvider(c.OutputDirectory),
                    CancellationToken = c.CancellationToken,
                });
                return;
            }
            case AppTask.AllTeachersExcel:
            {
                await GenerateAllTeacherExcel(c);
                c.AllTeachersFile.TryOpenInExplorer();
                break;
            }

            case AppTask.PerGroupAndPerTeacherPdfs:
            {
                await GeneratePdfsForGroupsAndTeachers(c);
                c.OutputDirectory.TryOpenInExplorer();
                break;
            }

            case AppTask.CreateLessonsInRegistry:
            {
                var attendance = GetAttendanceListOfCurrentTeacher(c);
                var topics = await GetLessonTopicsOfCurrentTeacher(c);

                // Passed manually, because this might be reconfigured to target another semester.
                var semester = GetCurrentSemester(c);
                var handler = c.Services.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>();

                using var registryContext = await MakeRegistryContext(c);
                var navigator = registryContext.Navigator(c.Services, c.CancellationToken);

                ILessonFilter LessonFilter()
                {
                    var b = c.Services
                        .LessonFilterBuilder()
                        .CurrentTeacher();

                    if (c.Services.GetRequiredService<ConfigProvider<RegistryLessonFilterConfig>>().Get() is { } filterConfig)
                    {
                        if (filterConfig.SkipAttendance is { } att)
                        {
                            b = b.SkipAttendance(att);
                        }
                    }
                    return b.Create();
                }

                await handler.Run(new()
                {
                    Navigator = navigator,
                    Attendance = attendance,
                    LessonTopics = topics,
                    Semester = semester,
                    LessonFilter = LessonFilter(),
                });
                break;
            }

            case AppTask.PullCurriculaFromOneDrive:
            {
                // TODO: Move to per-user config
                var conf = c.Services.GetRequiredService<IConfiguration>().GetMicrosoftGraphAuth();
                await CurriculaDownloadTasks.PullCurriculaToDisk(conf, c.CancellationToken);
                break;
            }

            case AppTask.FreeRooms:
            {
                await GenerateFreeRoomsExcel(c);
                c.FreeRoomsFile.TryOpenInExplorer();
                break;
            }

            case AppTask.FreeHoursOfGroup:
            {
                var sb = new StringBuilder();
                var handler = c.Services.GetRequiredService<PrintFreeHoursOfGroupTaskHandler>();
                handler.Run(new()
                {
                    Groups = [ "IA2401", "I2301" ],
                    StringBuilder = sb,
                });
                Console.WriteLine(sb.ToStringAndClear());
                break;
            }

            case AppTask.TableOfAllLabLessons:
            {
                var outputFile = c.OutputDirectory.File("deadlines.xlsx");
                await using var outputStream = outputFile.Open(FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);

                var handler = c.Services.GetRequiredService<GenerateDeadlinesExcelTaskHandler>();
                await handler.Run(new()
                {
                    CancellationToken = c.CancellationToken,
                    OutputStream = outputStream,
                });
                outputFile.TryOpenInExplorer();
                break;
            }

            case AppTask.JsonSchedulesForWebsite:
            {
                var services1 = new WebsiteJsonScheduleHelper.Services
                {
                    ParityDisplay = new(),
                    LessonTypeDisplay = new(),
                    SubGroupNumberDisplay = new(),
                };
                var schedule = c.Services.GetRequiredService<Schedule>();
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
                    await using var outputFile = c.OutputDirectory.OpenFile(fileName, FileMode.Create, FileAccess.Write);
                    await WebsiteJsonScheduleHelper.Serialize(model, outputFile);
                }
                c.OutputDirectory.TryOpenInExplorer();
                break;
            }

            case AppTask.CopyGradesFromMoodleToRegistry:
            {
                using var registryContext = await MakeRegistryContext(c);
                using var moodleContext = await MakeMoodleContext(c);
                var navigator = registryContext.Navigator(c.Services, c.CancellationToken);
                var handler = c.Services.GetRequiredService<CopyGradesFromMoodleForTestTaskHandler>();
                var semester = GetCurrentSemester(c);

                await handler.Run(new()
                {
                    RegistryNavigator = navigator,
                    CancellationToken = c.CancellationToken,
                    MoodleContext = moodleContext,
                    QuizId = c.MoodleQuizId,
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

            case AppTask.UpdateCalendar:
            {
                var handler = c.Services.GetRequiredService<UpdateLessonsInGoogleCalendarTaskHandler>();
                await handler.Update(c.CancellationToken);
                break;
            }
        }
    }

    public static Task GenerateFreeRoomsExcel(TaskExecutionContext c)
    {
        return Task.Run(async () =>
        {
            var handler = c.Services.GetRequiredService<GenerateFreeRoomsTaskHandler>();
            await using var outputStream = c.FreeRoomsFile.Open(FileMode.Create, FileAccess.Write);
            await handler.Run(new()
            {
                CancellationToken = c.CancellationToken,
                OutputStream = outputStream,
            });
        });
    }

    public static Task GeneratePdfsForGroupsAndTeachers(TaskExecutionContext c)
    {
        return Task.Run(async () =>
        {
            var handler = c.Services.GetRequiredService<GeneratePdfsForGroupsAndTeachersTaskHandler>();
            await handler.Run(new()
            {
                CancellationToken = c.CancellationToken,
                OutputDirectory = c.OutputDirectory,
            });
        });
    }

    public static Task GenerateAllTeacherExcel(TaskExecutionContext c)
    {
        return Task.Run(async () =>
        {
            var schedule = c.Services.LatestPeriodSchedule();
            var handler = c.Services.GetRequiredService<GenerateAllTeachersExcelTaskHandler>();
            await using var outputStream = c.AllTeachersFile.Open(FileMode.Create, FileAccess.ReadWrite);
            await handler.Run(new()
            {
                Schedule = schedule,
                CancellationToken = c.CancellationToken,
                OutputDirectory = outputStream,
            });
        });
    }

    public static StudentAttendanceList GetAttendanceListOfCurrentTeacher(TaskExecutionContext c)
    {
        var attendanceConfig = c.Services.GetRequiredService<ConfigProvider<LessonAttendanceConfig>>().Get();
        if (attendanceConfig is null)
        {
            return new([]);
        }

        var filteredSchedule = c.Services.ScopedSchedule();
        var builder = new AllStudentAttendanceListBuilder();
        foreach (var source in attendanceConfig.Sources)
        {
            if (source.FilePath == null)
            {
                throw new InvalidOperationException("Misconfigured source with a null path.");
            }
            using var stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            AttendanceExcel.ParseAttendanceListsExcel(new(
                lessonTypeParser: c.Services.GetRequiredService<LessonTypeParser>(),
                parseParameters: new()
                {
                    RepeatedCourseBehavior = source.RepeatedCourseBehavior ?? RepeatedCourseBehavior.Error,
                },
                builder: builder,
                schedule: filteredSchedule,
                workbook: workbook,
                lookup: c.Services.GetRequiredService<LookupFacade>(),
                groupParseContext: c.Services.GetRequiredService<GroupParseContext>()));
        }

        // Maybe configure this per workbook.
        var ret = builder.Build(missingDaysFiller:
            attendanceConfig.MissingDaysFiller ?? Attendance.Present);
        return ret;
    }

    public static async ValueTask<ILessonTopics> GetLessonTopicsOfCurrentTeacher(TaskExecutionContext c)
    {
        var config1 = c.Services.GetRequiredService<ConfigProvider>().Get(LessonTopicsConfig.Key);
        if (config1 == null)
        {
            return NoLessonTopics.Instance;
        }

        var filteredSchedule = c.Services.ScopedSchedule();
        var builder = new AllLessonTopicsDatabaseBuilder(filteredSchedule);
        foreach (var x in config1.Sources)
        {
            var source = x.Create(c.Services);
            await source.Configure(builder, c.CancellationToken);
        }
        foreach (var x in config1.FallbackProviders)
        {
            builder.FallbackProvider(x.LessonType, x.Provider);
        }
        var topics = builder.Build();
        return topics;
    }

    public static async ValueTask<RegistryScrapingContext> MakeRegistryContext(TaskExecutionContext c)
    {
        var credentialsResolver = c.Services.GetRequiredService<CredentialsResolver<BuiltRegistryConfig>>();
        var credentials = credentialsResolver.Get();
        var registryContext = await RegistryScrapingContext.Create(
            credentials: credentials,
            cancellationToken: c.CancellationToken);
        return registryContext;
    }

    public static async ValueTask<MoodleScrapingContext> MakeMoodleContext(TaskExecutionContext c)
    {
        var credentialsResolver = c.Services.GetRequiredService<CredentialsResolver<MoodleConfig>>();
        var credentials = credentialsResolver.Get();
        var registryContext = await MoodleScrapingContext.Create(
            credentials: credentials,
            cancellationToken: c.CancellationToken);
        return registryContext;
    }

    public static Semester GetCurrentSemester(TaskExecutionContext c)
    {
        return c.Services.GetRequiredService<IOptions<StudyYearOptions>>().Value.Semester;
    }
}

public sealed class AppTasksExecutionContext
{
    public required AppTask[] SelectedOptions { get; init; }
    public required OutputDirectory OutputDirectory { get; init; }
    public required string FreeRoomsExcelOutputFileName { get; init; }
    public required string AllTeachersOutputFileName { get; init; }
    public required Name TeacherName { get; init; }
    public required ServiceProvider RootServiceProvider { get; init; }
    public required CancellationToken CancellationToken { get; init; }
    public required string MoodleQuizId { get; set; }

    public FileInDirectory FreeRoomsFile => OutputDirectory.File(FreeRoomsExcelOutputFileName);
    public FileInDirectory AllTeachersFile => OutputDirectory.File(AllTeachersOutputFileName);
}

public readonly struct TaskExecutionContext
{
    public required AppTasksExecutionContext Context { get; init; }
    public required IServiceProvider Services { get; init; }
    public required AppTask AppTask { get; init; }
    public OutputDirectory OutputDirectory => Context.OutputDirectory;
    public FileInDirectory FreeRoomsFile => Context.FreeRoomsFile;
    public FileInDirectory AllTeachersFile => Context.AllTeachersFile;
    public CancellationToken CancellationToken => Context.CancellationToken;
    public string MoodleQuizId => Context.MoodleQuizId;
}
