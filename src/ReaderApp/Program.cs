using System.Diagnostics;
using ScheduleLib;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReaderApp;
using ScheduleLib.Parsing;

var dayNameProvider = new DayNameProvider();
var context = DocParseContext.Create(new()
{
    DayNameProvider = dayNameProvider,
    CourseNameParserConfig = new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#"],
        IgnoredFullWords = ["p/u", "pentru"],
        IgnoredShortenedWords = ["Opț"],
        MinUsefulWordLength = 3,
    }),
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


#if false
var schedule = context.BuildSchedule();
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
}
#endif

