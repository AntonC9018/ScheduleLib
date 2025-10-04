using System.Collections.Immutable;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleLib;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Excel;
using ScheduleLib.Parsing;

public sealed class CommissionSchedule
{
    public required ImmutableArray<Commission> Commissions;
}

public sealed class Commission
{
    public required int Number;
    public required DateOnly Date;
    public required ImmutableArray<Name> Students;
}

public static class CommissionParser
{
    public static CommissionSchedule ParseCommissions(string filePath)
    {
        using var excel = SpreadsheetDocument.Open(filePath, isEditable: false, new()
        {
            AutoSave = false,
            CompatibilityLevel = CompatibilityLevel.Version_2_20,
        });

        var workbook = excel.WorkbookPart;
        if (workbook is null)
        {
            throw new InvalidOperationException("Excel is wrong");
        }
        var worksheetPart = workbook.WorksheetParts.First();
        var worksheet = worksheetPart.Worksheet;
        var sheetData = worksheet.Elements<SheetData>().First();
        var stringTable = ParsedStringTable.Create(workbook);
        var widths = MergeCellMap.Create(worksheetPart);

        var state = new ParsingState();
        using var rows = sheetData.IndexedRows().GetEnumerator();
        var ret = ImmutableArray.CreateBuilder<Commission>();
        while (rows.MoveNext())
        {
            var row = rows.Current;
            if (row.IsJump && state.Action == Action.NameRow)
            {
                FinalizeCurrentCommissions();
                state.Action = Action.Date;
            }

            switch (state.Action)
            {
                case Action.Date:
                {
                    foreach (var cell in row.SizedCells(widths))
                    {
                        if (GetDate(cell.Cell) is not { } date)
                        {
                            continue;
                        }
                        state.DateMappings.AddAt(cell.Position, new(date, cell.Size));
                    }
                    if (!state.DateMappings.IsEmpty)
                    {
                        state.Action = Action.Commissions;
                    }
                    break;

                    DateOnly? GetDate(Cell cell)
                    {
                        DateOnly DateFromString(string s)
                        {
                            var excelEpoch = new DateOnly(1899, 12, 30);

                            var parser = new Parser(s);
                            var bparser = parser.BufferedView();
                            bparser.SkipNumbers();
                            var span = parser.PeekSpanUntilPosition(bparser.Position);
                            int days = int.Parse(span);

                            var date = excelEpoch.AddDays(days);
                            return date;
                        }

                        if (cell.DataType?.Value == CellValues.Date)
                        {
                            throw new NotImplementedException("How tf is this stored??");
                        }
                        var str = stringTable.GetStringValue(cell);
                        if (str is null)
                        {
                            return null;
                        }
                        return DateFromString(str);
                    }
                }

                case Action.Commissions:
                {
                    foreach (var cell in new SizedCellEnumerable(widths, row))
                    {
                        var text = stringTable.GetStringValue(cell.Cell);
                        if (text is null)
                        {
                            continue;
                        }

                        var parser = new Parser(text);
                        if (!parser.ConsumeExactString("Comisia"))
                        {
                            throw new InvalidOperationException("Expected text 'Comisia'");
                        }
                        if (!parser.SkipWhitespace().SkippedAny)
                        {
                            throw new InvalidOperationException("Expected whitespace after 'Comisia'");
                        }
                        var romanResult = parser.ReadRoman();
                        if (romanResult.Status != ReadRomanStatus.Ok)
                        {
                            throw new InvalidOperationException("Expected roman number after 'Comisia'");
                        }
                        int num = romanResult.Number;
                        state.CommissionNumberMappings.AddAt(cell.Position, new(num, cell.Size));
                    }
                    if (state.CommissionNumberMappings.IsEmpty)
                    {
                        continue;
                    }
                    state.Action = Action.FirstNameRow;
                    break;
                }

                case Action.FirstNameRow:
                case Action.NameRow:
                {
                    IEnumerable<(string Text, CellInfo Cell)> NonEmptyCells()
                    {
                        foreach (var cell in row.SizedCells(widths))
                        {
                            var text = stringTable.GetStringValue(cell.Cell);
                            if (text is not null)
                            {
                                yield return (text, cell);
                            }
                        }
                    }
                    bool NoneHasText()
                    {
                        foreach (var x in NonEmptyCells())
                        {
                            _ = x;
                            return false;
                        }
                        return true;
                    }

                    bool isFirst = state.Action == Action.FirstNameRow;
                    state.Action = Action.NameRow;

                    if (isFirst && NoneHasText())
                    {
                        break;
                    }
                    if (!isFirst && NoneHasText())
                    {
                        FinalizeCurrentCommissions();
                        state.Action = Action.Date;
                        break;
                    }

                    if (state.StudentColumns.Count == 0)
                    {
                        foreach (var x in NonEmptyCells())
                        {
                            var b = ImmutableArray.CreateBuilder<Name>();
                            state.StudentColumns.AddAt(x.Cell.Position, new(b, x.Cell.Size));
                        }
                    }

                    foreach (var x in NonEmptyCells())
                    {
                        var text = stringTable.GetStringValue(x.Cell.Cell);
                        if (text is null)
                        {
                            continue;
                        }

                        var parser = new Parser(text);
                        parser.SkipWhitespace();
                        parser.SkipNumbers();
                        parser.ConsumeExactString(".");
                        parser.SkipWhitespace();

                        var studentName = NameHelper.ParseName(ref parser);
                        var students = state.StudentColumns.Find(x.Cell.Position);
                        if (students is null)
                        {
                            throw new InvalidOperationException("Students must start immediately");
                        }
                        students.Add(studentName);
                    }
                    break;
                }
            }
        }

        switch (state.Action)
        {
            case Action.NameRow:
            case Action.FirstNameRow:
            {
                FinalizeCurrentCommissions();
                break;
            }
        }
        return new()
        {
            Commissions = ret.DrainToImmutable(),
        };

        void FinalizeCurrentCommissions()
        {
            foreach (var it in state.StudentColumns.EnumerateWithPosition())
            {
                if (it.Item is not { } students)
                {
                    continue;
                }
                var commissionNumber = state.CommissionNumberMappings.Find(it.Position);
                var date = state.DateMappings.Find(it.Position);
                if (date is null)
                {
                    throw new InvalidOperationException("Student column with no date");
                }
                ret.Add(new()
                {
                    Date = date.Value,
                    Number = commissionNumber,
                    Students = students.DrainToImmutable(),
                });
            }
            state.StudentColumns.Clear();
            state.CommissionNumberMappings.Clear();
            state.DateMappings.Clear();
        }
    }

    private struct ParsingState()
    {
        public Action Action = Action.Date;
        public SizedItemArray<int> CommissionNumberMappings = new();
        public SizedItemArray<DateOnly?> DateMappings = new();
        public SizedItemArray<ImmutableArray<Name>.Builder?> StudentColumns = new();
    }

    private enum Action
    {
        Date,
        Commissions,
        FirstNameRow,
        NameRow,
    }

}
