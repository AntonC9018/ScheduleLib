using ScheduleLib.Builders;
using ScheduleLib.Helper.Parsing;
using System.Globalization;
using System.Text.RegularExpressions;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Import.Pdf;

public delegate void ImportedLessonHandler(ImportedLessonInput input);

public static class PdfScheduleImporter
{
    private static readonly Regex Group = new(@"^(?:DU-)?[A-Z]+\d{4}\((?:ro|ru|en)\)$");
    private static readonly Regex Time = new(@"^[IV]+\s+(\d{1,2}:\d{2})-\d{1,2}:\d{2}$");

    public static IReadOnlyList<PdfScheduleCell> Read(Stream pdf, string source)
    {
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.Import.Pdf.PdfScheduleTable> tables =
            PdfTableReader.Read(pdf);
        string source2 = source;
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.Import.Pdf.PdfScheduleCell> resolve =
            Resolve(tables, source2);
        return resolve;
    }

    public static void Import(
        ScheduleImportContext context,
        Stream pdf,
        string source,
        ImportedLessonHandler? addLesson = null)
    {
        global::ScheduleLib.Parsing.ScheduleImportContext context2 = context;
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.Import.Pdf.PdfScheduleCell> cells =
            Read(pdf, source);
        global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Import.Pdf.PdfScheduleCell> cells2 = cells;
        global::ScheduleLib.Import.Pdf.ImportedLessonHandler? addLesson2 = addLesson;
        Import(context2,
            cells2,
            addLesson2);
    }

    public static IReadOnlyList<PdfScheduleCell> Resolve(IReadOnlyList<PdfScheduleTable> tables, string source)
    {
        var result = new List<PdfScheduleCell>();
        (string Name, double Center)[] columns = [];
        var dayParser = new DayNameParser(new());
        foreach (var table in tables)
        {
            var xs = table.Cells.SelectMany(c => new[]
                {
                    c.Bounds.Left,
                    c.Bounds.Right,
                })
                .Distinct()
                .Order()
                .ToArray();
            if (xs.Length < 4)
            {
                throw new PdfScheduleColumnsMissingException(source, table.Page);
            }

            var left = xs[2];
            var right = xs[^1];
            var headers = table.Cells.Where(c => Group.IsMatch(c.Text))
                .OrderBy(c => c.Bounds.Left)
                .ToArray();
            if (headers.Length > 0)
            {
                (string Name, double Center) ProjectValue(global::ScheduleLib.Import.Pdf.PdfTableCell c)
                {
                    string operationValue = c.Text;
                    double operationValue2 = c.Bounds.CenterX - left;
                    double operationValue3 = (operationValue2);
                    double operationValue4 = right - left;
                    double operationValue5 = operationValue3 / (operationValue4);
                    return (Name: operationValue, Center: operationValue5);
                }

                columns = headers.Select(ProjectValue)
                    .ToArray();
            }
            if (columns.Length == 0)
            {
                throw new PdfScheduleGroupHeaderMissingException(source, table.Page);
            }

            var days = table.Cells.Where(c => c.Bounds.Left == xs[0] && c.Text.Length > 0)
                .ToArray();
            var times = table.Cells.Where(c => c.Bounds.Left == xs[1] && c.Text.Length > 0)
                .ToArray();
            foreach (var day in days)
            {
                if (dayParser.Map(day.Text) is null)
                {
                    throw new UnknownPdfScheduleDayException(source: source,
                        table.Page,
                        dayText: day.Text);
                }
            }

            foreach (var time in times)
            {
                if (!Time.IsMatch(time.Text))
                {
                    throw new UnknownPdfScheduleTimeException(source: source,
                        table.Page,
                        timeText: time.Text);
                }
            }

            foreach (var cell in table.Cells)
            {
                var box = cell.Bounds;

                bool CheckCondition()
                {
                    if (box.Left < left)
                    {
                        return true;
                    }

                    if (cell.Text.Length == 0)
                    {
                        return true;
                    }

                    if (Group.IsMatch(cell.Text))
                    {
                        return true;
                    }

                    return false;
                }

                if (CheckCondition())
                {
                    continue;
                }

                var day = days.Where(c => c.Bounds.Top <= box.CenterY && box.CenterY < c.Bounds.Bottom)
                    .ToArray();
                var time = times.Where(c => c.Bounds.Top <= box.CenterY && box.CenterY < c.Bounds.Bottom)
                    .ToArray();
                var groups = columns.Where(c =>
                        box.Left <= left + c.Center * (right - left) && left + c.Center * (right - left) < box.Right)
                    .Select(c => c.Name)
                    .ToArray();

                bool CheckCondition2()
                {
                    if (day.Length != 1)
                    {
                        return true;
                    }

                    if (time.Length != 1)
                    {
                        return true;
                    }

                    if (groups.Length == 0)
                    {
                        return true;
                    }

                    return false;
                }

                if (CheckCondition2())
                {
                    throw new UnmappedPdfScheduleCellException(source: source,
                        table.Page,
                        box,
                        text: cell.Text);
                }

                string source2 = source;
                int page = table.Page;
                string[] groups2 = groups;
                string day2 = day[0].Text;
                global::System.Text.RegularExpressions.Match match = Time.Match(time[0].Text);
                global::System.Text.RegularExpressions.GroupCollection groupCollection = match.Groups;
                int groupnum = 1;
                string start = groupCollection[groupnum].Value;
                string text = PdfTextNormalizer.Normalize(cell.Text);
                string rawText = cell.Text;
                global::ScheduleLib.Import.Pdf.PdfBox bounds = box;
                var resolved = new PdfScheduleCell(Source: source2,
                    page,
                    groups2,
                    Day: day2,
                    Start: start,
                    Text: text,
                    RawText: rawText,
                    Bounds: bounds);
                global::System.Collections.Generic.List<global::ScheduleLib.Import.Pdf.PdfScheduleCell> list = result;
                global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Import.Pdf.PdfScheduleCell>
                    expandCohorts = PdfTextNormalizer.ExpandCohorts(resolved);
                global::System.Func<global::ScheduleLib.Import.Pdf.PdfScheduleCell,
                    global::ScheduleLib.Import.Pdf.PdfScheduleCell> selector = PdfTextNormalizer.RepairMissingParity;
                global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Import.Pdf.PdfScheduleCell>
                    collection = expandCohorts.Select(selector);
                list.AddRange(collection);
            }
        }
        return result;
    }

