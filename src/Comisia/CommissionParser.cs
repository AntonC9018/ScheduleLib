using System.Collections;
using System.Collections.Immutable;
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
    public required ImmutableArray<StudentName> Students;
}

public sealed class StudentName
{
    public NameParts<string?> FirstName;
    public NameParts<string?> LastName;
    public NameParts<string?> Patronymic;
}


public static class CommissionTeamParser
{
    private static StudentName ParseStudentName(ref Parser parser)
    {
        var ret = new StudentName();

        ret.LastName.A = ParseNamePart(ref parser, "No last name");

        LastNameCheck(ref parser);
        if (parser.Current == NameConstants.DoubleNameSeparator)
        {
            parser.Move();
            ret.LastName.B = ParseNamePart(ref parser, "Last name incomplete");
        }

        parser.SkipWhitespace();
        FirstNameCheck(ref parser);

        if (parser.Current == '(')
        {
            var s = parser.SkipUntilAny([')']);
            if (!s.Satisfied)
            {
                throw new NotSupportedException("Unclosed parenthesis");
            }

            parser.Move();

            parser.SkipWhitespace();
            FirstNameCheck(ref parser);
        }

        ret.FirstName.A = ParseNamePart(ref parser, "No first name");

        parser.SkipWhitespace();
        if (parser.IsEmpty)
        {
            return ret;
        }
        if (parser.Current == NameConstants.DoubleNameSeparator)
        {
            parser.Move();
            ret.FirstName.B = ParseNamePart(ref parser, "First name incomplete");
        }

        parser.SkipWhitespace();
        if (parser.IsEmpty)
        {
            return ret;
        }

        ret.Patronymic.A = ParseNamePart(ref parser, "No patronymic");
        if (parser.IsEmpty)
        {
            return ret;
        }

        if (parser.Current == NameConstants.DoubleNameSeparator)
        {
            parser.Move();
            ret.Patronymic.B = ParseNamePart(ref parser, "Patronymic incomplete");
        }

        return ret;

        static void FirstNameCheck(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                throw new NotSupportedException("First name expected");
            }
        }
        static void LastNameCheck(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                throw new NotSupportedException("Last name expected");
            }
        }
        static string ParseNamePart(ref Parser parser, string error)
        {
            var bparser = parser.BufferedView();
            var skipResult = bparser.SkipLetters();
            if (!skipResult.SkippedAny)
            {
                throw new NotSupportedException(error);
            }

            var ret = parser.PeekSpanUntilPosition(bparser.Position).ToString();
            parser.MoveTo(bparser.Position);
            return ret;
        }
    }

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

        using var rows = sheetData.Elements<Row>().WithIndex().GetEnumerator();
        var state = new ParsingState();
        var ret = ImmutableArray.CreateBuilder<Commission>();
        while (rows.MoveNext())
        {
            var row = rows.Current;

            state.CommissionColumnMappings.Clear();

            switch (state.Action)
            {
                case Action.Date:
                {
                    using var cells = row.Item.Elements<Cell>().GetEnumerator();
                    if (!cells.MoveNext())
                    {
                        continue;
                    }

                    var firstCell = cells.Current;
                    if (cells.MoveNext())
                    {
                        throw new InvalidOperationException("Expected only one cell for the date.");
                    }

                    var date = GetDate(firstCell);
                    state.Date = date;
                    state.Action = Action.Commissions;
                    break;

                    DateOnly GetDate(Cell cell)
                    {
                        DateOnly DateFromString(string s)
                        {
                            return DateOnly.ParseExact(s, "dd.MM.yy");
                        }

                        if (cell.DataType?.Value == CellValues.Date)
                        {
                            throw new NotImplementedException("How tf is this stored??");
                        }
                        var str = stringTable.GetStringValue(cell);
                        if (str is null)
                        {
                            throw new InvalidOperationException("Expected a string?");
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
                            throw new InvalidOperationException("");
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
                        var it = (num, ImmutableArray.CreateBuilder<StudentName>());
                        state.CommissionColumnMappings.Add(new(it, cell.Size));
                    }
                    state.Action = Action.FirstNameRow;
                    break;
                }

                case Action.FirstNameRow:
                case Action.NameRow:
                {
                    bool added = false;
                    foreach (var cell in new SizedCellEnumerable(widths, row))
                    {
                        var text = stringTable.GetStringValue(cell.Cell);
                        if (text is null)
                        {
                            continue;
                        }

                        var parser = new Parser(text);
                        var studentName = ParseStudentName(ref parser);

                        var list = state.CommissionColumnMappings.Find(cell.Position).Students;
                        list.Add(studentName);

                        added = true;
                    }

                    if (!added && state.Action == Action.NameRow)
                    {
                        FinalizeCurrentCommissions();
                        state.Action = Action.Date;
                        break;
                    }
                    if (state.Action == Action.FirstNameRow)
                    {
                        state.Action = Action.NameRow;
                        break;
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
            Commissions = ret.MoveToImmutable(),
        };

        void FinalizeCurrentCommissions()
        {
            foreach (var it in state.CommissionColumnMappings)
            {
                ret.Add(new()
                {
                    Date = state.Date,
                    Number = it.Item.CommissionNumber,
                    Students = it.Item.Students.MoveToImmutable(),
                });
            }
            state.CommissionColumnMappings.Clear();
        }
    }

    private struct ParsingState()
    {
        public Action Action = Action.Date;
        public DateOnly Date = default;
        public SizedItemArray<(int CommissionNumber, ImmutableArray<StudentName>.Builder Students)> CommissionColumnMappings = new();
    }

    private enum Action
    {
        Date,
        Commissions,
        FirstNameRow,
        NameRow,
    }
}
