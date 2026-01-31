using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleLib.Helper.Excel;

public readonly struct ParsedStringTable(List<string> strings)
{
    private readonly List<string> _strings = strings;

    public static ParsedStringTable Create(WorkbookPart workbook)
    {
        List<string> strings = new();
        var ret = new ParsedStringTable(strings);

        var stringTablePart = workbook.SharedStringTablePart;
        if (stringTablePart is null)
        {
            return ret;
        }
        var stringTable = stringTablePart.SharedStringTable;
        foreach (var str in stringTable.Elements<SharedStringItem>())
        {
            strings.Add(str.InnerText);
        }
        return ret;
    }

    public string? GetStringValue(Cell cell)
    {
        if (cell.DataType is not { } dt)
        {
            if (cell.CellValue is { } cv)
            {
                return cv.InnerText;
            }
            return null;
        }
        if (dt == CellValues.Boolean
            || dt == CellValues.Date
            || dt == CellValues.Error
            || dt == CellValues.Number)
        {
            throw new InvalidOperationException("Expected a string");
        }
        if (dt == CellValues.InlineString)
        {
            return cell.InlineString!.Text!.Text;
        }
        if (dt == CellValues.SharedString)
        {
            var index = cell.CellValue!.Text;
            if (!int.TryParse(index, out int i))
            {
                throw new InvalidOperationException("Invalid shared string index.");
            }
            return _strings[i];
        }
        if (dt == CellValues.String)
        {
            return cell.CellValue!.Text;
        }
        throw new InvalidOperationException("Invalid type");
    }
}
