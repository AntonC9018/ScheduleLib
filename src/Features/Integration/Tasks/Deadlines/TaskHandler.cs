using System.Text;
using AutoConstructor.Attributes;
using ClosedXML.Excel;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Dates;

namespace ScheduleLib.Application.Core;

using Option = Config.Option;


[AutoConstructor]
public sealed partial class GenerateDeadlinesExcelTaskHandler
{
    private readonly ScheduledDateTimeProvider _dateTimeProvider;
    private readonly CurrentTeacherIdProvider _idProvider;
    private readonly Schedule _schedule;
    private readonly IOptions<StudyYearOptions> _studyYear;
    private readonly DataProvider<DeadlinesExcelBuiltConfig> _deadlinesExcelConfigProvider;
    private readonly LabsMappingProvider _labsProvider;

    public readonly struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public required Stream OutputStream { get; init; }
    }

    public async ValueTask Run(RunParams p)
    {
        p.CancellationToken.ThrowIfCancellationRequested();
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
            p.CancellationToken.ThrowIfCancellationRequested();
            var lessonsBySubgroup = schedule
                .EnumerateLessons()
                .Where(x => x.Lesson.Group == group)
                .GroupBy(x => (Partition: x.Lesson.GroupPartitionKey, Course: x.Lesson.Course))
                .ToArray();

            if (lessonsBySubgroup.GroupBy(x => x.Key.Course).Any(course => course.Count() > 1
                && course.Any(x => x.Key.Partition == GroupPartitionKey.All)))
            {
                throw new NotImplementedException("Shared labs not implemented");
            }

            foreach (var l in lessonsBySubgroup)
            {
                p.CancellationToken.ThrowIfCancellationRequested();
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
                        if (key.Partition.ToDisplayString() is { } partitionDisplay)
                        {
                            sb.Append($"({partitionDisplay})");
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

                    global::ClosedXML.Excel.IXLWorksheet iXLWorksheet = worksheet;
                    int firstCellRow2 = secondRowPos;
                    int firstCellColumn2 = firstColPos;
                    int int32 = secondRowPos + maxRows;
                    int lastCellRow2 = int32 - 1;
                    int lastCellColumn2 = maxCols;
                    var dataRange = iXLWorksheet.Range(
                        firstCellRow: firstCellRow2, firstCellColumn: firstCellColumn2, lastCellRow: lastCellRow2, lastCellColumn: lastCellColumn2);

                    worksheet.ConditionalFormats.RemoveAll();
                    for (int i = 0; i <= deadlinesExcelConfig.LessonDelayLimit; i++)
                    {
                        var gradientPos = (float) i / deadlinesExcelConfig.LessonDelayLimit;
                        var color = ColorHelper.Lerp(
                            a: deadlinesExcelConfig.GoodColor,
                            b: deadlinesExcelConfig.BadColor,
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
                        p.CancellationToken.ThrowIfCancellationRequested();
                        string leftCellRef = worksheet
                            .Cell(row: cell.Address.RowNumber, column: cell.Address.ColumnNumber - 1)
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
                        global::ClosedXML.Excel.IXLWorksheet iXLWorksheet2 = worksheet;
                        int firstCellRow3 = secondRowPos;
                        int firstCellColumn3 = labCols;
                        int int322 = secondRowPos + maxRows;
                        int lastCellRow3 = int322 - 1;
                        int lastCellColumn3 = labCols;
                        var labsRange = iXLWorksheet2.Range(
                            firstCellRow: firstCellRow3, firstCellColumn: firstCellColumn3, lastCellRow: lastCellRow3, lastCellColumn: lastCellColumn3);
                        foreach (var x in labsRange.Cells().WithIndex())
                        {
                            var t = tasks[x.Index];
                            x.Item.SetValue(t.Name);
                            if (t.Url is { } url)
                            {
                                global::ClosedXML.Excel.IXLCell iXLCell = x.Item;
                                global::ClosedXML.Excel.XLHyperlink hyperlink = new XLHyperlink(url);
                                iXLCell.SetHyperlink(hyperlink);
                            }
                        }
                    }
                }
            }
        }

        p.CancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(p.OutputStream);
    }
}
