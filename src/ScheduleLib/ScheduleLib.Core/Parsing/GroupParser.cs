using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing.GroupParser;

public sealed class GroupParseContext
{
    // Must be modulo 100
    public int CurrentStudyYear { get; }

    private GroupParseContext(int currentStudyYear)
    {
        CurrentStudyYear = currentStudyYear;
    }

    public struct Params
    {
        public required int CurrentStudyYear;
    }

    public static GroupParseContext Create(Params p)
    {
        int year = p.CurrentStudyYear % 100;
        return new(year);
    }

    public Grade DetermineGrade(int year)
    {
        var ret = CurrentStudyYear - year + 1;
        return new(ret);
    }
}

public static class GroupHelper
{
    // Just do the lazy thing here for now.
    public static Group? TryParse(this GroupParseContext context, ReadOnlyMemory<char> name)
    {
        try
        {
            return Parse(context, name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Group Parse(this GroupParseContext context, ReadOnlyMemory<char> name)
    {
        // M2401(ro)
        // IAFR2402
        // IAFR2403 R
        // SMMSPA 2401
        // MIA 2402 (ru)
        if (name.Length < 5)
        {
            throw new ArgumentException("The minimum length of the name is 5", paramName: nameof(name));
        }

        var baseParser = new Parser(name);
        var parser = baseParser.BufferedView();

        bool isDual = parser.ConsumeExactString("DU-");

        var (label, isFr, isMaster) = ParseLabel(ref parser);
        var qualificationType = isMaster ? QualificationType.Master : QualificationType.Licenta;

        if (isFr && isDual)
        {
            throw new InvalidOperationException("Both FR and DUAL parsed, not allowed.");
        }

        parser.SkipWhitespace();

        int year = ParseYear(ref parser);
        var grade = context.DetermineGrade(year);
        int groupNumber = ParseGroup(ref parser);
        _ = groupNumber;

        string actualName;
        {
            string masterString = isMaster ? "M" : "";
            string frString = isFr ? "FR" : "";
            string faculty = label;
            string dualStr = isDual ? "DU-" : "";
            actualName = $"{dualStr}{masterString}{faculty}{frString}{year:00}{groupNumber:00}";
        }

        parser.SkipWhitespace();
        var language = ParseLanguage();

        AttendanceMode attendanceMode;
        if (isFr)
        {
            attendanceMode = AttendanceMode.FrecventaRedusa;
        }
        else if (isDual)
        {
            attendanceMode = AttendanceMode.Dual;
        }
        else
        {
            attendanceMode = AttendanceMode.Zi;
        }

        return new()
        {
            Faculty = new(label),
            GroupNumber = groupNumber,
            Grade = grade,
            Language = language,
            Name = actualName,
            QualificationType = qualificationType,
            AttendanceMode = attendanceMode,
        };


        Language ParseLanguage()
        {
            parser.SkipWhitespace();
            if (parser.IsEmpty)
            {
                return Language.Ro;
            }

            {
                char ch = parser.Current;
                if (ParserHelper.IsUpperAscii(ch))
                {
                    if (ch != 'R')
                    {
                        throw new InvalidOperationException($"Unrecognized language: {ch}");
                    }
                    parser.Move();
                    return Language.Ru;
                }
            }

            bool isParen = false;
            if (parser.Current == '(')
            {
                parser.Move();
                isParen = true;
            }

            var bparser = parser.BufferedView();
            int languageLen = 0;

            while (true)
            {
                if (bparser.IsEmpty)
                {
                    if (isParen)
                    {
                        throw new InvalidOperationException("Unclosed parenthesis in the language.");
                    }

                    break;
                }

                char ch = bparser.Current;
                bparser.Move();
                if (ParserHelper.IsLowerAscii(ch))
                {
                    languageLen++;
                }
                else if (ch == ')' && isParen)
                {
                    break;
                }
            }

            var langSpan = parser.PeekSpan(languageLen);
            var ret = LanguageHelper.ParseName(langSpan)
                ?? throw new InvalidOperationException($"Unrecognized language string");
            parser.MoveTo(bparser.Position);
            return ret;
        }
    }

    private static (string Label, bool IsFR, bool IsMaster) ParseLabel(ref Parser parser)
    {
        var bparser = parser.BufferedView();
        if (!ParserHelper.IsUpperAscii(bparser.Current))
        {
            throw new InvalidOperationException("Must be prefixed with at least one letter indicating the group.");
        }

        bool isMaybeMaster = false;
        if (bparser.ConsumeExactChar('M'))
        {
            isMaybeMaster = true;
        }

        bool isFr = false;
        if (bparser.ConsumeExactString("FR", StringComparison.OrdinalIgnoreCase))
        {
            isFr = true;
        }

        var labelParser = bparser.BufferedView();
        ReadOnlySpan<char> label;

        if (!isFr)
        {
            // Skip until the label is ending.
            var skip = new SkipLabelUntilFR();
            bparser.SkipWindow(ref skip, minWindowSize: 1, maxWindowSize: 2);
            isFr = skip.IsFr;

            label = labelParser.PeekSpanUntilPosition(bparser.Position);

            if (isFr)
            {
                const int frLen = 2;
                bparser.Move(frLen);
            }
        }
        else
        {
            bparser.Skip(new SkipLabel());
            label = labelParser.PeekSpanUntilPosition(bparser.Position);
        }

        bparser.SkipWhitespace();

        if (bparser.IsEmpty
            || IsLabelChar(bparser.Current))
        {
            throw new InvalidOperationException("After the label, it must include a number!");
        }

        bool isCertainlyMaster = isMaybeMaster && !label.IsEmpty;
        if (label.IsEmpty)
        {
            label = "M";
        }

        parser.MoveTo(bparser.Position);
        return (label.ToString(), isFr, isCertainlyMaster);
    }

    public const int GroupNumberLen = 2;
    private static int ParseGroup(ref Parser parser)
    {
        var result = parser.ConsumePositiveIntWithMaxLength(GroupNumberLen);
        if (result is { } num)
        {
            return (int) num;
        }
        throw new InvalidOperationException($"String must include {GroupNumberLen} letters of the group after the year.");
    }

    public const int YearLen = 2;
    private static int ParseYear(ref Parser parser)
    {
        var result = parser.ConsumePositiveInt(YearLen);
        switch (result.Status)
        {
            case ConsumeIntStatus.Ok:
            {
                return (int) result.Value;
            }
            case ConsumeIntStatus.InputTooShort:
            {
                throw new InvalidOperationException("String must include 2 letters of the year after the label.");
            }
            case ConsumeIntStatus.NotAnInteger:
            {
                throw new InvalidOperationException("Must be a valid year that has 2 letters.");
            }
            default:
            {
                throw Unreachable();
            }
        }
    }

    private static bool IsLabelChar(char ch)
    {
        return char.IsUpper(ch);
    }

    private struct SkipLabel : IShouldSkip
    {
        public bool ShouldSkip(char ch) => IsLabelChar(ch);
    }

    private struct SkipLabelUntilFR : IShouldSkipSequence
    {
        public bool IsFr { readonly get; private set; }

        public bool ShouldSkip(ReadOnlySpan<char> window)
        {
            if (!IsLabelChar(window[0]))
            {
                return false;
            }
            if (window.Length < 2)
            {
                return true;
            }
            if (window is "FR")
            {
                IsFr = true;
                return false;
            }
            return true;
        }
    }
}
