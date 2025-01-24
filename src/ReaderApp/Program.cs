using ScheduleLib;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.Word;
using DocumentFormat.OpenXml.Packaging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

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

{
    var schedule = context.BuildSchedule();
    var services = new GroupColumnScheduleTableDocument.Services
    {
        LessonTimeConfig = context.TimeConfig,
        ParityDisplay = new(),
        LessonTypeDisplay = new(),
        TimeSlotDisplay = new(),
        SubGroupNumberDisplay = new(),
        DayNameProvider = dayNameProvider,

        // Initialized later.
        StringBuilder = null!,
    };

    var outputDirPath = "output";
    Directory.CreateDirectory(outputDirPath);
    foreach (var filePath in Directory.EnumerateFiles(outputDirPath, "*.pdf", SearchOption.TopDirectoryOnly))
    {
        File.Delete(filePath);
    }

    QuestPDF.Settings.License = LicenseType.Community;

    var tasks = new List<Task>();
    for (int groupId = 0; groupId < schedule.Groups.Length; groupId++)
    {
        int groupId1 = groupId;
        var t = Task.Run(() =>
        {
            var groupName = schedule.Groups[groupId1].Name;
            GenerateWithFilter(groupName, new()
            {
                GroupFilter = new()
                {
                    GroupIds = [new(groupId1)],
                },
            });
        });
        tasks.Add(t);
    }
    for (int teacherId = 0; teacherId < schedule.Teachers.Length; teacherId++)
    {
        int teacherId1 = teacherId;
        var t = Task.Run(() =>
        {
            var teacherName = schedule.Teachers[teacherId1].Name;
            var fileName = teacherName.Replace('.', '_');
            GenerateWithFilter(fileName, new()
            {
                TeacherFilter = new()
                {
                    IncludeIds = [new(teacherId1)],
                },
            });
        });
        tasks.Add(t);
    }
    await Task.WhenAll(tasks);

    void GenerateWithFilter(string name, in ScheduleFilter filter)
    {
        var filteredSchedule = schedule.Filter(filter);
        if (filteredSchedule.IsEmpty)
        {
            return;
        }

        var generator = new GroupColumnScheduleTableDocument(filteredSchedule, services with
        {
            StringBuilder = new(),
        });

        var path = Path.Combine(outputDirPath, name + ".pdf");
        generator.GeneratePdf(path);
    }
}

