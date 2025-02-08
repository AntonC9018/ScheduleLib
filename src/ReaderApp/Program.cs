using System.Reflection;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using DocumentFormat.OpenXml.Packaging;
using ReaderApp;
using ReaderApp.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib;
using ScheduleLib.Builders;

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
    teach.Add("Băț", "Beț");
    teach.Add("Spincean", "Sprîncean");
    teach.Add("Anghelova", "Anghelov");
});

{
    // Register the teachers from the list.
    const string fileName = @"data\Cadre didactice DI 2024-2025.xlsx";
    using var excel = SpreadsheetDocument.Open(fileName, isEditable: false, new()
    {
        AutoSave = false,
        CompatibilityLevel = CompatibilityLevel.Version_2_20,
    });

    ExcelTeacherListParser.AddTeachersFromExcel(new()
    {
        Excel = excel,
        Schedule = context.Schedule,
    });
}
{
    context.Schedule.SetStudyYear(2024);

    const string dirName = @"data\2024_sem2";
    foreach (var filePath in Directory.EnumerateFiles(dirName, "*.docx", SearchOption.TopDirectoryOnly))
    {
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        WordScheduleParser.ParseToSchedule(new()
        {
            Context = context,
            Document = document,
        });
    }
}

var schedule = context.BuildSchedule();
Console.WriteLine("Schedule built");

var option = Option.CreateLessonsInRegistry;

var cancellationToken = CancellationToken.None;
_ = cancellationToken;

switch (option)
{
    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.AllTeachersExcel:
    {
        const string outputFile = "all_teachers_orar.xlsx";
        string outputFileFullPath = Path.GetFullPath(outputFile);

        var timeConfig = new DefaultLessonTimeConfig(context.TimeConfig);

        Tasks.GenerateAllTeacherExcel(new()
        {
            DayNameProvider = new DayNameProvider(),
            StringBuilder = new(),
            LessonTypeDisplay = new(),
            ParityDisplay = new(),
            TimeSlotDisplay = new(),
            SeminarDate = (DayOfWeek.Wednesday, timeConfig.T15_00),
            OutputFilePath = outputFileFullPath,
            Schedule = schedule,
            TimeConfig = context.TimeConfig,
        });

        ExplorerHelper.OpenFolderAndSelectFile(outputFileFullPath);
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
        var dateProvider = Tasks.CreateDateProviderFromWeekParityExcel(new()
        {
            InputPath = @"data\Paritate.docx",
        });
        var credentials = Tasks.GetCredentials();
        await RegistryScraping.AddLessonsToOnlineRegistry(new()
        {
            CancellationToken = cancellationToken,
            Credentials = credentials,
            Schedule = schedule,
            Session = Session.Ses2,
            ErrorHandler = new RegistryErrorLogger(),
            CourseNameUnifier = context.CourseNameUnifierModule,
            GroupParseContext = context.Schedule.GroupParseContext!,
            LookupModule = context.Schedule.LookupModule!,
            DateProvider = dateProvider,
            TimeConfig = context.TimeConfig,
        });
        break;
    }
}


