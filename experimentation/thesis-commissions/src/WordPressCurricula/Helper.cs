using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using AngleSharp.Dom;
using ScheduleLib;

namespace WordPressCurricula;

using ScheduleLib.Parsing;

internal static class Helper
{
    public static Curriculum ParseCurriculum(string name)
    {
        var parser = new Parser(name);
        var bparser = parser.BufferedView();

        bool hasCode = Helper.SkipCode(ref bparser);
        if (!hasCode)
        {
            var normalizedName = Helper.NormalizeName(name);
            return new Curriculum
            {
                Code = new(null),
                Name = normalizedName,
            };
        }

        {
            var code = parser.SourceUntilExclusive(bparser.Position);
            var codeStr = code.ToString();
            var normalizedCode = Helper.NormalizeCode(codeStr);

            parser.MovePast(bparser.Position);
            var name1 = parser.SourceUntilEnd();
            var nameStr = name1.ToString();
            var normalizedName = Helper.NormalizeName(nameStr);

            return new()
            {
                Code = normalizedCode,
                Name = normalizedName,
            };
        }
    }

    public static Code NormalizeCode(string s)
    {
        var withStandardChars = s.Replace("_", ".");
        return new(withStandardChars);
    }

    public static string NormalizeName(string name)
    {
        var withStandardChars = name.Replace("_", " ");
        return withStandardChars;
    }

