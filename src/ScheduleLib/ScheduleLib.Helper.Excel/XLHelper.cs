using ClosedXML.Excel;
using ScheduleLib.Helper;

namespace ScheduleLib.Excel.Helper;

// ReSharper disable once InconsistentNaming
public static class ExcelLibExtensions
{
    public static ExcelRangeException Exception(this IXLCell cell, string message, Exception? inner = null)
    {
        return new(cell.ActualRange().RangeAddress.ToString()!, message, inner);
    }
    public static ExcelRangeException Exception(this IXLRow row, string message, Exception? inner = null)
    {
        return new(row.RangeAddress.ToString()!, message, inner);
    }
    public static ExcelSheetException Exception(this IXLWorksheet sheet, string message, Exception? inner = null)
    {
        return new(sheet.Name, message, inner);
    }

    public static int ColumnCount(this IXLCell cell)
    {
        if (cell.MergedRange() is { } merged)
        {
            return merged.ColumnCount();
        }
        return 1;
    }

    public static IXLRange ActualRange(this IXLCell cell)
    {
        if (cell.MergedRange() is { } merged)
        {
            return merged;
        }
        return cell.AsRange();
    }

    public static IEnumerable<IXLCell> CellsWithMergedAppearingOnce(this IXLRow row)
    {
        IXLRange? currentRange = null;

        foreach (var cell in row.Cells())
        {
            var range = cell.ActualRange();
            if (ReferenceEquals(currentRange, range))
            {
                continue;
            }

            currentRange = range;
            yield return cell;
        }
    }

}

public interface ICellProcessor<TColumn>
{
    public bool Process(TColumn column, IXLCell value);
}

public sealed class ExcelSheetException : NotSupportedException
{
    public string SheetName { get; set; }

    public ExcelSheetException(string sheetName, string message, Exception? inner = null)
        : base($"Sheet {sheetName}: {message}", inner)
    {
        SheetName = sheetName;
    }
}

public sealed class ExcelRangeException : NotSupportedException
{
    public string CellRange { get; set; }

    public ExcelRangeException(string cellRange, string message, Exception? inner = null)
        : base($"{cellRange}: {message}", inner)
    {
        CellRange = cellRange;
    }
}
