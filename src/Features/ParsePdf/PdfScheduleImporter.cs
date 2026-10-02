using ScheduleLib.Builders;
using ScheduleLib.Helper.Parsing;
using System.Globalization;
using System.Text.RegularExpressions;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Import.Pdf;

public delegate void ImportedLessonHandler(ParsedLesson lesson, DayOfWeek day, TimeSlot slot, IReadOnlyList<GroupId> groups, string? alternative);

public static class PdfScheduleImporter
{
    private static readonly Regex Group = new(@"^(?:DU-)?[A-Z]+\d{4}\((?:ro|ru|en)\)$");
    private static readonly Regex Time = new(@"^[IV]+\s+(\d{1,2}:\d{2})-\d{1,2}:\d{2}$");

    public static IReadOnlyList<PdfScheduleCell> Read(Stream pdf, string source) => Resolve(PdfTableReader.Read(pdf), source);

    public static void Import(ScheduleImportContext context, Stream pdf, string source,
        ImportedLessonHandler? addLesson = null) => Import(context, Read(pdf, source), addLesson);

    public static IReadOnlyList<PdfScheduleCell> Resolve(IReadOnlyList<PdfScheduleTable> tables, string source)
    {
        var result = new List<PdfScheduleCell>();
        (string Name, double Center)[] columns = [];
        var dayParser = new DayNameParser(new());
        foreach (var table in tables)
        {
            var xs = table.Cells.SelectMany(c => new[] { c.Bounds.Left, c.Bounds.Right }).Distinct().Order().ToArray();
            if (xs.Length < 4) throw new FormatException($"{source}, page {table.Page}: expected day, time and group columns.");
            var left = xs[2];
            var right = xs[^1];
            var headers = table.Cells.Where(c => Group.IsMatch(c.Text)).OrderBy(c => c.Bounds.Left).ToArray();
            if (headers.Length > 0)
                columns = headers.Select(c => (Name: c.Text, Center: (c.Bounds.CenterX - left) / (right - left))).ToArray();
            if (columns.Length == 0) throw new FormatException($"{source}, page {table.Page}: missing group header.");
            var days = table.Cells.Where(c => c.Bounds.Left == xs[0] && c.Text.Length > 0).ToArray();
            var times = table.Cells.Where(c => c.Bounds.Left == xs[1] && c.Text.Length > 0).ToArray();
            foreach (var day in days)
                if (dayParser.Map(day.Text) is null) throw new FormatException($"{source}, page {table.Page}: unknown day {day.Text}.");
            foreach (var time in times)
                if (!Time.IsMatch(time.Text)) throw new FormatException($"{source}, page {table.Page}: unknown time {time.Text}.");
            foreach (var cell in table.Cells)
            {
                var box = cell.Bounds;
                if (box.Left < left || cell.Text.Length == 0 || Group.IsMatch(cell.Text)) continue;
                var day = days.Where(c => c.Bounds.Top <= box.CenterY && box.CenterY < c.Bounds.Bottom).ToArray();
                var time = times.Where(c => c.Bounds.Top <= box.CenterY && box.CenterY < c.Bounds.Bottom).ToArray();
                var groups = columns.Where(c => box.Left <= left + c.Center * (right - left) && left + c.Center * (right - left) < box.Right).Select(c => c.Name).ToArray();
                if (day.Length != 1 || time.Length != 1 || groups.Length == 0)
                    throw new FormatException($"{source}, page {table.Page}: unmapped cell {box}: {cell.Text}");
                var resolved = new PdfScheduleCell(source, table.Page, groups, day[0].Text, Time.Match(time[0].Text).Groups[1].Value,
                    PdfTextNormalizer.Normalize(cell.Text), RawText: cell.Text, Bounds: box);
                result.AddRange(PdfTextNormalizer.ExpandCohorts(resolved).Select(PdfTextNormalizer.RepairMissingParity));
            }
        }
        return result;
    }

    public static void Import(ScheduleImportContext context, IEnumerable<PdfScheduleCell> cells, ImportedLessonHandler? addLesson = null)
    {
        var failures = new List<Exception>();
        foreach (var cell in cells)
        {
            try
            {
                var day = context.DayNameParser.Map(cell.Day) ?? throw new FormatException($"Unknown day: {cell.Day}");
                var time = context.TimeConfig.FindTimeSlotByStartTime(TimeOnly.Parse(cell.Start, CultureInfo.InvariantCulture))
                    ?? throw new FormatException($"Unknown start time: {cell.Start}");
                foreach (var parsed in context.ParseLessons(cell.Text.Split('\n').Select(l => l.AsMemory())))
                {
                    var lesson = parsed;
                    var names = cell.Groups;
                    var namedGroup = names.FirstOrDefault(g => g.Split('(')[0] == lesson.PartitionHint.ToString());
                    if (namedGroup is not null) { names = [namedGroup]; lesson.PartitionHint = default; }
                    var groups = names.Select(g => context.Schedule.Group(g).Id).ToArray();
                    if (addLesson is null) context.AddOrMergeLesson(lesson, day, time, groups, new(cell.Alternative));
                    else addLesson(lesson, day, time, groups, cell.Alternative);
                }
            }
            catch (Exception e) when (e is FormatException or ScheduleBuildException or NotSupportedException or WrongFormatException)
            {
                failures.Add(new FormatException($"{cell.Source}, page {cell.Page}, {cell.Day} {cell.Start}, {string.Join(", ", cell.Groups)} at {cell.Bounds}:\n{cell.Text}", e));
            }
        }
        if (failures.Count > 0) throw new AggregateException("PDF lesson import failed.", failures);
    }
}
