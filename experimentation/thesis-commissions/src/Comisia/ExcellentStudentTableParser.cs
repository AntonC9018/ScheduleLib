using System.Collections.Immutable;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleLib;
using ScheduleLib.Curriculum;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Excel;

namespace Comisia;

public sealed class ExcellentStudentInfo
{
    public required string FirstName;
    public required string LastName;
}

public sealed class ExcellentStudentsTable
{
    public required ImmutableArray<ExcellentStudentInfo> Students;
}

public static class ExcellentStudentTableParser
{
    public static ExcellentStudentsTable Parse(string filePath)
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

        var strings = StringSearchHelper.SetupSearchArray<Column>(x =>
        {
            x.Set(Column.LastName, "Nume");
            x.Set(Column.FirstName, "Prenume");
        });


        foreach (var row in sheetData.IndexedRows())
        {
            switch (state.Action)
            {
                case Action.Headers:
                {
                    foreach (var col in row.SizedCells(widths))
                    {
                        state.ColumnMappings.Add(new(Column.Unknown, col.Size));
                    }


                    Column SearchColumn(string text)
                    {
                        for (int index = 0; index < strings.Length; index++)
                        {
                            var column = (Column) index;
                            var exactString = strings[index];
                            if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(text, exactString))
                            {
                                return column;
                            }
                        }
                        return Column.Unknown;
                    }

                    foreach (var col in row.SizedCells(widths))
                    {
                        var text = stringTable.GetStringValue(col.Cell);
                        if (text is null)
                        {
                            continue;
                        }
                        var match = SearchColumn(text);
                        if (match == Column.Unknown)
                        {
                            continue;
                        }
                        state.ColumnMappings.ReplaceItem(col.Position, match);

                        if (state.FoundHeaders.IsSet((int) match))
                        {
                            throw new InvalidOperationException($"Found column twice {match}");
                        }
                        state.FoundHeaders.Set((int) match);
                    }

                    state.Action = Action.Data;
                    break;
                }
                case Action.Data:
                {
                    bool AreAllEmpty()
                    {
                        foreach (var col in row.SizedCells(widths))
                        {
                            var str = stringTable.GetStringValue(col.Cell);
                            if (str != null)
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                    if (AreAllEmpty())
                    {
                        break;
                    }

                    string? firstName = null;
                    string? lastName = null;

                    foreach (var col in row.SizedCells(widths))
                    {
                        var str = stringTable.GetStringValue(col.Cell);
                        if (str is null)
                        {
                            continue;
                        }

                        if (col.Position >= state.ColumnMappings.TotalSize)
                        {
                            break;
                        }

                        var column = state.ColumnMappings.Find(col.Position);
                        switch (column)
                        {
                            case Column.Unknown:
                            {
                                continue;
                            }
                            case Column.FirstName:
                            {
                                firstName = str;
                                continue;
                            }
                            case Column.LastName:
                            {
                                lastName = str;
                                continue;
                            }
                            default:
                            {
                                throw UnreachableHelper.Unreachable();
                            }
                        }
                    }

                    if (firstName is null || lastName is null)
                    {
                        throw new InvalidOperationException($"Row {row.Index} didn't have student name");
                    }

                    var student = new ExcellentStudentInfo
                    {
                        FirstName = firstName,
                        LastName = lastName,
                    };
                    state.Students.Add(student);
                    break;
                }
            }
        }

        var ret = state.Students.DrainToImmutable();
        return new()
        {
            Students = ret,
        };
    }

    private struct ParsingState()
    {
        public UnsizedBitArray32 FoundHeaders = default;
        public SizedItemArray<Column> ColumnMappings = new();
        public Action Action = Action.Headers;
        public ImmutableArray<ExcellentStudentInfo>.Builder Students = ImmutableArray.CreateBuilder<ExcellentStudentInfo>();
    }

    private enum Action
    {
        Headers,
        Data,
    }

    private enum Column
    {
        Unknown = -1,
        FirstName,
        LastName,
    }
}