    private struct SkipSeparator : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (ch == ' ')
            {
                return true;
            }
            if (IsCodeSeparator(ch))
            {
                return true;
            }
            return false;
        }
    }

    public static bool IsCodeSymbol(char ch)
    {
        if (char.IsDigit(ch))
        {
            return true;
        }
        if (IsCodeSeparator(ch))
        {
            return true;
        }
        return false;
    }

    public static bool IsCodeSeparator(char ch)
    {
        if (ch is '_')
        {
            return true;
        }
        if (ch is '.')
        {
            return true;
        }
        return false;
    }

    public static bool SkipCode(ref Parser parser)
    {
        var bparser = parser.BufferedView();
        bool skippedAny = false;
        bool foundNumberInThisSegment = false;

        bool Return(ref Parser parser)
        {
            if (!skippedAny)
            {
                return false;
            }
            if (!foundNumberInThisSegment)
            {
                return true;
            }
            parser.MoveTo(bparser.Position);
            return true;
        }

        while (true)
        {
            if (bparser.IsEmpty)
            {
                throw new InvalidOperationException("No name");
            }
            if (bparser.Current == ' ')
            {
                return Return(ref parser);
            }
            if (!Helper.IsCodeSymbol(bparser.Current))
            {
                return Return(ref parser);
            }
            if (Helper.IsCodeSeparator(bparser.Current))
            {
                if (!skippedAny)
                {
                    throw new InvalidOperationException("Separator cannot be the first symbol");
                }
                if (!foundNumberInThisSegment)
                {
                    throw new InvalidOperationException("Empty segment not allowed");
                }

                parser.MoveTo(bparser.Position);
                foundNumberInThisSegment = false;
            }
            if (char.IsDigit(bparser.Current))
            {
                foundNumberInThisSegment = true;
            }

            bparser.Move();
            skippedAny = true;
        }
    }

    private struct SkipDigitOrSeparator : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (char.IsDigit(ch))
            {
                return true;
            }
            if (IsCodeSeparator(ch))
            {
                return true;
            }
            if (ch == wordSeparator)
            {
                return true;
            }
            return false;
        }
    }

    private const char wordSeparator = ' ';

    internal static List<CurriculumInfo> ParseCurriculumFiles(string curriculaDir)
    {
        var ret = new List<CurriculumInfo>();

        string GetLastSegment(string path)
        {
            var lastIndex = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;
            return path[lastIndex ..];
        }

        foreach (var facultyNameDir in Directory.EnumerateDirectories(curriculaDir))
        {
            var facultyName = GetLastSegment(facultyNameDir);
            foreach (var programNameDir in Directory.EnumerateDirectories(facultyNameDir))
            {
                var programName = GetLastSegment(programNameDir);
                foreach (var specialtyNameFile in Directory.EnumerateFiles(programNameDir))
                {
                    var specialtyWithCode = GetLastSegment(specialtyNameFile);
                    specialtyWithCode = Path.GetFileNameWithoutExtension(specialtyWithCode);

                    var curriculum = Helper.ParseCurriculum(specialtyWithCode);

                    ret.Add(new()
                    {
                        FilePath = specialtyNameFile,
                        Curriculum = curriculum,
                        Faculty = facultyName,
                        Program = programName,
                    });
                }
            }
        }
        return ret;
    }

    public static HtmlCurriculum ParseCurriculumInHtml(HtmlCurriculumParsingParams @params)
    {
        using var sourceE = @params.Source.GetEnumerator();
        var state = new HtmlCurriculumParsingState
        {
            Source = sourceE,
        };
        if (!state.NextParser())
        {
            throw new InvalidOperationException("Empty source");
        }
        var ret = ParseCodeOrName(ref state);
        return ret;

    }

    internal static ImmutableArray<Code> ParseCodes(ref HtmlCurriculumParsingState state)
    {
        if (!state.EnsureParserSet())
        {
            throw new InvalidOperationException("bang!");
        }

        var bparser = state.Parser.BufferedView();
        var codeState = new CodeParsingState();
        var ret = ImmutableArray.CreateBuilder<Code>();
        var currentCodeStartParser = state.Parser.BufferedView();

        bool IsNoCode()
        {
            return codeState == default && ret.Count == 0;
        }

        void EndCurrent()
        {
            if (!codeState.IsNumber)
            {
                throw new InvalidOperationException("Code must start with a number");
            }
            if (!codeState.HadSeparator)
            {
                throw new InvalidOperationException("Code must be made up of two parts");
            }

            codeState.Reset();

            var code = currentCodeStartParser.PeekSpanUntilPosition(bparser.Position);
            var codeStr = code.ToString();
            ret.Add(new(codeStr));
        }

        while (true)
        {
            if (bparser.IsEmpty)
            {
                if (!state.NextParser())
                {
                    throw new InvalidOperationException("Name must follow code");
                }
                if (!codeState.IsNumber)
                {
                    throw UnreachableHelper.Unreachable();
                }

                EndCurrent();
                break;
            }

            if (char.IsNumber(bparser.Current))
            {
                codeState.IsNumber = true;
                bparser.Move();
                continue;
            }

            if (bparser.Current == '.')
            {
                if (!codeState.IsNumber)
                {
                    throw new InvalidOperationException("Number must precede '.' in code");
                }
                if (codeState.HadSeparator)
                {
                    throw new InvalidOperationException("Separator appeared twice");
                }

                codeState.HadSeparator = true;
                codeState.IsNumber = false;
                bparser.Move();
                continue;
            }

            if (bparser.Current == '/')
            {
                EndCurrent();
                bparser.Move();
                currentCodeStartParser = bparser;
                continue;
            }

            if (char.IsWhiteSpace(bparser.Current))
            {
                EndCurrent();
                bparser.Move();
                break;
            }

            if (IsNoCode())
            {
                break;
            }
        }
        state.Parser.MoveTo(bparser.Position);

        return ret.DrainToImmutable();
    }

    internal static HtmlCurriculum ParseCodeOrName(ref HtmlCurriculumParsingState state)
    {
        var codes = ParseCodes(ref state);

        while (true)
        {
            if (!state.EnsureParserSet())
            {
                throw new InvalidOperationException("Name must follow code");
            }
            if (!state.Parser.SkipWhitespace().EndOfInput)
            {
                break;
            }
        }

        var sb = state.StringBuilder;
        bool isWhitespace = false;
        bool isNextModifiers = false;

        while (true)
        {
            if (!state.EnsureParserSet())
            {
                break;
            }

            var bparser = state.Parser.BufferedView();

            var skipResult = bparser.Skip(new SkipNotWhitespaceOrParen());
            _ = skipResult;
            var word = state.Parser.PeekSpanUntilPosition(bparser.Position);

            bool appendedSpace = sb.Length > 0 && isWhitespace && word.Length > 0;
            if (appendedSpace)
            {
                sb.Append(' ');
            }
            sb.Append(word);

            if (bparser.IsEmpty)
            {
                state.Parser.MoveTo(bparser.Position);
                break;
            }

            bool isCurrentWhitespace = char.IsWhiteSpace(bparser.Current);
            if (isCurrentWhitespace)
            {
                bparser.Move();
            }

            state.Parser.MoveTo(bparser.Position);

            if (appendedSpace)
            {
                isWhitespace = false;
            }
            else if (isCurrentWhitespace)
            {
                isWhitespace = true;
            }

            if (isCurrentWhitespace)
            {
                continue;
            }

            Debug.Assert(bparser.Current == '(');
            state.Parser.Move();
            isNextModifiers = true;
            break;
        }

        var name = sb.ToStringAndClear();

        var attendanceModifiers = ImmutableArray<AttendanceLink>.Empty;
        if (isNextModifiers)
        {
            attendanceModifiers = ParseAttendanceModifiers(ref state);
        }

        return new()
        {
            Codes = codes,
            AttendanceLinks = attendanceModifiers,
            Name = name,
        };
    }

    private struct AttendanceModifiersParsingState()
    {
        public ImmutableArray<IElement>.Builder Spans = ImmutableArray.CreateBuilder<IElement>();
        public int StartPos = default;
        public int EndPos = default;
        public bool ExpectNext = true;
    }

    internal static ImmutableArray<AttendanceLink> ParseAttendanceModifiers(ref HtmlCurriculumParsingState state)
    {
        var sb = state.StringBuilder;
        var ret = ImmutableArray.CreateBuilder<AttendanceLink>();
        var parsingState = new AttendanceModifiersParsingState();

        while (true)
        {
            if (!state.EnsureParserSet())
            {
                if (parsingState.ExpectNext)
                {
                    throw new InvalidOperationException("Expecting next modifier");
                }
                break;
            }

            if (sb.Length == 0)
            {
                state.Parser.SkipWhitespace();
            }

            var bparser = state.Parser.BufferedView();
            var skipResult = bparser.SkipUntilAny(",)");
            Debug.Assert(skipResult.SkippedAny);

            if (sb.Length == 0)
            {
                parsingState.StartPos = state.Parser.Position.Index;
            }
            parsingState.Spans.Add(state.CurrentElement);

            if (skipResult.EndOfInput)
            {
                var s = state.Parser.PeekSpanUntilEnd();
                parsingState.EndPos = state.Parser.EndPosition.Index;
                sb.Append(s);
                continue;
            }

            var text = state.Parser.PeekSpanUntilPosition(bparser.Position);
            if (sb.Length != 0)
            {
                sb.Append(text);
                text = sb.ToStringAndClear();
            }

            if (skipResult.SkippedAny)
            {
                parsingState.EndPos = state.Parser.Position.Index;
            }

            bool isUselessInfo = text.Contains("credite", StringComparison.OrdinalIgnoreCase);
            if (!isUselessInfo)
            {
                Debug.Assert(parsingState.Spans.Count != 0);
                var attendanceMode = ParseAttendanceMode(text);
                ret.Add(new()
                {
                    AttendanceMode = attendanceMode,
                    LinkSequence = new()
                    {
                        HtmlElements = parsingState.Spans.DrainToImmutable(),
                        EndIndexExclusive = parsingState.EndPos,
                        StartIndex = parsingState.StartPos,
                    },
                });
            }

            if (skipResult.SkippedAny)
            {
                state.Parser.MoveTo(bparser.Position);
            }

            switch (bparser.Current)
            {
                case ')':
                    parsingState.ExpectNext = false;
                    break;
                case ',':
                    parsingState.ExpectNext = true;
                    break;
                default:
                    throw UnreachableHelper.Unreachable();
            }
            continue;
        }

        return ret.DrainToImmutable();
    }


    private static AttendanceMode ParseAttendanceMode(ReadOnlySpan<char> text)
    {
        if (Equal(text, "studii cu frecvență"))
        {
            return AttendanceMode.Zi;
        }
        if (Equal(text, "studii cu frecvență redusă"))
        {
            return AttendanceMode.FR;
        }
        if (Equal(text, "studii duale"))
        {
            return AttendanceMode.Dual;
        }
        if (Equal(text, "studii la distanță"))
        {
            return AttendanceMode.Distance;
        }
        throw new InvalidOperationException($"Unkonwn label {text}");

        static bool Equal(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            return IgnoreDiacriticsAndCaseComparer.Instance.Equals(a, b);
        }
    }


    private struct SkipNotWhitespaceOrParen : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (char.IsWhiteSpace(ch))
            {
                return false;
            }
            if (ch == '(')
            {
                return false;
            }
            return true;
        }
    }

    private record struct CodeParsingState()
    {
        public bool IsNumber = false;
        public bool HadSeparator = false;

        public void Reset()
        {
            this = new();
        }
    }
}

