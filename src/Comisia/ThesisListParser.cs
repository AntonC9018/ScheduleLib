using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ScheduleLib;
using ScheduleLib.Curriculum;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Excel;
using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;

namespace Comisia;

public sealed class ThesisList
{
    public required ImmutableArray<Thesis> Items;
}
public sealed class Thesis
{
    public required Name StudentName;
    public required Name TeacherName;
    public required string GroupName;
    public required string ThesisNameRomanian;
    public required string? ThesisNameRussian; // \ / ignore any " \n  also (russian text) is allowed?
    public required string? ThesisNameEnglish;
}

internal struct ThesisInParsing()
{
    public Name? TeacherName;
    public string? GroupName;
    public string? ThesisNameRomanian;
    public string? ThesisNameRussian;
    public string? ThesisNameEnglish;
}

public enum ThesisType
{
    An,
    Licenta,
    Master,
}

public static class ThesisListParser
{
    private enum Column
    {
        Unknown = -1,
        Number,
        Group,
        StudentName,
        Mentor,
        ThesisName,
        ThesisNameEnglish,
        Count,
    }

    public static ThesisList Parse(string filePath, ThesisType targetThesisType)
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
        if (workbook.Workbook.Sheets is not { } sheets)
        {
            throw new InvalidOperationException("No sheets in excel");
        }
        var sheet = sheets.Elements<Sheet>().FirstOrDefault(x =>
        {
            if (x.Name?.Value is not { } name)
            {
                return false;
            }
            if (x.State is { } state
                && state != SheetStateValues.Visible)
            {
                return false;
            }
            if (x.Id?.Value is null)
            {
                return false;
            }
            var comparer = IgnoreDiacriticsAndCaseComparer.Instance;
            switch (targetThesisType)
            {
                case ThesisType.An:
                    return comparer.Contains(name, "an");
                case ThesisType.Licenta:
                    return comparer.Contains(name, "licenta");
                case ThesisType.Master:
                    return comparer.Contains(name, "master");
                default:
                    throw Unreachable();
            }
        });

        if (sheet is null)
        {
            throw new InvalidOperationException("Sheet not found");
        }

        var stringTable = ParsedStringTable.Create(workbook);
        var worksheetPart = (WorksheetPart) workbook.GetPartById(sheet.Id!.Value!);
        var widths = MergeCellMap.Create(worksheetPart);
        var worksheet = worksheetPart.Worksheet;
        var sheetData = worksheet.GetFirstChild<SheetData>();
        if (sheetData is null)
        {
            throw new InvalidOperationException("SheetData not found");
        }

