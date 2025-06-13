using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleLib.Parsing;

namespace ScheduleLib.Helper.Excel;

public static class ExcelRangeHelper
{
    public struct AppendCellReferenceParams
    {
        public required StringBuilder StringBuilder;
        public required CellPosition Position;
    }

    public static void AppendCellReference(AppendCellReferenceParams p)
    {
        Span<char> stack = stackalloc char[8];
        int stackPos = 0;

        uint remaining = p.Position.Col + 1;
        while (true)
        {
            const uint base_ = 'Z' - 'A' + 1;
            byte remainder = (byte)((remaining - 1) % base_);
            byte letter = (byte)('A' + remainder);
            char ch = (char) letter;
            stack[stackPos] = ch;
            stackPos++;

            remaining -= remainder;
            remaining /= base_;
            if (remaining == 0)
            {
                break;
            }
        }

        for (int j = stackPos - 1; j >= 0; j--)
        {
            p.StringBuilder.Append(stack[j]);
        }

        p.StringBuilder.Append(p.Position.Row + 1);
    }

    public static StringValue GetCellReference(AppendCellReferenceParams p)
    {
        Debug.Assert(p.StringBuilder.Length == 0);
        AppendCellReference(p);
        var ret = p.StringBuilder.ToStringAndClear();
        return new StringValue(ret);
    }

    public struct AppendCellRangeParams
    {
        public required StringBuilder StringBuilder;
        public required CellPosition Start;
        public required CellPosition EndInclusive;
    }

    public static void AppendCellRange(AppendCellRangeParams p)
    {
        AppendCellReference(new()
        {
            StringBuilder = p.StringBuilder,
            Position = p.Start,
        });
        p.StringBuilder.Append(':');
        AppendCellReference(new()
        {
            StringBuilder = p.StringBuilder,
            Position = p.EndInclusive,
        });
    }

    public static StringValue GetCellRange(AppendCellRangeParams p)
    {
        Debug.Assert(p.StringBuilder.Length == 0);
        AppendCellRange(p);
        var ret = p.StringBuilder.ToStringAndClear();
        return new StringValue(ret);
    }

    public static CellPosition GetPosition(Indexed<Cell> cell, Indexed<Row> row)
    {
        CellPosition pos;
        if (cell.Item.CellReference?.Value is { } val)
        {
            var parser = new Parser(val);
            pos = parser.ParseCellPosition();
            if (!parser.IsEmpty)
            {
                throw new InvalidOperationException("Expected valid cell position syntax.");
            }
        }
        else
        {
            uint rowIndex = row.Item.RowIndex?.Value ?? ((uint) row.Index + 1);
            pos = new((uint)(cell.Index + 1), rowIndex);
        }
        return pos;
    }

    public static CellPosition ParseCellPosition(this ref Parser parser)
    {
        uint col = 0;
        while (true)
        {
            if (parser.IsEmpty)
            {
                throw new InvalidOperationException("Invalid cell position");
            }
            if (!char.IsLetter(parser.Current))
            {
                break;
            }
            col *= 'Z' - 'A' + 1;
            col += (uint) (char.ToUpperInvariant(parser.Current) - 'A' + 1);
            parser.Move();
        }

        var bparser = parser.BufferedView();
        if (!bparser.SkipNumbers().SkippedAny)
        {
            throw new InvalidOperationException("Expected number after the letter");
        }
        uint row = uint.Parse(parser.PeekSpanUntilPosition(bparser.Position));
        parser.MoveTo(bparser.Position);

        return new(col, row);
    }
}
