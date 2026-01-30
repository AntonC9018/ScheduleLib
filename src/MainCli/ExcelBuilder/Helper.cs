using DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleLib.Application.Core.ExcelBuilder;

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

public static class ExcelBuilderHelper
{
    public static void SetStringValue(this Cell cell, string str)
    {
        cell.DataType = CellValues.String;
        cell.CellValue = new(str);
    }
}