public struct HtmlCurriculum
{
    public required string Name;
    public required ImmutableArray<Code> Codes;
    public required ImmutableArray<AttendanceLink> AttendanceLinks;
}

public struct HtmlCurriculumInParsing()
{
    public required string Name;
    public required ImmutableArray<Code>.Builder Codes;
    public required ImmutableArray<AttendanceLink>.Builder AttendanceLinks;
}

public struct AttendanceLink
{
    public required LinkSequence LinkSequence;
    public required AttendanceMode AttendanceMode;
}

public enum AttendanceMode
{
    Zi,
    FR,
    Distance,
    Dual,
}

public struct LinkSequence
{
    public required ImmutableArray<IElement> HtmlElements;
    public required int StartIndex;
    public required int EndIndexExclusive;
}

public record struct HtmlTextWithLink(string Text, IElement Element);

public struct HtmlCurriculumParsingParams
{
    public required IEnumerable<HtmlTextWithLink> Source;
}

public struct HtmlCurriculumParsingState()
{
    public required IEnumerator<HtmlTextWithLink> Source;
    public StringBuilder StringBuilder = new();
    public Parser Parser = new("");

    public readonly IElement CurrentElement => Source.Current.Element;

    public bool NextParser()
    {
        if (!Source.MoveNext())
        {
            return false;
        }

        Parser = new(Source.Current.Text);
        return true;
    }
    public bool EnsureParserSet()
    {
        while (Parser.IsEmpty)
        {
            bool ret = NextParser();
            return ret;
        }
        return true;
    }
}

public struct Curriculum
{
    public required string Name;
    public required MaybeCode Code;
}

public record struct MaybeCode(string? Value)
{
    public override string? ToString()
    {
        return Value;
    }

    public static implicit operator MaybeCode(Code value) => new(value.Value);

    public Code? AsCode()
    {
        if (Value is null)
        {
            return null;
        }
        return new(Value);
    }
}

public record struct Code(string Value)
{
    public override string? ToString()
    {
        return Value;
    }
}

public enum QualificationId
{
    Licenta,
    LicentaDistanta,
    LicentaFR,
    Master,
    MasterDistanta,
    MasterFR,
}