        var rows = sheetData.IndexedRows();
        var state = new State();
        foreach (var row in rows)
        {
            if (state.Action == Action.MeaninglessHeaders)
            {
                if (row.SizedCells(widths).MoreThanOneItem())
                {
                    state.Action = Action.MeaningfulHeaders;
                }
            }

            switch (state.Action)
            {
                case Action.MeaninglessHeaders:
                {
                    break;
                }
                case Action.MeaningfulHeaders:
                {
                    foreach (var cell in row.SizedCells(widths))
                    {
                        var text = stringTable.GetStringValue(cell.Cell);
                        if (text is null)
                        {
                            throw new InvalidOperationException("Text must not be null");
                        }
                        var column = MatchColumn(text);
                        state.ColumnMappings.AddAt(cell.Position, new(column, cell.Size));
                        if (column != Column.Unknown)
                        {
                            state.PresentColumns.Set((int) column);
                        }
                    }

                    var requiredColumns = BitArray32.AllSet((int) Column.Count);
                    if (targetThesisType != ThesisType.An)
                    {
                        requiredColumns.Set((int) Column.ThesisNameEnglish, false);
                    }

                    var presentColumns = state.PresentColumns.WithFixedSize((int) Column.Count);
                    var missingColumns = presentColumns.Flipped;
                    if (!missingColumns.Intersect(requiredColumns).IsEmpty)
                    {
                        // TODO: Wrap bit array with enum to present these.
                        throw new InvalidOperationException("There are missing columns");
                    }

                    state.Action = Action.Data;
                    break;
                }
                case Action.Data:
                {
                    var thesis = new ThesisInParsing();
                    if (row.SizedCells(widths).All(x => x.Cell.CellValue == null))
                    {
                        break;
                    }
                    foreach (var cell in row.SizedCells(widths))
                    {
                        if (!state.ColumnMappings.TryFind(cell.Position, out var column))
                        {
                            break;
                        }
                        var text = stringTable.GetStringValue(cell.Cell);
                        switch (column)
                        {
                            case Column.Number:
                                continue;

                            case Column.StudentName:
                            {
                                if (text is null or "")
                                {
                                    throw new InvalidOperationException("Student name is required");
                                }
                                var parser = new Parser(text);
                                while (true)
                                {
                                    parser.SkipWhitespace();
                                    if (parser.IsEmpty)
                                    {
                                        break;
                                    }

                                    var studentName = NameHelper.ParseName(ref parser);
                                    state.StudentNames.Add(studentName);
                                    if (!parser.SkipWhitespace().SkippedAny)
                                    {
                                        break;
                                    }
                                }
                                if (!parser.IsEmpty)
                                {
                                    throw new InvalidOperationException("Parser not empty after student name");
                                }
                                if (state.StudentNames.Count == 0)
                                {
                                    throw new InvalidOperationException("No student names found");
                                }
                                break;
                            }
                            case Column.Mentor:
                            {
                                if (text is null or "")
                                {
                                    continue;
                                }
                                thesis.TeacherName = ParseName(text);
                                break;
                            }
                            case Column.Group:
                            {
                                if (text is null or "")
                                {
                                    throw new InvalidOperationException("Group is required");
                                }

                                var parser = new Parser(text);
                                var sb = state.StringBuilder;
                                int parenDepth = 0;
                                while (true)
                                {
                                    if (parser.IsEmpty)
                                    {
                                        break;
                                    }
                                    switch (parser.Current)
                                    {
                                        case '(':
                                            parenDepth += 1;
                                            break;
                                        case ')':
                                            parenDepth -= 1;
                                            break;

                                        case '-':
                                            break;

                                        default:
                                        {
                                            if (char.IsWhiteSpace(parser.Current))
                                            {
                                                break;
                                            }
                                            if (parenDepth > 0)
                                            {
                                                break;
                                            }
                                            sb.Append(parser.Current);
                                            break;
                                        }
                                    }
                                    parser.Move();
                                }

                                var groupName = sb.ToStringAndClear();
                                thesis.GroupName = groupName;
                                break;
                            }
                            case Column.ThesisName:
                            {
                                if (text is null or "")
                                {
                                    continue;
                                }
                                var ret = ParseThesisNames(text);
                                thesis.ThesisNameRomanian = ret.Ro.ToString();
                                thesis.ThesisNameRussian = ret.Ru.Length > 0 ? ret.Ru.ToString() : null;
                                break;
                            }
                            case Column.ThesisNameEnglish:
                            {
                                thesis.ThesisNameEnglish = text;
                                break;
                            }
                        }
                    }

                    foreach (var studentName in state.StudentNames)
                    {
                        state.Result.Add(new()
                        {
                            GroupName = thesis.GroupName ?? throw new InvalidOperationException("Group name is required"),
                            StudentName = studentName,
                            TeacherName = thesis.TeacherName ?? throw new InvalidOperationException("Teacher name is required"),
                            ThesisNameRomanian = thesis.ThesisNameRomanian ?? throw new InvalidOperationException("Thesis name in Romanian is required"),
                            ThesisNameRussian = thesis.ThesisNameRussian,
                            ThesisNameEnglish = thesis.ThesisNameEnglish,
                        });
                    }
                    state.StudentNames.Clear();
                    break;
                }
            }
        }
        return new ThesisList
        {
            Items = state.Result.DrainToImmutable(),
        };
    }

    private static Name ParseName(string text)
    {
        var parser = new Parser(text);
        parser.SkipWhitespace();
        var studentName = NameHelper.ParseName(ref parser);
        parser.SkipWhitespace();
        if (!parser.IsEmpty)
        {
            throw new InvalidOperationException("Parser not empty after name");
        }

        return studentName;
    }

    private static Column MatchColumn(string text)
    {
        text = text.Replace("\n", " ");

        for (int index = 0; index < _headerKeys.Length; index++)
        {
            var key = _headerKeys[index];
            var column = (Column) index;
            var it = _headerKeys[index];
            if (it.ExactString is { } exactString)
            {
                if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(text, exactString))
                {
                    return column;
                }
            }
            else if (it.OrderedKeywords.Length > 0)
            {
                if (CheckKeywords(text, key))
                {
                    return column;
                }
            }
            else
            {
                throw Unreachable();
            }
        }
        return Column.Unknown;
    }

    private static bool CheckKeywords(string text, SearchFilter key)
    {
        int keywordIndex = 0;
        var parser = new Parser(text);

        while (true)
        {
            if (keywordIndex >= key.OrderedKeywords.Length)
            {
                return true;
            }

            var keyword = key.OrderedKeywords[keywordIndex];
            if (!CheckKeyword())
            {
                break;
            }
            keywordIndex += 1;
            continue;

            bool CheckKeyword()
            {
                while (true)
                {
                    parser.SkipWhitespace();

                    var bparser = parser.BufferedView();
                    if (!bparser.SkipUntilAny(" ").SkippedAny)
                    {
                        return false;
                    }

                    var word = parser.PeekSpanUntilPosition(bparser.Position);
                    parser.MoveTo(bparser.Position);

                    if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(word, keyword))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static readonly ImmutableArray<SearchFilter> _headerKeys =
        StringSearchHelper.SetupSearchArray<Column, SearchFilter>(b =>
        {
            b.Set(Column.Number, new()
            {
                ExactString = "nr.",
            });
            b.Set(Column.Group, new()
            {
                ExactString = "grupa",
            });
            b.Set(Column.StudentName, new()
            {
                ExactString = "numele studentului",
            });
            b.Set(Column.Mentor, new()
            {
                ExactString = "numele conducatorului stiintific",
            });
            b.Set(Column.ThesisName, new()
            {
                OrderedKeywords = ["denumirea", "romana"],
            });
            b.Set(Column.ThesisNameEnglish, new()
            {
                OrderedKeywords = ["denumirea", "engleza"],
            });
        });

    private enum Action
    {
        MeaninglessHeaders,
        MeaningfulHeaders,
        Data,
    }

    private struct State()
    {
        public Action Action = Action.MeaninglessHeaders;
        public readonly SizedItemArray<Column> ColumnMappings = new();
        public UnsizedBitArray32 PresentColumns = default;
        public StringBuilder StringBuilder = new();
        public ImmutableArray<Thesis>.Builder Result = ImmutableArray.CreateBuilder<Thesis>();
        public readonly List<Name> StudentNames = new();
    }

    private sealed class SearchFilter
    {
        public ImmutableArray<string> OrderedKeywords = default;
        public string? ExactString = null;
    }

    private static bool IsParen(char ch)
    {
        if (ch == ')')
        {
            return true;
        }
        if (ch == '(')
        {
            return true;
        }
        return false;
    }

    private static bool IsSep(char ch)
    {
        if (ch == '.')
        {
            return true;
        }
        if (ch == '/')
        {
            return true;
        }
        if (ch == '\\')
        {
            return true;
        }
        if (ch == '\r')
        {
            return true;
        }
        if (ch == '\n')
        {
            return true;
        }
        return false;
    }

    private static bool IsRussian(char ch)
    {
        if (ch >= 'А' && ch <= 'я')
        {
            return true;
        }
        if (ch == 'Ё' || ch == 'ё')
        {
            return true;
        }
        return false;
    }

    private readonly struct SearchSep() : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (IsSep(ch))
            {
                return false;
            }
            return true;
        }
    }

    private readonly struct SearchRussianOrSep(bool searchRussian, bool searchSeparators) : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (IsParen(ch))
            {
                return false;
            }
            if (searchSeparators && IsSep(ch))
            {
                return false;
            }
            if (searchRussian && IsRussian(ch))
            {
                return false;
            }
            return true;
        }
    }

    private struct ThesisParsingState()
    {
        public ParserPosition? RoEnd;
        public ParserPosition? RoStart;
        public ParserPosition? RuStart;
        public ParserPosition? RuEnd;
        public bool SawRussian = false;
        public bool RussianSeenInParens = false;
        public int ParenDepth = 0;

        public readonly bool HasRu => RuStart is not null;

        public readonly ThesisNames GetResult(Parser p)
        {
            return new(Ro(p), Ru(p));
        }

        private readonly ReadOnlyMemory<char> Trim(ReadOnlyMemory<char> s)
        {
            var span = s.Span;

            // BUG: if it ends on a quoted word, the closing quote is still removed

            int start = 0;
            while (start < span.Length && Check(span[start]))
            {
                start += 1;
            }

            int end = span.Length - 1;
            while (end >= start && Check(span[end]))
            {
                end -= 1;
            }

            return s[start .. (end + 1)];

            bool Check(char ch)
            {
                if (char.IsWhiteSpace(ch))
                {
                    return true;
                }
                if ("\"«»„“”‟‹›❝❞❮❯".Contains(ch))
                {
                    return true;
                }
                return false;
            }
        }

        private readonly ReadOnlyMemory<char> Slice(Parser p, ParserPosition start, ParserPosition? end)
        {
            var t = p.BufferedView();
            t.MoveTo(start);
            var end1 = end ?? t.EndPosition;
            var ret = t.SourceUntilExclusive(end1);
            ret = Trim(ret);
            return ret;
        }

        public readonly ReadOnlyMemory<char> Ro(Parser p)
        {
            if (RoStart is not { } start)
            {
                throw new InvalidOperationException("Romanian name is required");
            }
            var ret = Slice(p, start, RoEnd);
            return ret;
        }

        public readonly ReadOnlyMemory<char> Ru(Parser p)
        {
            if (RuStart is not { } start)
            {
                return null;
            }
            var ret = Slice(p, start, RuEnd);
            return ret;
        }
    }

    internal record struct ThesisNames(ReadOnlyMemory<char> Ro, ReadOnlyMemory<char> Ru);

    internal static ThesisNames ParseThesisNames(string text)
    {
        var initialParser = new Parser(text);
        initialParser.SkipWhitespace();

        var parser = initialParser.BufferedView();

        // Explicit ru: ro: syntax
        {
            var bparser = parser.BufferedView();
            string[] options = ["ro:", "ru:"];
            const int ro = 0;
            const int ru = 1;
            for (int i = ro; i <= ru; i++)
            {
                ThesisNames ResultHelper(ReadOnlyMemory<char> a, ReadOnlyMemory<char> b)
                {
                    a = a.Trim();
                    b = b.Trim();
                    if (i == ru)
                    {
                        (a, b) = (b, a);
                    }
                    return new(a, b);
                }

                var opt = options[i];
                var otherOpt = options[1 - i];
                if (!bparser.ConsumeExactString(opt))
                {
                    continue;
                }
                bparser.SkipWhitespace();

                {
                    var bparserWorkingCopy = bparser.BufferedView();
                    while (true)
                    {
                        var loopParser = bparserWorkingCopy.BufferedView();
                        var res = loopParser.Skip(new SearchSep());
                        if (!res.Satisfied)
                        {
                            break;
                        }
                        var potentialEndPos = loopParser.Position;

                        loopParser.Move();
                        loopParser.SkipWhitespace();
                        bparserWorkingCopy.MoveTo(loopParser.Position);

                        if (!loopParser.ConsumeExactString(otherOpt))
                        {
                            continue;
                        }

                        var str1 = bparser.SourceUntilExclusive(potentialEndPos);
                        var str2 = loopParser.SourceUntilEnd();
                        return ResultHelper(str1, str2);
                    }
                }

                // No matching separator, just search for the other string.
                {
                    var bparser1 = bparser.BufferedView();
                    var skipResult = bparser1.SkipUntilSequence([otherOpt]);
                    if (!skipResult.Satisfied)
                    {
                        if (i == ru)
                        {
                            throw new InvalidOperationException("Expected ro when specifying ru explicitly");
                        }
                        else
                        {
                            // this is fine
                            return new(bparser1.SourceUntilEnd(), null);
                        }
                    }

                    // Found the LANG: bit
                    var potentialEndPos = bparser1.Position;

                    bparser1.Move(otherOpt.Length);
                    bparser1.SkipWhitespace();

                    var str1 = bparser.SourceUntilExclusive(potentialEndPos);
                    var str2 = bparser1.SourceUntilEnd();
                    return ResultHelper(str1, str2);
                }
            }
        }

        ThesisParsingState state = new();
        // separator-based syntax with russian letter checks (see the tests)
        while (true)
        {
            var bparser = parser.BufferedView();
            var skipResult = bparser.Skip(
                new SearchRussianOrSep(
                    searchRussian: !state.SawRussian,
                    searchSeparators: state.ParenDepth == 0));
            if (skipResult.EndOfInput)
            {
                if (state.ParenDepth != 0)
                {
                    throw new InvalidOperationException("Unclosed parentheses");
                }
                if (!state.SawRussian)
                {
                    state.RoStart = initialParser.Position;
                    state.RoEnd = null;
                    state.RuStart = null;
                    state.RuEnd = null;
                }
                return state.GetResult(initialParser);
            }

            var x = bparser.Current;

            if (!state.SawRussian && IsRussian(x))
            {
                if (state.RuStart is null)
                {
                    state.RuStart = initialParser.Position;
                }
                state.SawRussian = true;
                state.RussianSeenInParens = state.ParenDepth > 0;
            }
            if (state.SawRussian)
            {
                Debug.Assert(state.HasRu);
            }

            void SkipForSep()
            {
                if (state.SawRussian)
                {
                    state.RuEnd = bparser.Position;
                }
                else
                {
                    state.RoStart = initialParser.Position;
                    state.RoEnd = bparser.Position;
                }
                bparser.Move();
                bparser.SkipWhitespace(); // repeated \r, \n and friends
                if (state.SawRussian)
                {
                    state.RoStart = bparser.Position;
                }
                else
                {
                    state.RuStart = bparser.Position;
                }
            }

            if (IsParen(x))
            {
                switch (x)
                {
                    case '(':
                    {
                        if (state.ParenDepth == 0 && !state.SawRussian)
                        {
                            state.RoStart = initialParser.Position;
                            state.RoEnd = bparser.Position;
                            bparser.Move();
                            bparser.SkipWhitespace(); // repeated \r, \n and friends
                            state.RuStart = bparser.Position;
                        }
                        else
                        {
                            bparser.Move();
                        }
                        state.ParenDepth += 1;
                        break;
                    }
                    case ')':
                    {
                        if (state.ParenDepth == 0)
                        {
                            throw new InvalidOperationException("Closing paren without opening paren");
                        }
                        state.ParenDepth -= 1;
                        if (state.ParenDepth == 0 && !state.SawRussian)
                        {
                            state.RuStart = null;
                            state.RoEnd = null;
                        }
                        else if (state.ParenDepth == 0 && state.RussianSeenInParens)
                        {
                            state.RuEnd = bparser.Position;
                        }
                        bparser.Move();
                        break;
                    }
                }
            }
            else if (x == '.')
            {
                // Check for whitespace after dot.
                var copy = bparser.BufferedView();
                copy.Move();
                var whitespaceRes = copy.SkipWhitespace();
                if (!whitespaceRes.SkippedAny)
                {
                    // It's just part of a word like ASP.NET
                    bparser.Move();
                }
                else
                {
                    SkipForSep();
                }
            }
            else if (IsSep(x))
            {
                SkipForSep();
            }
            else
            {
                bparser.Move();
            }

            parser.MoveTo(bparser.Position);
        }
    }
}
