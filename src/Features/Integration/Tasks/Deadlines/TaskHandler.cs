using System.Text;
using AutoConstructor.Attributes;
using ClosedXML.Excel;
using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Application.Core;

using Option = Config.Option;

public sealed class DeadlinesExcelConfig : IConfig<DeadlinesExcelConfig>
{
    public static LayerConfigKey<DeadlinesExcelConfig> Key { get; } = LayerConfigKey.Registry.Register<DeadlinesExcelConfig>();

    public System.Drawing.Color? GoodColor { get; set; }
    public System.Drawing.Color? BadColor { get; set; }
    public int LessonDelayLimit { get; set; } = -1;
    public int MaxTaskRows { get; set; } = -1;
    public float ColumnWidth { get; set; } = float.NegativeInfinity;

    public static void Register(IServiceCollection services)
    {
        services.AddMapper<DeadlinesConfigMapper>();
        services.AddMerger<DeadlinesExcelConfigMerger>();
        services.RegisterBasicOperationsAndMergers<DeadlinesExcelConfig>();
        services.AddConfigProvider(DeadlinesExcelBuiltConfig.Key);
    }
}

public sealed class DeadlinesExcelBuiltConfig
{
    public static LayerConfigKey<DeadlinesExcelBuiltConfig> Key => new(DeadlinesExcelConfig.Key.Value);

    public required System.Drawing.Color GoodColor { get; init; }
    public required System.Drawing.Color BadColor { get; init; }
    public required int LessonDelayLimit { get; init; }
    public required int MaxTaskRows { get; init; }
    public required float ColumnWidth { get; init; }
}

[AutoConstructor]
public sealed partial class GenerateDeadlinesExcelTaskHandler
{
    private readonly ScheduledDateTimeProvider _dateTimeProvider;
    private readonly CurrentTeacherIdProvider _idProvider;
    private readonly Schedule _schedule;
    private readonly IOptions<StudyYearOptions> _studyYear;
    private readonly ConfigProvider<DeadlinesExcelBuiltConfig> _deadlinesExcelConfigProvider;
    private readonly LabsMappingProvider _labsProvider;

    public readonly struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public required Stream OutputStream { get; init; }
    }

    public async ValueTask Run(RunParams p)
    {
        var deadlinesExcelConfig = _deadlinesExcelConfigProvider.Get();
        if (deadlinesExcelConfig is null)
        {
            throw new InvalidOperationException("Can't use the deadlines feature unless it's been configured.");
        }

        var labsValue = await _labsProvider.Get();
        var schedule = _schedule.Filter(
            FilterHelper.Builder()
                .WithTeacher(_idProvider.Get())
                .WithLessonType(LessonType.Lab));

        using var workbook = new XLWorkbook();

        foreach (var group in schedule.Groups)
        {
            var lessonsBySubgroup = schedule
                .EnumerateLessons()
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
                var scheduledLessons = _dateTimeProvider.GetSorted(new()
                {
                    Lessons = l.Select(x => x.Id),
                    Semester = _studyYear.Value.Semester,
                }).ToArray();
                if (scheduledLessons.Length == 0)
                {
                    continue;
                }

                var labs = labsValue.Get(key.Course);
                bool created = false;
                if (labs != null)
                {
                    foreach (var m in labs)
                    {
                        if (m.Option.Language != _schedule.Get(group).Language)
                        {
                            continue;
                        }
                        var option = m.Option;
                        var tasks = m.Tasks;
                        DoLabs(option, tasks);
                        created = true;
                    }
                }
                if (!created)
                {
                    DoLabs(option: default, tasks: null);
                }
                continue;


                void DoLabs(Option option, List<LabTask>? tasks)
                {
                    string sheetName;
                    {
                        var sb = new StringBuilder();
                        var s = _schedule;
                        var shortName = s.Get(key.Course).Names[^1];
                        var groupName = s.Get(group).Name;
                        sb.Append($"{shortName} - {groupName}");
                        if (key.SubGroup != SubGroup.All)
                        {
                            sb.Append($"({key.SubGroup.Value})");
                        }
                        if (option != default)
                        {
                            var lb = new ListStringBuilder(sb, "|");
                            if (option.Type != null)
                            {
                                lb.Append(option.Type);
                            }
                            if (option.Language is { } lang)
                            {
                                lb.Append($"{lang}");
                            }
                        }
                        sheetName = sb.ToString();
                    }
                    if (workbook.Worksheets.FirstOrDefault(x => x.Name == sheetName) is not { } worksheet)
                    {
                        worksheet = workbook.Worksheets.Add(sheetName);
                    }

                    int maxRows = tasks?.Count ?? deadlinesExcelConfig.MaxTaskRows;
                    const int labCols = 1;
                    const int firstRowPos = 1;
                    const int firstColPos = labCols + 1;
                    const int secondRowPos = firstRowPos + 1;
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
                        firstCellRow: secondRowPos,
                        firstCellColumn: firstColPos,
                        lastCellRow: secondRowPos + maxRows - 1,
                        lastCellColumn: maxCols);

                    worksheet.ConditionalFormats.RemoveAll();
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
                        if (cell.Value.IsBlank)
                        {
                            string formula = $"""=IF(AND({leftCellRef}<>"",{leftCellRef}<=0,{leftCellRef}>{-deadlinesExcelConfig.LessonDelayLimit}),{leftCellRef}-1,"")""";
                            cell.FormulaA1 = formula;
                        }
                    }

                    if (tasks != null)
                    {
                        var labsRange = worksheet.Range(
                            firstCellRow: secondRowPos,
                            firstCellColumn: labCols,
                            lastCellRow: secondRowPos + maxRows - 1,
                            lastCellColumn: labCols);
                        foreach (var x in labsRange.Cells().WithIndex())
                        {
                            var t = tasks[x.Index];
                            x.Item.SetValue(t.Name);
                            if (t.Url is { } url)
                            {
                                x.Item.SetHyperlink(new XLHyperlink(url));
                            }
                        }
                    }
                }
            }
        }

        workbook.SaveAs(p.OutputStream);
    }
}
