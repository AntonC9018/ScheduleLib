using ScheduleLib;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.Word;
using DocumentFormat.OpenXml.Packaging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

var context = DocParseContext.Create(new()
{
    DayNameProvider = new(),
    CourseParseContext = CourseParseContext.Create(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#"],
        IgnoredFullWords = ["p/u", "pentru"],
        IgnoredShortenedWords = ["Opț"],
        MinUsefulWordLength = 3,
    }),
});
{
    context.Schedule.SetStudyYear(2024);

    const string fileName = "Orar_An_II Lic.docx";
    var fullPath = Path.GetFullPath(fileName);
    using var document = WordprocessingDocument.Open(fullPath, isEditable: false);

    WordScheduleParser.ParseToSchedule(new()
    {
        Context = context,
        Document = document,
    });
}

{
    var schedule = context.BuildSchedule();

    var filteredSchedule = schedule.Filter(new()
    {
        Grade = 2,
        QualificationType = QualificationType.Licenta,
    });
    var dayNameProvider = new DayNameProvider();
    var timeSlotDisplayHandler = new TimeSlotDisplayHandler();
    var generator = new ScheduleTableDocument(filteredSchedule, new()
    {
        DayNameProvider = dayNameProvider,
        LessonTimeConfig = context.TimeConfig,
        ParityDisplay = new(),
        StringBuilder = new(),
        LessonTypeDisplay = new(),
        TimeSlotDisplay = timeSlotDisplayHandler,
        SubGroupNumberDisplay = new(),
    });

    QuestPDF.Settings.License = LicenseType.Community;
    QuestPDF.Settings.EnableDebugging = true;
    generator.GeneratePdfAndShow();
}
