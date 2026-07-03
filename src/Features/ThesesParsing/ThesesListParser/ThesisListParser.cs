using System.Collections.Immutable;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Core.Services;
using ScheduleLib.Curriculum;
using ScheduleLib.Excel.Helper;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace ScheduleLib.Theses.Parsing;

public sealed class ThesisList
{
    public required ImmutableArray<Thesis> Items;
}

public enum ThesisNameLanguage
{
    Ro,
    Ru,
    En,
}

public sealed class Thesis
{
    public required Name StudentName;
    public required Name TeacherName;
    public required string GroupName;
    public required OneForEachEnumMemberArray<ThesisNameLanguage, string?> ThesisNames;
}


internal struct ThesisInParsing()
{
    public List<Name>? TeacherName;
    public string? GroupName;
    public OneForEachEnumMemberArray<ThesisNameLanguage, string?> ThesisNames = new();
}

public enum ThesisType
{
    An,
    Licenta,
    Master,
}

public interface INameRemapper
{
    public Name RemapName(Name name);
}

public sealed class DoNothingNameRemapper : INameRemapper
{
    public Name RemapName(Name name)
    {
        return name;
    }
}

public sealed class ThesisListParser(
    INameRemapper _teacherNameRemapper,
    INameRemapper _studentNameRemapper)
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ThesisListParser>(sp =>
        {
            var mapperTeacher = sp.GetRequiredKeyedService<INameRemapper>(NameMappingKeys.Teacher);
            var mapperStudent = sp.GetRequiredKeyedService<INameRemapper>(NameMappingKeys.Student);
            return new(mapperTeacher, mapperStudent);
        });
    }

    private enum Column
    {
        Unknown = -1,
        Number,
        Group,
        StudentName,
        Mentor,
        ThesisNameRu,
        ThesisNameRo,
        ThesisNameEn,
        Count,
    }

    // public enum Token
    // {
    //     QuotationMark = '\"',
    //     Separator = '/',
    //     Semicolor = ':',
    //     Word,
    // }
    //
    // public sealed class TokenReader : ITokenReader
    // {
    //     private static readonly TokenTypeLabels Labels = LexerHelper.CreateLabels(typeof(Token));
    //
    //     public TokenType Read(ref Parser parser)
    //     {
    //     }
    // }

    public ThesisList Parse(Stream file, ThesisType targetThesisType)
    {
        using var excel = new XLWorkbook(file);

        var sheet = excel.Worksheets.FirstOrDefault(s =>
        {
            if (s.Visibility != XLWorksheetVisibility.Visible)
            {
                return false;
            }
            var comparer = IgnoreDiacriticsAndCaseComparer.Instance;
            switch (targetThesisType)
            {
                case ThesisType.An:
                    return comparer.Contains(s.Name, "an");
                case ThesisType.Licenta:
                    return comparer.Contains(s.Name, "licenta");
                case ThesisType.Master:
                    return comparer.Contains(s.Name, "master");
                default:
                    throw Unreachable();
            }
        });

        if (sheet is null)
        {
            throw new InvalidOperationException("Sheet not found");
        }

        var state = new State();
        state.Action = Action.MeaningfulHeaders;
        foreach (var row in sheet.Rows())
        {
            switch (state.Action)
            {
                case Action.MeaninglessHeaders:
                {
                    break;
                }
                case Action.MeaningfulHeaders:
                {
                    if (!TryReadHeaderRow(
                            row: row,
                            targetThesisType: targetThesisType,
                            state: ref state))
                    {
                        break;
                    }

                    state.Action = Action.Data;
                    break;
                }
                case Action.Data:
                {
                    var thesis = new ThesisInParsing();
                    var cells = row.CellsWithMergedAppearingOnce().ToArray();
                    if (cells.All(x => x.Value.IsBlank))
                    {
                        break;
                    }
                    foreach (var cell in cells)
                    {
                        try
                        {
                            if (!ParseColumn(cell, ref state, ref thesis))
                            {
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            throw cell.Exception("Error while parsing thesis list cell", ex);
                        }
                    }
                    // Won't fail if no students parsed, which is fine.
                    foreach (var studentName in state.StudentNames)
                    {
                        if (thesis.TeacherName is null
                            || thesis.TeacherName.Count == 0)
                        {
                            throw new InvalidOperationException("Teacher name is required");
                        }
                        foreach (var teacherName in thesis.TeacherName)
                        {
                            var mappedTeacherName = _teacherNameRemapper.RemapName(teacherName);
                            state.Result.Add(new()
                            {
                                GroupName = thesis.GroupName ?? throw new InvalidOperationException("Group name is required"),
                                StudentName = studentName,
                                TeacherName = mappedTeacherName,
                                ThesisNames = thesis.ThesisNames,
                            });
                        }
                    }
                    state.StudentNames.Clear();
                    break;
                }
            }
        }
        if (state.Action != Action.Data)
        {
            throw new InvalidOperationException("Header row not found");
        }
        return new ThesisList
        {
            Items = state.Result.DrainToImmutable(),
        };
    }

    private static bool TryReadHeaderRow(
        IXLRow row,
        ThesisType targetThesisType,
        ref State state)
    {
        var columnMappings = new SizedItemArray<Column>();
        var presentColumns = new EnumBitArray<Column>();
        foreach (var cell in row.CellsWithMergedAppearingOnce())
        {
            if (!cell.TryGetValue(out string text))
            {
                continue;
            }

            var column = MatchColumn(text);
            var range = cell.ActualRange();
            if (range.RowCount() != 1)
            {
                throw cell.Exception("Row count > 1");
            }
            columnMappings.AddAt(
                range.FirstColumn().ColumnNumber(),
                new(column, range.ColumnCount()));
            if (column != Column.Unknown)
            {
                presentColumns.Set(column);
            }
        }

        var missingRequiredColumns = presentColumns
            .Flipped
            .Intersect(GetRequiredHeaderColumns(targetThesisType));
        if (missingRequiredColumns.AreAnySet)
        {
            return false;
        }

        state.ColumnMappings.Clear();
        foreach (var columnMapping in columnMappings)
        {
            state.ColumnMappings.Add(columnMapping);
        }
        state.PresentColumns = presentColumns;
        return true;
    }

    private static EnumBitArray<Column> GetRequiredHeaderColumns(ThesisType targetThesisType)
    {
        var requiredColumns = EnumBitArray<Column>.AllSet;
        requiredColumns.Clear(Column.ThesisNameEn);

        return requiredColumns;
    }

    private bool ParseColumn(
        IXLCell cell,
        ref State state,
        ref ThesisInParsing thesis)
    {
        var range = cell.ActualRange();
        if (!state.ColumnMappings.TryFind(range.FirstColumn().ColumnNumber(), out var column))
        {
            return false;
        }

        string? GetStringForName()
        {
            if (!cell.TryGetValue(out string text))
            {
                return null;
            }
            text = ConfusableNamesHelper.ReplaceConfusableRussianChars(text);
            return text;
        }

        switch (column)
        {
            case Column.Number:
            {
                return true;
            }

            case Column.StudentName:
            {
                if (GetStringForName() is not { } text)
                {
                    return false;
                }
                var parser = new Parser(text);
                try
                {
                    while (true)
                    {
                        parser.SkipWhitespace();
                        while (TrySkipParenthesizedText(ref parser))
                        {
                            parser.SkipWhitespace();
                        }
                        if (parser.IsEmpty)
                        {
                            break;
                        }

                        var studentName = NameHelper.Parse(ref parser);
                        studentName = _studentNameRemapper.RemapName(studentName);
                        state.StudentNames.Add(studentName);
                        if (!parser.SkipWhitespace().SkippedAny)
                        {
                            break;
                        }
                    }
                }
                catch (NameParsingException) when (state.StudentNames.Count == 0)
                {
                    return false;
                }
                if (!parser.IsEmpty)
                {
                    throw new InvalidOperationException("Parser not empty after student name");
                }
                if (state.StudentNames.Count == 0)
                {
                    return false;
                }
                break;
            }
            case Column.Mentor:
            {
                if (GetStringForName() is not { } text)
                {
                    return false;
                }
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }
                thesis.TeacherName = ParseNames(text);
                break;
            }
            case Column.Group:
            {
                if (!cell.TryGetValue(out string text))
                {
                    return false;
                }
                thesis.GroupName = text.Trim();
                break;
            }
            case Column.ThesisNameRu
                or Column.ThesisNameRo
                or Column.ThesisNameEn:
            {
                var lang = column switch
                {
                    Column.ThesisNameRu => ThesisNameLanguage.Ru,
                    Column.ThesisNameRo => ThesisNameLanguage.Ro,
                    Column.ThesisNameEn => ThesisNameLanguage.En,
                    _ => throw Unreachable(),
                };
                if (!cell.TryGetValue(out string text))
                {
                    return false;
                }
                thesis.ThesisNames[lang] = text;
                break;
            }
        }
        return true;
    }

    private static bool TrySkipParenthesizedText(ref Parser parser)
    {
        if (parser.IsEmpty || parser.Current != '(')
        {
            return false;
        }

        var bparser = parser.BufferedView();
        if (!bparser.SkipUntilAny(")").Satisfied)
        {
            return false;
        }

        parser.MovePast(bparser.Position);
        return true;
    }

    private static List<Name> ParseNames(string text)
    {
        var parser = new Parser(text);
        while (true)
        {
            var ret = new List<Name>();

            parser.SkipWhitespace();
            var name = NameHelper.Parse(ref parser);
            ret.Add(name);
            parser.SkipWhitespace();

            if (parser.ConsumeExactChar(','))
            {
                continue;
            }
            if (!parser.IsEmpty)
            {
                throw new InvalidOperationException("Parser not empty after name");
            }
            return ret;
        }
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
                    if (parser.IsEmpty)
                    {
                        return false;
                    }

                    var bparser = parser.BufferedView();
                    if (!bparser.SkipNotWhitespace().SkippedAny)
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
                OrderedKeywords = ["nr."],
            });
            b.Set(Column.Group, new()
            {
                OrderedKeywords = ["grupa"],
            });
            b.Set(Column.StudentName, new()
            {
                OrderedKeywords = ["student"],
            });
            b.Set(Column.Mentor, new()
            {
                OrderedKeywords = ["conducatorului", "stiintific"],
            });
            b.Set(Column.ThesisNameRu, new()
            {
                OrderedKeywords = ["rusa"],
            });
            b.Set(Column.ThesisNameRo, new()
            {
                OrderedKeywords = ["romana"],
            });
            b.Set(Column.ThesisNameEn, new()
            {
                OrderedKeywords = ["engleza"],
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
        public EnumBitArray<Column> PresentColumns = default;
        public ImmutableArray<Thesis>.Builder Result = ImmutableArray.CreateBuilder<Thesis>();
        public readonly List<Name> StudentNames = new();
    }

    private sealed class SearchFilter
    {
        public ImmutableArray<string> OrderedKeywords = default;
        public string? ExactString = null;
    }
}

file static class ConfusableNamesHelper
{
    private static readonly IReadOnlyDictionary<char, char> Confusable = new Dictionary<char, char>
    {
        ['А']='A', ['а']='a',
        ['В']='B',
        ['Е']='E', ['е']='e',
        ['К']='K',
        ['М']='M',
        ['Н']='H',
        ['О']='O', ['о']='o',
        ['Р']='P', ['р']='p',
        ['С']='C', ['с']='c',
        ['Т']='T',
        ['Х']='X', ['х']='x',
        ['у']='y',
    };

    public static string ReplaceConfusableRussianChars(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Confusable.TryGetValue(chars[i], out var latin))
            {
                chars[i] = latin;
            }
        }

        return new string(chars);
    }
}
