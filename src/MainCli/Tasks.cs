using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Dom;
using AngleSharp.Dom;
using ClosedXML.Excel;
using ConvertDocToDocx;
using DocumentFormat.OpenXml.Packaging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using OpenHolidays;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using MainCli.Helper;
using QuizModels;
using ScheduleLib.OnlineRegistry;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Generation.TeacherCute;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Moodle;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.Scraping.Common;
using SpreadCheetah;

namespace MainCli;

public struct GeneratePdfForGroupsAndTeachersParams()
{
    public required PdfLessonTextDisplayHandler.Services LessonTextDisplayServices;
    public required LessonTimeConfig LessonTimeConfig;
    public required TimeSlotDisplayHandler TimeSlotDisplay;
    public required DayNameProvider DayNameProvider;
    public required Schedule Schedule;
    public required string OutputPath;
}

public struct AllTeacherExcelParams()
{
    public required TempOutputDirectoryService OutputDirectory;
    public required string OutputFilePath;
    public required DayNameProvider DayNameProvider;
    public required (DayOfWeek Day, TimeSlot TimeSlot) SeminarDate;
    public required StringBuilder StringBuilder;
    public required LessonTypeDisplayHandler LessonTypeDisplay;
    public required ParityDisplayHandler ParityDisplay;
    public required TimeSlotDisplayHandler TimeSlotDisplay;
    public required FilteredSchedule Schedule;
    public required LessonTimeConfig TimeConfig;
}


public struct ParseStudyWeekWordDocParams
{
    public required string InputPath;
    public required HolidayPeriod[] Holidays;
}

