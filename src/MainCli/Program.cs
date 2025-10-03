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

const Session semester = Session.Ses1;
{
    const int year = 2025;
    context.Schedule.SetStudyYear(year);

    string dirName = @$"data\{year}_sem{(int) semester}";
    _ = dirName;
    // await Tasks.ParseDocumentDirIntoSchedule(
    //     context,
    //     dirName,
    //     cancellationToken: cancellationToken);
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
// if (Directory.Exists(outputDirectory))
// {
//     Directory.Delete(outputDirectory, recursive: true);
// }

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
        string[] scopes = [
            DriveService.Scope.DriveFile,
            DriveService.Scope.Drive,
        ];
        var credPath = "google_token_store";

        var clientSecrets = config.GetSection("Google").Get<ClientSecrets>();
        if (clientSecrets is null
            || clientSecrets.ClientId == null
            || clientSecrets.ClientSecret == null)
        {
            throw new InvalidOperationException("Configuration for google is missing");
        }

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets: clientSecrets,
            scopes: scopes,
            user: "user",
            taskCancellationToken: CancellationToken.None,
            dataStore: new FileDataStore(credPath, fullPath: true));

        using var driveService = new DriveService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "ScheduleLib",
            });
        _ = driveService;

        var folderId = await driveService.FindFolderId("orar", cancellationToken);
        var files = await driveService.GetFiles(folderId, cancellationToken);

        var comparer = StringComparer.OrdinalIgnoreCase;
        var existingLocalFiles = Directory.EnumerateFiles(outputDirectory)
            .Select(x => Path.GetFileName(x))
            .ToHashSet(comparer);
        var existingCloudFiles = files.Select(x => x.Name).ToHashSet(comparer);
        var cloudFilesToDelete = new List<BasicDriveFile>();
        var cloudFilesToUpdate = new List<BasicDriveFile>();
        var cloudFilesToCreate = new List<string>();
        foreach (var file in files)
        {
            if (existingLocalFiles.Contains(file.Name))
            {
                cloudFilesToUpdate.Add(file);
            }
            else
            {
                cloudFilesToDelete.Add(file);
            }
        }
        foreach (var local in existingLocalFiles)
        {
            if (!existingCloudFiles.Contains(local))
            {
                cloudFilesToCreate.Add(local);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var batchDeleteOperation = DriveApiHelper.ExecuteBatchDeleteAsync(
            driveService,
            cloudFilesToDelete,
            cts.Token);
        var taskBuilder = ArrayBuilder.Create<Task>(
            cloudFilesToCreate.Count
            + cloudFilesToUpdate.Count
            + batchDeleteOperation.BatchCount);
        try
        {
            foreach (var deleteTask in batchDeleteOperation.Tasks)
            {
                taskBuilder.Add(deleteTask);
            }
            foreach (var fileName in cloudFilesToCreate)
            {
                var t = driveService.UploadFile(
                    inputFilePath: Path.Combine(outputDirectory, fileName),
                    outputFileName: fileName,
                    folderId: folderId,
                    cancellationToken: cts.Token);
                taskBuilder.Add(t);
            }
            foreach (var file in cloudFilesToUpdate)
            {
                var t = driveService.UpdateFile(
                    fileInputPath: Path.Combine(outputDirectory, file.Name),
                    fileId: file.Id,
                    cancellationToken: cts.Token);
                taskBuilder.Add(t);
            }
            await Task.WhenAll(taskBuilder.Complete());
        }
        catch (Exception)
        {
            cts.Cancel();
            throw;
        }

        continue;
    }
    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.AllTeachersExcel:
    {
        var filteredSchedule = schedule.Filter(new()
        {
            PeriodFilter = new()
            {
                PeriodId = new(schedule.Periods.Length - 1),
                UnspecifiedIsAll = true,
            },
        });

        const string allTeachersOutputFile = "all_teachers_orar.xlsx";
        string allTeachersOutputFileFullPath = Path.GetFullPath($"{outputDirectory}/{allTeachersOutputFile}");

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

        ExplorerHelper.TryOpenExplorerAndSelectFile(allTeachersOutputFileFullPath);
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
            OutputPath = outputDirectory,
        });
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
            OutputPath = $"{outputDirectory}/free_rooms.xlsx",
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

                        var listBuilder = new ListStringBuilder(sb, ",");
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
