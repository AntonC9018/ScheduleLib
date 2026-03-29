using System.Text;
using FmiWebsiteInterop.Schedule;
using FmiWebsiteInterop.Teachers;
using FmiWebsiteInterop.Theses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Curriculum.Download;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

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
    ListOfThesesPerTeacherForWebsite,
    CreatePredzashitaExcels,
    Query,
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
                var x = c.Services.GetRequiredService<AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler>();
                await x.Execute(c.CancellationToken);
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
                var outputDir = c.OutputDirectory.CreateSubDir("orar");
                var slugLookup = await c.Services.GetRequiredService<ISlugProvider>().SlugMap(cancellationToken: c.CancellationToken);
                foreach (var (teacher, filteredSchedule) in grouping.Filter(schedule))
                {
                    var name = new Name(teacher.Item.PersonName.AsNameFields());
                    if (!slugLookup.TryGetValue(name, out var slug))
                    {
                        Console.WriteLine($"No slug for '{name}'.");
                        continue;
                    }

                    var model = WebsiteJsonScheduleHelper.CreateSerializationModel(
                        filteredSchedule,
                        services1);
                    if (model.ScheduleDaysDto.IsEmpty)
                    {
                        continue;
                    }
                    var fileName = $"{slug}.json";
                    await using var outputFile = outputDir.OpenFile(fileName, FileMode.Create, FileAccess.Write);
                    await WebsiteJsonScheduleHelper.Serialize(model, outputFile);
                }

                var zipPath = await outputDir.Zip();
                ExplorerHelper.TryOpenExplorerAndSelectFile(zipPath);

                break;
            }

            case AppTask.CopyGradesFromMoodleToRegistry:
            {
                using var registryContext = await c.Services.MakeRegistryContext(c.CancellationToken);
                using var moodleContext = await c.Services.MakeMoodleContext(c.CancellationToken);
                var navigator = registryContext.Navigator(c.Services, c.CancellationToken);
                var handler = c.Services.GetRequiredService<CopyGradesFromMoodleForTestTaskHandler>();
                var semester = c.Services.GetCurrentSemester();

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

            case AppTask.ListOfThesesPerTeacherForWebsite:
            {
                var handler = c.Services.GetRequiredService<ThesesConversionTaskHandler>();
                var outputDir = c.OutputDirectory.CreateSubDir("theses");
                await handler.Handle(c.CancellationToken, outputDir);

                var zipPath = await outputDir.Zip();
                ExplorerHelper.TryOpenExplorerAndSelectFile(zipPath);

                break;
            }

            case AppTask.CreatePredzashitaExcels:
            {
                var handler = c.Services.GetRequiredService<ListsForPredzashitaTaskHandler>();
                await handler.Handle(c.OutputDirectory, c.CancellationToken);
                c.OutputDirectory.TryOpenInExplorer();
                break;
            }

            case AppTask.Query:
            {
                var schedule = c.Services.GetRequiredService<Schedule>();
                var filter = FilterHelper.Builder()
                    .WithLatestPeriod(schedule);
                var filteredSchedule = schedule.Filter(filter);

                var timeConfig = c.Services.GetRequiredService<LessonTimeConfig>();
                var startTimeSlot = timeConfig.FindTimeSlotByIncludedTime(new(hour: 17, minute: 00));
                var timeDisplay = c.Services.GetRequiredService<TimeSlotDisplayHandler>();

                foreach (var x in filteredSchedule.EnumerateWeeklyLessons())
                {
                    if (!x.Date.Parity.IsMatch(Parity.OddWeek))
                    {
                        continue;
                    }
                    if (x.Date.DayOfWeek != DayOfWeek.Tuesday)
                    {
                        continue;
                    }
                    if (x.Date.TimeSlot > startTimeSlot)
                    {
                        continue;
                    }
                    if (x.Lesson.Room.Id != "423/4")
                    {
                        continue;
                    }
                    var teacher = schedule.Get(x.Lesson.Teachers[0]);
                    var course = schedule.Get(x.Lesson.Course);
                    var timeInterval = timeConfig.GetTimeSlotInterval(x.Date.TimeSlot);
                    var time = timeDisplay.IntervalDisplay(timeInterval);
                    Console.WriteLine($"{teacher.PersonName}, {course.FullName}, {time}");
                }
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
