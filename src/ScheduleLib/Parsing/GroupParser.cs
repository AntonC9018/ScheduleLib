using System.Diagnostics;

namespace ScheduleLib.Parsing.GroupParser;

public sealed class GroupParseContext
{
    // Must be modulo 100
    public required int CurrentStudyYear;

    public struct Params
    {
        public required int CurrentStudyYear;
    }

    public static GroupParseContext Create(Params p)
    {
        int year = p.CurrentStudyYear % 100;
        return new()
        {
            CurrentStudyYear = year,
        };
    }

    public Grade DetermineGrade(int year)
    {
        var ret = CurrentStudyYear - year + 1;
        return new(ret);
    }
}

public static class GroupHelper
{
    public static Group Parse(this GroupParseContext context, string name)
    {
        // M2401(ro)
        // IAFR2402
        // IAFR2403 R
        if (name.Length < 5)
        {
            throw new ArgumentException("The minimum length of the name is 5", paramName: nameof(name));
        }

        var baseParser = new Parser(name);
        var parser = baseParser.BufferedView();

        var (label, isFr, isMaster) = ParseLabel(ref parser);
        var qualificationType = isMaster ? QualificationType.Master : QualificationType.Licenta;
        int year = ParseYear(ref parser);
        var grade = context.DetermineGrade(year); // no validation for now.
        int groupNumber = ParseGroup(ref parser);
        var actualName = baseParser.PeekSpanUntilPosition(parser.Position).ToString();
        var language = ParseLanguage();

        return new()
        {
            Faculty = new(label),
            GroupNumber = groupNumber,
            Grade = grade,
            Language = language,
            Name = actualName,
            QualificationType = qualificationType,
            AttendanceMode = isFr ? AttendanceMode.FrecventaRedusa : AttendanceMode.Zi,
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
                if (ParserHelper.IsUpper(ch))
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
                if (ParserHelper.IsLower(ch))
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
        if (!ParserHelper.IsUpper(bparser.Current))
        {
            throw new InvalidOperationException("Must be prefixed with at least one letter indicating the group.");
        }
        bparser.Move();

        bool isMaybeMaster = false;
        bool isFr = false;

        if (bparser.Current == 'M')
        {
            isMaybeMaster = true;
        }

        while (true)
        {
            if (bparser.IsEmpty)
            {
                throw new InvalidOperationException("After the label, it must include a number!");
            }

            if (bparser.CanPeekCount(2))
            {
                var chars = bparser.PeekSpan(2);
                if (chars[0] == 'F' && chars[1] == 'R' && !ParserHelper.IsUpper(chars[2]))
                {
                    isFr = true;
                }
            }

            if (!ParserHelper.IsUpper(bparser.Current))
            {
                break;
            }

            bparser.Move();
        }

        bool isMaster1 = false;
        // More than 1 letter in label
        if (bparser.Position - parser.Position > 1)
        {
            isMaster1 = isMaybeMaster;
            if (isMaster1)
            {
                parser.Move();
            }
        }

        var label1 = parser.PeekSpanUntilPosition(bparser.Position);
        parser.MoveTo(bparser.Position);
        return (label1.ToString(), isFr, isMaster1);
    }

    public const int GroupNumberLen = 2;
    private static int ParseGroup(ref Parser parser)
    {
        var result = parser.ConsumePositiveInt(GroupNumberLen);
        switch (result.Status)
        {
            case ConsumeIntStatus.Ok:
            {
                return (int) result.Value;
            }
            case ConsumeIntStatus.InputTooShort:
            {
                throw new InvalidOperationException($"String must include {GroupNumberLen} letters of the group after the year.");
            }
            case ConsumeIntStatus.NotAnInteger:
            {
                throw new InvalidOperationException($"Must be a valid number that has {GroupNumberLen} letters.");
            }
            default:
            {
                Debug.Fail("Unreachable");
                return 0;
            }
        }
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
                Debug.Fail("Unreachable");
                return 0;
            }
        }
    }
}