public static class Tasks
{
    public static ManualAllScheduledDateProvider CreateDateProviderFromWeekParityExcel(
        ParseStudyWeekWordDocParams p)
    {
        using var stream = File.OpenRead(p.InputPath);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);
        var studyWeeks = ParityExcelParser.Parse(word).ToArray();
        var ret = new ManualAllScheduledDateProvider(
            studyWeeks: studyWeeks,
            holidays: p.Holidays);
        return ret;
    }

    public static Credentials GetRegistryCredentials(
        IConfiguration configuration,
        bool allowUserInput)
    {
        var ret = configuration.MaybeGetCredentials(RegistryScraping.CredentialsConfigKey);
        if (ret != null)
        {
            return ret;
        }
        if (!allowUserInput)
        {
            throw new InvalidOperationException("Credentials not found.");
        }

        Console.WriteLine("No 'Registry' key specified in user secrets.");
        Console.WriteLine("https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0&tabs=windows#secret-manager");
        Console.WriteLine("You may input it manually for this session only:");

        Console.Write("Login: ");
        var login = Console.ReadLine() ?? throw new InvalidOperationException();

        Console.Write("Password: ");
        using var password = ReadPassword();

        ret = new()
        {
            Login = login,
            Password = password.ToString() ?? throw Unreachable(),
        };
        return ret;
    }

    private static SecureString ReadPassword()
    {
        var pwd = new SecureString();
        while (true)
        {
            ConsoleKeyInfo i = Console.ReadKey(intercept: true);
            if (i.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (i.Key == ConsoleKey.Backspace)
            {
                if (pwd.Length == 0)
                {
                    continue;
                }

                pwd.RemoveAt(pwd.Length - 1);
                Console.Write("\b \b");
                continue;
            }

            // the key pressed does not correspond to a printable character, e.g. F1, Pause-Break, etc
            if (i.KeyChar != '\u0000')
            {
                pwd.AppendChar(i.KeyChar);
                Console.Write("*");
                continue;
            }
        }
        return pwd;
    }

    public static void OptionallyEnrichContextWithTeacherFullNames(
        ScheduleBuilder schedule,
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        using var excel = SpreadsheetDocument.Open(filePath, isEditable: false, new()
        {
            AutoSave = false,
            CompatibilityLevel = CompatibilityLevel.Version_2_20,
        });

        ExcelTeacherListParser.AddTeachersFromExcel(new()
        {
            Excel = excel,
            Schedule = schedule,
        });
    }

    public static string GetDirectoryHash(
        string srcFullPath,
        string searchPattern = "*",
        bool hashPaths = true,
        bool hashContents = true)
    {
        Debug.Assert(srcFullPath == Path.GetFullPath(srcFullPath));

        var filePaths = Directory.GetFiles(
                srcFullPath,
                searchPattern: searchPattern,
                SearchOption.AllDirectories)
            .OrderBy(p => p)
            .ToArray();

        const int MaxPathBytes = 4096;
        const int BufferSize = 8192;
        using var pathBuffer = new RentedBuffer<byte>(MaxPathBytes);
        using var readBuffer = new RentedBuffer<byte>(BufferSize);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        foreach (var filePath in filePaths)
        {
            if (hashPaths)
            {
                var relativePath = filePath.AsSpan(srcFullPath.Length + 1);
                int byteCount = Encoding.UTF8.GetBytes(relativePath, pathBuffer.Span);
                hasher.AppendData(pathBuffer.Span[.. byteCount]);
            }

            if (hashContents)
            {
                using var fs = File.OpenRead(filePath);
                int read;
                while ((read = fs.Read(readBuffer.Span)) > 0)
                {
                    hasher.AppendData(readBuffer.Span[.. read]);
                }
            }
        }

        var hashLen = hasher.HashLengthInBytes;
        using var hash = new RentedBuffer<byte>(hashLen);
        int len = hasher.GetCurrentHash(hash.Span);
        Debug.Assert(len == hashLen);
        return Convert.ToHexStringLower(hash.Span);
    }

    public static async Task ParseDocumentDirIntoSchedule(
        DocParseContext context,
        string dirName,
        CancellationToken cancellationToken)
    {
        dirName = Path.GetFullPath(dirName);

        await ParseDirectoryToSchedule(
            context,
            dirName,
            cancellationToken: cancellationToken);

        var subdirs = Directory.EnumerateDirectories(dirName, "*", SearchOption.TopDirectoryOnly)
            .Select(x =>
            {
                var lastSegmentStart = x.LastIndexOf(Path.DirectorySeparatorChar);
                Debug.Assert(lastSegmentStart != -1);
                lastSegmentStart += 1;

                var lastSegment = x.AsSpan()[lastSegmentStart ..];

                if (!DateOnly.TryParseExact(
                        lastSegment,
                        format: "dd.MM.yy",
                        provider: null,
                        style: DateTimeStyles.None,
                        result: out var startDate))
                {
                    throw new InvalidOperationException($"The folders must be named in the format 'DD.MM.YYYY'. Found this: {x}");
                }
                return (SubDirPath: x, StartDate: startDate);
            })
            .OrderBy(x => x.StartDate);

        foreach (var t in subdirs)
        {
            await ParseDirectoryToSchedule(
                context,
                t.SubDirPath,
                cancellationToken: cancellationToken,
                period: new()
                {
                    StartDate = t.StartDate,
                });
        }
        return;

        static async Task ParseDirectoryToSchedule(
            DocParseContext context,
            string dirName,
            CancellationToken cancellationToken,
            PeriodBeginning? period = null)
        {
            foreach (var filePath in Directory.EnumerateFiles(dirName, "*.doc", SearchOption.TopDirectoryOnly))
            {
                var outputPath = PathHelper.WithExtension(filePath, ".docx");
                var conversionSuccessful = await DocToDocxConversionHelper.TryConvertFile(
                    inputPath: filePath,
                    outputPath: outputPath,
                    cancellationToken: cancellationToken);
                if (!conversionSuccessful)
                {
                    throw new InvalidOperationException("Could not convert doc to docx");
                }
                File.Delete(filePath);
            }

            foreach (var filePath in Directory.EnumerateFiles(dirName, "*.docx", SearchOption.TopDirectoryOnly))
            {
                using var document = WordprocessingDocument.Open(filePath, isEditable: false);
                context.SetPeriod(period);

                WordScheduleParser.ParseToSchedule(new()
                {
                    Context = context,
                    Document = document,
                });
            }
        }
    }

    // ReSharper disable once UnusedMember.Global
    public static async Task<HolidayPeriod[]> GetHolidayPeriodsFromApi(
        Schedule schedule,
        CancellationToken cancellationToken)
    {
        using var holidaysHttpClient = new HttpClient();
        var holidaysClient = new OpenHolidaysClient(holidaysHttpClient);
        var holidaysProvider = new HolidaysProvider(holidaysClient, new()
        {
            CountryIsoCode = "MD",
        });
        var wholePeriod = schedule.WholePeriod();
        var ret = await holidaysProvider.GetHolidayPeriods(new()
        {
            From = wholePeriod.Start,
            To = wholePeriod.EndExclusive,
            CancellationToken = cancellationToken,
        });
        return ret;
    }

    public struct GenerateFreeRoomsParams
    {
        public required string OutputPath;
        public required ParityDisplayHandler ParityDisplay;
        public required TimeSlotDisplayHandler TimeSlotDisplay;
        public required DayNameProvider DayNameProvider;
        public required CancellationToken CancellationToken;
        public required Schedule Schedule;
        public required LessonTimeConfig TimeConfig;
    }

    public struct UploadStuffToDriveParams
    {
        public required IConfiguration Configuration;
        public required TempOutputDirectoryService OutputDirectory;
        public required CancellationToken CancellationToken;
    }

    public static ClientSecrets GetDriveConfig(IConfiguration config)
    {
        var clientSecrets = config.GetSection("Google").Get<ClientSecrets>();
        if (clientSecrets is null
            || clientSecrets.ClientId == null
            || clientSecrets.ClientSecret == null)
        {
            throw new InvalidOperationException("Configuration for google is missing");
        }
        return clientSecrets;
    }

    public static async Task UploadStuffToDrive(UploadStuffToDriveParams p)
    {
        string[] scopes = [
            DriveService.Scope.DriveFile,
            DriveService.Scope.Drive,
        ];
        var credPath = "google_token_store";
        var clientSecrets = GetDriveConfig(p.Configuration);
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

        var folderId = await driveService.FindFolderId("orar", p.CancellationToken);
        var files = await driveService.GetFiles(folderId, p.CancellationToken);

        var comparer = StringComparer.OrdinalIgnoreCase;
        var existingLocalFiles = p.OutputDirectory
            .FilePaths("*", new()
            {
                RecurseSubdirectories = true,
            })
            .Select(x => x.Path)
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(p.CancellationToken);
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
            Stream File(string path)
            {
                var stream = p.OutputDirectory.File(path, FileMode.Open, FileAccess.Read);
                return stream;
            }
            foreach (var fileName in cloudFilesToCreate)
            {
                await using var stream = File(fileName);
                var t = driveService.UploadFile(
                    stream,
                    outputFileName: fileName,
                    folderId: folderId,
                    cancellationToken: cts.Token);
                taskBuilder.Add(t);
            }
            foreach (var file in cloudFilesToUpdate)
            {
                await using var stream = File(file.Name);
                var t = driveService.UpdateFile(
                    stream,
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
    }

    public struct PrintFreeHoursOfGroupParams
    {
        public required Schedule Schedule;
        public required DayNameProvider DayNameProvider;
        public required LessonTimeConfig TimeConfig;
        public required string[] Groups;
        public required StringBuilder StringBuilder;
    }

    public static void PrintFreeHoursOfGroup(PrintFreeHoursOfGroupParams p)
    {
        foreach (var parity in new[]{Parity.EvenWeek, Parity.OddWeek})
        {
            foreach (var group in p.Groups)
            {
                foreach (var isOptional in new[] { true, false })
                {
                    var displayHandler = new TimeSlotDisplayHandler();
                    var groupId = p.Schedule.Groups
                        .WithIndex()
                        .Where(x => x.Item.Name == group)
                        .Select(x => new GroupId(x.Index))
                        .Single();
                    var lessons = p.Schedule.RegularLessons
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
                            if (sg == SpecialSubGroups.Optional)
                            {
                                return true;
                            }
                            return false;
                        });

                    var allTimes = p.TimeConfig.TimeSlots
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
                    p.StringBuilder.AppendLine($"paritatea: {parityDisplay.Get(parity)}, grupa: {group}, optional?: {isOptional}");
                    foreach (var day in byDay)
                    {
                        p.StringBuilder.Append(p.DayNameProvider.GetDayName(day.Day));
                        p.StringBuilder.Append(":");

                        var listBuilder = new ListStringBuilder(p.StringBuilder, ",");
                        foreach (var time in day.Times)
                        {
                            var start = time.Start;
                            var end = time.EndInclusive;
                            var startTime = p.TimeConfig.GetTimeSlotInterval(start).Start;
                            var endTime = p.TimeConfig.GetTimeSlotInterval(end).End;
                            var duration = endTime - startTime;
                            var intervalStr = displayHandler.IntervalDisplay(new TimeSlotInterval(startTime, duration));
                            listBuilder.Append(intervalStr);
                        }
                        p.StringBuilder.AppendLine();
                    }
                    p.StringBuilder.AppendLine();
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
    }

    public static async Task LoadSchedule(
        DocParseContext context,
        string scheduleSourcesDir,
        string serializedSchedulePath,
        Action<DocParseContext> beforeEndAction,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        var scheduleSourcesDirFullPath = Path.GetFullPath(scheduleSourcesDir);

        async ValueTask<SerializationModels.ScheduleModel?> GetValidModel()
        {
            if (bypassCache)
            {
                return null;
            }
            if (!Path.Exists(serializedSchedulePath))
            {
                return null;
            }

            var filesHash = GetDirectoryHash(scheduleSourcesDirFullPath);

            await using var inputFile = File.OpenRead(serializedSchedulePath);
            var serializedModel = await ScheduleSerializer.Deserialize(inputFile, cancellationToken);
            if (serializedModel.Hash != filesHash)
            {
                return null;
            }

            return serializedModel;
        }

        if (await GetValidModel() is { } scheduleSerializedModel)
        {
            ScheduleSerializer.AddToBuilder(
                context.Schedule,
                scheduleSerializedModel,
                context.CourseNameUnifierModule);

            beforeEndAction(context);
            return;
        }

        {
            await ParseDocumentDirIntoSchedule(
                context,
                scheduleSourcesDirFullPath,
                cancellationToken: cancellationToken);

            beforeEndAction(context);

            var schedule = context.Schedule.Build();

            // I think word resaves them in some way.
            var newFilesHash = GetDirectoryHash(scheduleSourcesDirFullPath);
            await using var outputFile = new FileStream(serializedSchedulePath, FileMode.Create);
            await ScheduleSerializer.Serialize(schedule, outputFile, newFilesHash, cancellationToken);
            return;
        }
    }

    public readonly struct GenerateDeadlinesExcelParams()
    {
        public required FilteredSchedule Schedule { get; init; }
        public required IAllScheduledDateProvider DateProvider { get; init; }
        public required Semester Semester { get; init; }
        public required LessonTimeConfig TimeConfig { get; init; }
        public required SemesterIntervalProvider SemesterIntervalProvider { get; init; }
        public required string OutputFilePath { get; init; }
        public required System.Drawing.Color GoodColor { get; init; }
        public required System.Drawing.Color BadColor { get; init; }
        public required int LessonDelayLimit { get; init; }
        public required int MaxTaskRows { get; init; }
        public float ColumnWidth { get; init; } = 5;
    }

    public static void GenerateDeadlinesExcel(GenerateDeadlinesExcelParams p)
    {
    }

    public static async Task CopyGradesFromMoodleForTest(
        IConfiguration config,
        CourseNameUnifierModule courseNameUnifierModule,
        LookupModule lookupModule,
        Schedule schedule,
        GroupParseContext groupParseContext,
        Semester semester,
        string quizId,
        CancellationToken cancellationToken)
    {
        var registryCredentials = Tasks.GetRegistryCredentials(config, allowUserInput: false);
        var moodleCredentials = config.GetCredentials(MoodleInterop.CredentialsKey);

        using var registryContext = await RegistryScrapingContext.Create(registryCredentials, cancellationToken);
        using var moodleContext = await MoodleScrapingContext.Create(moodleCredentials, cancellationToken);

        var registryNav = registryContext.Navigator(
            new RegistryErrorLogger(),
            cancellationToken);
        var coursesNav = registryNav.Courses(
            courseNameUnifierModule,
            lookupModule);
        var groupsNav = registryNav.Groups(
            schedule,
            groupParseContext);

        var quiz = await moodleContext.ScrapeQuizAttempts(quizId);

        Dictionary<Name, float> gradeByName = new(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        foreach (var q in quiz.Attempts)
        {
            var parser = new Parser(q.UserName);
            var name = NameHelper.TryParseName(ref parser);
            if (name is null)
            {
                Console.WriteLine($"{q.UserName} not parsed as name.");
                continue;
            }

            // They go in different order on moodle.
            {
                var f = name.FirstName;
                var l = name.LastName;
                name.FirstName = l;
                name.LastName = f;
            }

            if (q.Grade is not { } grade1)
            {
                Console.WriteLine($"{q.UserName} not graded yet!");
                continue;
            }
            gradeByName[name] = grade1;
        }

        // determine course from path
        var parsedPath = MoodlePathParser.TryParse(quiz.Path.Select(x => x.Name));
        _ = parsedPath;
        if (parsedPath is null)
        {
            throw new InvalidOperationException("Could not parse path");
        }

        var courseId = courseNameUnifierModule.Find(new()
        {
            Lookup = lookupModule,
            CourseName = parsedPath.CourseName,
        });
        var grade = parsedPath.Grade;
        var qualificationType = parsedPath.QualificationType;

        foreach (var course in await coursesNav.Get(semester))
        {
            if (course.CourseId != courseId)
            {
                continue;
            }

            foreach (var group in await groupsNav.Get(course))
            {
                if (group.Groups.IsWildcard)
                {
                    throw new NotImplementedException();
                }
                var groupInfo = schedule.Get(group.Groups.Value[0]);
                if (groupInfo.QualificationType != qualificationType)
                {
                    continue;
                }
                if (groupInfo.Grade != grade)
                {
                    continue;
                }

                var evaluareDoc = await registryNav.GetHtml(group.EvaluationUri);

                // Find anchor with text Testarea X
                IHtmlAnchorElement TestAnchor()
                {
                    var tables = evaluareDoc.QuerySelectorAll<IHtmlAnchorElement>("table a");
                    var matching = tables.Where(x =>
                    {
                        var parser = new Parser(x.TextContent);
                        parser.SkipWhitespace();
                        if (!parser.ConsumeExactString("Testarea"))
                        {
                            return false;
                        }
                        if (!parser.SkipWhitespace().SkippedAny)
                        {
                            return false;
                        }
                        var bparser = parser.BufferedView();
                        if (!bparser.SkipNumbers().SkippedAny)
                        {
                            return false;
                        }

                        var numberSpan = parser.PeekSpanUntilPosition(bparser.Position);
                        var number = int.Parse(numberSpan);
                        if (parsedPath.TestNumber != number)
                        {
                            return false;
                        }

                        return true;
                    });
                    var header = matching.First();
                    return header;
                }

                var testUrl = TestAnchor();
                var test1Doc = await registryNav.GetHtml(new(testUrl.Href));
                var table = test1Doc.QuerySelector<IHtmlTableElement>("table")
                    ?? throw new InvalidOperationException("No table found");
                int nameColumnIndex = FindColumnIndex("Numele");
                int gradeColumnIndex = FindColumnIndex("Nota");

                for (int i = 1; i < table.Rows.Length; i++)
                {
                    var row = table.Rows[i];
                    var nameCell = row.Cells[nameColumnIndex];

                    Name name;
                    {
                        var nameParser = new Parser(nameCell.TextContent);
                        nameParser.SkipWhitespace();
                        name = NameHelper.ParseName(ref nameParser);
                        nameParser.SkipWhitespace();
                        if (nameParser.ConsumeExactString("exmatr"))
                        {
                            continue;
                        }
                        if (!nameParser.IsEmpty)
                        {
                            throw new InvalidOperationException("Extra text after name");
                        }
                    }

                    if (!gradeByName.Remove(name, out float gradeInDb))
                    {
                        Console.WriteLine($"No student in moodle: {name}");
                        continue;
                    }

                    var gradeRounded = (int) Math.Round(gradeInDb);

                    {
                        var gradeCell = row.Cells[gradeColumnIndex];
                        var input = gradeCell.QuerySelector<IHtmlInputElement>("""input[type="text"]""")
                            ?? throw new InvalidOperationException("No input found in grade cell");
                        input.Value = gradeRounded.ToString();
                    }
                }

                var form = test1Doc.QuerySelector<IHtmlFormElement>("form")
                    ?? throw new InvalidOperationException("No form found");
                _ = form;

                // var button = test1Doc.QuerySelector<IHtmlButtonElement>("form > div > div > button")
                //     ?? throw new InvalidOperationException("No submit button found");
                // await button.SubmitAsync();
                await form.SubmitAsync();
                continue;

                int FindColumnIndex(string name)
                {
                    return table.Rows[0].Cells.WithIndex().Where(x =>
                    {
                        var t = x.Item.TextContent.AsSpan().Trim();
                        return t.SequenceEqual(name);
                    }).Single().Index;
                }
            }
        }

        foreach (var (name, value) in gradeByName)
        {
            Console.WriteLine($"Student not found in registry: {name} ({value})");
        }
    }
}

public enum Option
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
}
