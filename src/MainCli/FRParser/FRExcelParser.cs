using ClosedXML.Excel;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.WordDoc;

namespace MainCli.FR;

public static class FrExcelParser
{
    public struct Params
    {
        public required Stream InputFile { get; init; }
        public required DocParseContext Context { get; init; }
    }

    private readonly record struct NumberedWorksheet(IXLWorksheet Worksheet, int Semester);

    public static ValueTask Parse(Params p)
    {
        using var xl = new XLWorkbook(p.InputFile);
        var ws = xl.Worksheets
            .Select(x =>
            {
                var parser = new Parser(x.Name);
                if (!parser.ConsumeExactString("sem"))
                {
                    return default;
                }
                if (!parser.SkipWhitespace().SkippedAny)
                {
                    return default;
                }
                var roman = parser.ReadRoman();
                if (roman.Status != ReadRomanStatus.Ok)
                {
                    return default;
                }

                return new NumberedWorksheet(x, roman.Number);
            })
            .WhereNotDefault()
            .Single();

        using var rowE = ws.Worksheet.Rows().GetEnumerator();
        if (!rowE.MoveNext())
        {
            throw new InvalidOperationException("Expected header row!");
        }
        var years = ParseGrades(rowE);
        _ = years;
        return ValueTask.CompletedTask;
    }

    private const int NoYear = -1;
    private static SizedItemArray<int> ParseGrades(IEnumerator<IXLRow> rowE)
    {
        var ret = new SizedItemArray<int>();
        if (!rowE.MoveNext())
        {
            throw new InvalidOperationException("Expected grade row!");
        }
        foreach (var cell in rowE.Current.Cells())
        {
            var merged = cell.MergedRange();
            if (merged.Rows().Count() != 1)
            {
                continue;
            }

            var str = cell.GetString();
            var parser = new Parser(str);
            if (!parser.ConsumeExactString("Anul"))
            {
                throw new InvalidOperationException("Expected `Anul` in the header row.");
            }
            if (!parser.SkipWhitespace().SkippedAny)
            {
                throw new InvalidOperationException("Expected whitespace after `Anul`.");
            }
            var romanResult = parser.ReadRoman();
            if (romanResult.Status != ReadRomanStatus.Ok)
            {
                throw new InvalidOperationException("Expected roman after `Anul`.");
            }

            var grade = romanResult.Number;
            var columnNumber = cell.AsRange().FirstColumn().ColumnNumber();
            var columnCount = merged.ColumnCount();
            var item = new SizedItem<int>(item: grade, size: columnCount);
            ret.AddAt(columnNumber, item, fillerValue: NoYear);
        }
        return ret;
    }

    private static GroupId[] ParseGroups(
        IEnumerator<IXLRow> rowE,
        ScheduleBuilder builder,
        SizedItemArray<int> years)
    {
        return null!;
    }
}
