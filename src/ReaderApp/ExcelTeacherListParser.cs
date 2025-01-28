using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleLib;
using ScheduleLib.Parsing;

namespace ReaderApp;

public struct ExcelTeacherListParseParams
{
    public required SpreadsheetDocument Excel;
    public required ScheduleBuilder Schedule;
}

public static class ExcelTeacherListParser
{
    public static bool AddTeachersFromExcel(ExcelTeacherListParseParams p)
    {
        var workbook = p.Excel.WorkbookPart;
        if (workbook is null)
        {
            return false;
        }
        var worksheet = workbook.WorksheetParts.First().Worksheet;
        var sheetData = worksheet.Elements<SheetData>().First();
        var stringTable = ParsedStringTable.Create(workbook);

        var cellMetadata = workbook.CellMetadataPart;

        int skippedRows = 2;

        int rowIndex = 0;
        using var rowEnumerator = sheetData.Elements<Row>().GetEnumerator();
        if (!SkipTitleRows())
        {
            return false;
        }
        if (!ParseHeaderRow())
        {
            return false;
        }
        ParseDataRows();

        return true;

        bool SkipTitleRows()
        {
            while (true)
            {
                if (!rowEnumerator.MoveNext())
                {
                    return false;
                }

                rowIndex++;
                if (rowIndex > skippedRows)
                {
                    return true;
                }

                var row = rowEnumerator.Current;
                _ = row;
            }
        }

        bool ParseHeaderRow()
        {
            if (!rowEnumerator.MoveNext())
            {
                return false;
            }
            return true;
        }

        void ParseDataRows()
        {
            while (true)
            {
                var row = rowEnumerator.Current;
                int colIndex = 0;
                TeacherBuilder builder = default;
                foreach (var cell in row.Elements<Cell>())
                {
                    if (!Process(cell))
                    {
                        return;
                    }
                    if (colIndex == 4)
                    {
                        break;
                    }

                    colIndex++;
                }

                if (!rowEnumerator.MoveNext())
                {
                    return;
                }
                continue;

                bool Process(Cell cell)
                {
                    switch (colIndex)
                    {
                        case 0:
                        {
                            if (cell.CellValue is null)
                            {
                                return false;
                            }
                            return true;
                        }
                        // Nume, prenume
                        case 1:
                        {
                            var teacherName = stringTable.GetStringValue(cell);
                            if (teacherName is null)
                            {
                                throw new NotSupportedException("Expected a teacher name in the cell.");
                            }
                            var parsedName = ParseTeacherName(teacherName);
                            builder = p.Schedule.Teacher(new TeacherBuilderModel.NameModel
                            {
                                FirstName = parsedName.FirstName.ToString(),
                                LastName = parsedName.LastName.ToString(),
                            });
                            return true;
                        }
                        // E-mail
                        case 2:
                        {
                            var email = stringTable.GetStringValue(cell);
                            if (email is not null)
                            {
                                builder.Model.Contacts.PersonalEmail = email;
                            }
                            return true;
                        }
                        // E-mail corporativ
                        case 3:
                        {
                            var email = stringTable.GetStringValue(cell);
                            if (email is not null)
                            {
                                builder.Model.Contacts.CorporateEmail = email;
                            }
                            return true;
                        }
                        // Phone number
                        case 4:
                        {
                            var phone = stringTable.GetStringValue(cell);
                            if (phone is not null)
                            {
                                builder.Model.Contacts.PhoneNumber = phone;
                            }
                            return true;
                        }
                        default:
                        {
                            Debug.Fail("??");
                            return false;
                        }
                    }
                }
            }

        }
    }

    private readonly struct ParsedStringTable(List<string> strings)
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

    private struct TeacherNameFromExcel()
    {
        public ReadOnlyMemory<char> FirstName;
        public ReadOnlyMemory<char> LastName;
        public ReadOnlyMemory<char> MaidenName = "".AsMemory();
    }

    private static TeacherNameFromExcel ParseTeacherName(string s)
    {
        var ret = new TeacherNameFromExcel();

        var parser = new Parser(s);
        var bparser = parser.BufferedView();
        {
            var r = bparser.SkipUntil([' ']);
            if (!r.Satisfied)
            {
                throw new NotSupportedException("Wrong teacher name format");
            }
        }
        {
            ret.LastName = parser.SourceUntilExclusive(bparser);
            parser.MovePast(bparser.Position);
        }
        bparser = parser.BufferedView();

        var maidenName = MaybeParseMaidenName(ref bparser);
        if (!maidenName.IsEmpty)
        {
            ret.MaidenName = maidenName;

            parser.MoveTo(bparser.Position);
            if (parser.IsEmpty)
            {
                throw new NotSupportedException("Expected first name after maiden name");
            }
            if (parser.Current != ' ')
            {
                throw new NotSupportedException("A space must follow the maiden name.");
            }
            parser.Move();
        }
        bparser = parser.BufferedView();

        {
            var r = bparser.SkipNotWhitespace();
            if (!r.EndOfInput)
            {
                throw new NotSupportedException("No spaces should follow the first name.");
            }

            var firstName = parser.SourceUntilExclusive(bparser);
            ret.FirstName = firstName;
        }

        return ret;
    }

    private static ReadOnlyMemory<char> MaybeParseMaidenName(ref Parser parser)
    {
        if (parser.IsEmpty)
        {
            return ReadOnlyMemory<char>.Empty;
        }
        if (parser.Current != '(')
        {
            return ReadOnlyMemory<char>.Empty;
        }

        parser.Move();

        var bparser = parser.BufferedView();

        var parenSkipResult = bparser.SkipUntil([')']);
        if (parenSkipResult.EndOfInput)
        {
            throw new ArgumentException("The opening parenthesis must be closed.");
        }
        if (!parenSkipResult.SkippedAny)
        {
            throw new ArgumentException("The opening parenthesis must be followed by a name.");
        }

        var lastName = parser.SourceUntilExclusive(bparser);
        parser.MovePast(bparser.Position);
        return lastName;
    }
}
