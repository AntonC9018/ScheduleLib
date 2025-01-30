using System.Diagnostics;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ReaderApp.ExcelBuilder;

public struct CellsBuilder
{
    private readonly SheetData _sheetData;
    private Row? _row;

    public CellsBuilder(SheetData sheetData)
    {
        _sheetData = sheetData;
        _row = null;
    }

    public Row NextRow()
    {
        _row = new Row();
        _sheetData.AppendChild(_row);
        return _row;
    }

    public Cell NextCell()
    {
        var cell = new Cell();
        Debug.Assert(_row != null);
        _row.AppendChild(cell);
        return cell;
    }
}
