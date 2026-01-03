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
        var groups = ParseGroups(rowE, p.Context.Schedule, years);
        if (!rowE.MoveNext())
        {
            throw new InvalidOperationException("Bullshit row not found");
        }

        while (rowE.MoveNext())
        {
            // if found date again in a new merged region, verify that all X rows have been read
            // if it's time to read the date and day then {
            // parse date and day
            // validate day
            // store the fact that X cells are under the same day }
            // read the cell with roman and time range
            // validate that the roman is consecutive
            // validate that the cell is not merged
            // validate the start and end of range (I'll do it myself)
            // read next cell
            // parse using a thing I have (I'll do it myself)
            // the row count must be 1
            // save the colCount
            // add a lesson (I'll do it myself)
            // get the groups involved by doing groups[colNumber] for each of the involved column numbers. for this, use the groups list and subtract the start of the groups
        }

        return ValueTask.CompletedTask;
    }

    private struct Day
    {
        public required DayOfWeek DayOfWeek;
        public required DateOnly Date;
    }

    private struct DayTimeSlots
    {
        public required TimeSlot Start;
        public required int Count;
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
            if (ret.IsEmpty)
            {
                ret.AddAt(columnNumber, item, fillerValue: NoYear);
            }
            else
            {
                if (ret.TotalSize != columnNumber)
                {
                    throw new InvalidOperationException("Empty grade cell not allowed.");
                }
                ret.Add(item);
            }
        }
        return ret;
    }

    private readonly struct Groups
    {
        private readonly GroupId[] _arr;
        private readonly int _firstColumnOffset;

        public Groups(GroupId[] arr, int firstColumnOffset)
        {
            _arr = arr;
            _firstColumnOffset = firstColumnOffset;
        }

        public int FirstColumn() => _firstColumnOffset;

        public GroupId Get(int column)
        {
            int index = column - _firstColumnOffset;
            return _arr[index];
        }
    }

    private static Groups ParseGroups(
        IEnumerator<IXLRow> rowE,
        ScheduleBuilder builder,
        SizedItemArray<int> years)
    {
        if (!rowE.MoveNext())
        {
            throw new InvalidOperationException("No groups row found.");
        }
        if (years.FindPositionOfFirstOtherThan(NoYear) is not { } firstColumnOffset)
        {
            return new([], 0);
        }
        var outputCount = years.Count - firstColumnOffset;
        using var cellE = rowE.Current.Cells().GetEnumerator();
        while (true)
        {
            if (!cellE.MoveNext())
            {
                return new([], firstColumnOffset);
            }
            var col = cellE.Current!.AsRange().FirstColumn().ColumnNumber();
            if (col == outputCount)
            {
                break;
            }
            if (col > outputCount)
            {
                throw new InvalidOperationException("Number of blanks doesn't match");
            }
            if (!cellE.Current!.Value.IsBlank)
            {
                throw new InvalidOperationException("Skipped cells must be blank");
            }
        }

        var groups = new GroupId[outputCount];
        int groupIndex = 0;
        while (true)
        {
            var cell = cellE.Current!;
            var str = cell.GetString();
            var group = builder.Group(str);
            if (group.Ref.AttendanceMode != AttendanceMode.FrecventaRedusa)
            {
                throw new InvalidOperationException("Expected only FR groups in the FR excel");
            }

            {
                var col = cell.AsRange().FirstColumn().ColumnNumber();
                var colIndex = col - firstColumnOffset;
                if (colIndex != groupIndex)
                {
                    throw new InvalidOperationException("Skipped a column, they must be consecutive.");
                }
            }

            groups[groupIndex] = group;
            groupIndex++;

            if (!cellE.MoveNext())
            {
                break;
            }
        }

        if (groupIndex != outputCount)
        {
            throw new InvalidOperationException("Not all columns covered by year are covered by groups");
        }

        return new(groups, firstColumnOffset);
    }
}
