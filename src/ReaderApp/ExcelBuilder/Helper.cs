using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleLib;

namespace ReaderApp;

public readonly struct Spaces : ISpanFormattable
{
    private readonly int _count;
    public Spaces(int count) => _count = count;

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return new string(NonBreakingSpace, _count);
    }

    private const char NonBreakingSpace = '\u00A0';

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = provider;
        _ = format;

        if (destination.Length < _count)
        {
            charsWritten = 0;
            return false;
        }
        charsWritten = _count;
        for (int i = 0; i < _count; i++)
        {
            destination[i] = NonBreakingSpace;
        }
        return true;
    }
}

public static class ExcelHelper
{
    public static void SetStringValue(this Cell cell, string str)
    {
        cell.DataType = CellValues.String;
        cell.CellValue = new(str);
    }

    public record struct CellPosition(uint ColIndex, uint RowIndex);

    public struct AppendCellReferenceParams
    {
        public required StringBuilder StringBuilder;
        public required CellPosition Position;
    }

    public static void AppendCellReference(AppendCellReferenceParams p)
    {
        Span<char> stack = stackalloc char[8];
        int stackPos = 0;

        uint remaining = p.Position.ColIndex + 1;
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

        p.StringBuilder.Append(p.Position.RowIndex + 1);
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
}


