using AutoConstructor.Attributes;
using ClosedXML.Excel;
using MainCli.BuilderNew;
using MainCli.BuilderNew.Retrieval;
using Microsoft.Extensions.Options;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;

namespace MainCli;

public sealed class DeadlinesExcelConfig : IConfig<DeadlinesExcelBuiltConfig>
{
    public static LayerConfigKey<DeadlinesExcelBuiltConfig> Key { get; } = LayerConfigKey.Registry.Register<DeadlinesExcelBuiltConfig>();

    public System.Drawing.Color? GoodColor { get; set; }
    public System.Drawing.Color? BadColor { get; set; }
    public int LessonDelayLimit { get; set; } = -1;
    public int MaxTaskRows { get; set; } = -1;
    public float ColumnWidth { get; set; } = float.NegativeInfinity;
}

public sealed class DeadlinesExcelBuiltConfig
{
    public required System.Drawing.Color GoodColor { get; init; }
    public required System.Drawing.Color BadColor { get; init; }
    public required int LessonDelayLimit { get; init; }
    public required int MaxTaskRows { get; init; }
    public required float ColumnWidth { get; init; }
}

[AutoConstructor]
public sealed partial class GenerateDeadlinesExcelHandler
{
    private readonly SemesterIntervalProvider _semesterIntervalProvider;
    private readonly IAllScheduledDateProvider _dateProvider;
    private readonly CurrentTeacherIdProvider _idProvider;
    private readonly Schedule _schedule;
    private readonly LessonTimeConfig _timeConfig;
    private readonly IOptions<StudyYearOptions> _studyYear;
    private readonly ConfigProvider<DeadlinesExcelConfig> _deadlinesExcelConfigProvider;

    public readonly struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public required Stream OutputStream { get; init; }
    }

    public ValueTask Run(RunParams p)
    {
        DeadlinesExcelBuiltConfig deadlinesExcelConfig;
        {
            var x = _deadlinesExcelConfigProvider.Get();
            deadlinesExcelConfig = new()
            {
                BadColor = x.BadColor!.Value,
                ColumnWidth = x.ColumnWidth,
                GoodColor = x.GoodColor!.Value,
                LessonDelayLimit = x.LessonDelayLimit,
                MaxTaskRows = x.MaxTaskRows,
            };
        }

        var schedule = _schedule.Filter(
            FilterHelper.Builder()
                .WithTeacher(_idProvider.Get())
                .WithLessonType(LessonType.Lab));

        using var workbook = new XLWorkbook();

        foreach (var group in schedule.Groups)
        {
            var lessonsBySubgroup = schedule.Lessons
                .Where(x => x.Lesson.Group == group)
                .GroupBy(x => (x.Lesson.SubGroup, x.Lesson.Course))
                .ToArray();

            if (lessonsBySubgroup.Length > 1
                && lessonsBySubgroup.Any(x => x.Key.SubGroup == SubGroup.All))
            {
                throw new NotImplementedException("Shared labs not implemented");
            }

            foreach (var l in lessonsBySubgroup)
            {
                var key = l.Key;
                var scheduledLessons = ScheduledLessonsHelper.GetSortedScheduledLessons(new()
                {
                    Lessons = l.Select(x => x.Id),
                    Schedule = _schedule,
                    DateProvider = _dateProvider,
                    Semester = _studyYear.Value.Semester,
                    TimeConfig = _timeConfig,
                    SemesterIntervalProvider = _semesterIntervalProvider,
                }).ToArray();
                if (scheduledLessons.Length == 0)
                {
                    continue;
                }

                string sheetName;
                {
                    var s = _schedule;
                    var shortName = s.Get(key.Course).Names[^1];
                    var groupName = s.Get(group).Name;
                    sheetName = $"{shortName} - {groupName}";
                    if (key.SubGroup != SubGroup.All)
                    {
                        sheetName = $"{sheetName}({key.SubGroup.Value})";
                    }
                }
                var worksheet = workbook.Worksheets.Add(sheetName);

                const int emptyCols = 1;
                const int firstRowPos = 1;
                const int firstColPos = emptyCols + 1;
                {
                    var firstRow = worksheet.Row(firstRowPos);
                    for (int index = 0; index < scheduledLessons.Length; index++)
                    {
                        int cellIndex = index + firstColPos;
                        var lesson = scheduledLessons[index];
                        var cell = firstRow.Cell(cellIndex);
                        var d = lesson.DateTime;
                        cell.Value = d.ToString("dd.MM");
                    }
                    for (int index = 0; index < scheduledLessons.Length; index++)
                    {
                        worksheet.Column(index + firstColPos).Width = deadlinesExcelConfig.ColumnWidth;
                    }
                }

                int maxCols = scheduledLessons.Length;

                var dataRange = worksheet.Range(
                    firstCellRow: firstRowPos + 1,
                    firstCellColumn: firstColPos,
                    lastCellRow: deadlinesExcelConfig.MaxTaskRows,
                    lastCellColumn: maxCols);

                for (int i = 0; i <= deadlinesExcelConfig.LessonDelayLimit; i++)
                {
                    var gradientPos = (float) i / deadlinesExcelConfig.LessonDelayLimit;
                    var color = ColorHelper.Lerp(
                        deadlinesExcelConfig.GoodColor,
                        deadlinesExcelConfig.BadColor,
                        gradientPos);
                    var xlColor = XLColor.FromColor(color);
                    var conditionalFormat = worksheet.AddConditionalFormat();
                    conditionalFormat.Range = dataRange;
                    conditionalFormat
                        .WhenEquals(-i)
                        .Fill
                        .SetBackgroundColor(xlColor);
                }

                foreach (var cell in dataRange.Cells())
                {
                    string leftCellRef = worksheet
                        .Cell(cell.Address.RowNumber, cell.Address.ColumnNumber - 1)
                        .Address
                        .ToStringRelative();
                    string formula = $"""=IF(AND({leftCellRef}<>"",{leftCellRef}<=0,{leftCellRef}>{-deadlinesExcelConfig.LessonDelayLimit}),{leftCellRef}-1,"")""";
                    cell.FormulaA1 = formula;
                }
            }
        }

        workbook.SaveAs(p.OutputStream);
        return ValueTask.CompletedTask;
    }
}
