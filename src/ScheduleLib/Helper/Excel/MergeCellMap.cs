using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleLib.Parsing;

namespace ScheduleLib.Helper.Excel;

public sealed class MergeCellMap
{
    private readonly Dictionary<CellPosition, int> _cellWidths;

    public static MergeCellMap Create(WorksheetPart worksheetPart)
    {
        return new MergeCellMap(worksheetPart);
    }

    public MergeCellMap(WorksheetPart worksheetPart)
    {
        _cellWidths = new Dictionary<CellPosition, int>();

        var mergeCells = worksheetPart.Worksheet.Elements<MergeCells>().FirstOrDefault();
        if (mergeCells == null)
        {
            return;
        }

        foreach (var mergeCell in mergeCells.Elements<MergeCell>())
        {
            if (mergeCell.Reference?.Value is not { } val)
            {
                continue;
            }

            var parser = new Parser(val);
            var start = parser.ParseCellPosition();
            if (!parser.ConsumeExactString(":"))
            {
                throw new InvalidOperationException("Expected a range syntax.");
            }
            var end = parser.ParseCellPosition();
            if (!parser.IsEmpty)
            {
                throw new InvalidOperationException("Range syntax continues?");
            }
            int width = (int)(end.Col - start.Col + 1);
            _cellWidths[start] = width;
        }
    }

    public int GetCellWidth(Indexed<Cell> cell, Indexed<Row> row)
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
        return _cellWidths.GetValueOrDefault(pos, 1);
    }
}