    public static void Import(
        ScheduleImportContext context,
        IEnumerable<PdfScheduleCell> cells,
        ImportedLessonHandler? addLesson = null)
    {
        var failures = new List<Exception>();
        foreach (var cell in cells)
        {
            try
            {
                var day = context.DayNameParser.Map(cell.Day)
                    ?? throw new UnknownImportedPdfScheduleDayException(cell.Day);
                var time = context.TimeConfig.FindTimeSlotByStartTime(TimeOnly.Parse(cell.Start,
                        CultureInfo.InvariantCulture))
                    ?? throw new UnknownImportedPdfScheduleStartTimeException(cell.Start);
                foreach (var parsed in context.ParseLessons(cell.Text.Split('\n')
                             .Select(l => l.AsMemory())))
                {
                    var lesson = parsed;
                    var names = cell.Groups;
                    var namedGroup = names.FirstOrDefault(g => g.Split('(')[0] == lesson.PartitionHint.ToString());
                    if (namedGroup is not null)
                    {
                        names = [namedGroup];
                        lesson.PartitionHint = default;
                    }
                    var groups = names.Select(g => context.Schedule.Group(g).Id)
                        .ToArray();
                    global::ScheduleLib.Parsing.Lesson.ParsedLesson lesson2 = lesson;
                    global::System.DayOfWeek day2 = day;
                    global::TimeSlot timeSlot = time;
                    global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.GroupId> groups2 = groups;
                    global::ScheduleLib.Alternative alternative = new(cell.Alternative);
                    var input = new ImportedLessonInput(lesson2,
                        day2,
                        timeSlot,
                        groups2,
                        alternative);
                    if (addLesson is null)
                    {
                        context.AddOrMergeLesson(input);
                    }
                    else
                    {
                        addLesson(input);
                    }
                }
            }
            catch (Exception e) when (e is FormatException
                                          or ScheduleBuildException
                                          or NotSupportedException
                                          or WrongFormatException)
            {
                var failure = new PdfScheduleCellImportException(cell, e);
                failures.Add(failure);
            }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("PDF lesson import failed.", failures);
        }
    }
}
