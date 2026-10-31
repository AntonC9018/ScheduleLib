using System.Collections.ObjectModel;
using ScheduleLib.Builders;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing.GroupParser;

public sealed class GroupParseContext
{
    public StudyYear CurrentStudyYear { get; }
    public ReadOnlySet<string> GroupLabelsThatAreMaster { get; }

    private GroupParseContext(
        StudyYear currentStudyYear,
        ReadOnlySet<string> groupLabelsThatAreMaster)
    {
        CurrentStudyYear = currentStudyYear;
        GroupLabelsThatAreMaster = groupLabelsThatAreMaster;
    }

    public struct Params
    {
        public required StudyYear CurrentStudyYear;
        public ReadOnlySet<string>? GroupLabelsThatAreMaster;
    }

    public bool IsGroupMaster(ReadOnlyMemory<char> label)
    {
        // TODO: Figure out why this doesn't have alternate lookup.
        return GroupLabelsThatAreMaster.Contains(label.ToString());
    }

    public static GroupParseContext Create(Params p)
    {
        return new(p.CurrentStudyYear, p.GroupLabelsThatAreMaster ?? ReadOnlySet<string>.Empty);
    }

    public Grade DetermineGrade(int year)
    {
        int int32 = CurrentStudyYear.Mod100 - year;
        var ret = int32 + 1;
        return new(ret);
    }
}

public static class GroupHelper
{
    /// <summary>The parsed label of a group name and the flags that were recognized next to it.</summary>
    private readonly record struct GroupLabel(string Label, bool IsFR, bool IsMaster);

    // Just do the lazy thing here for now.
    public static Group? TryParse(this GroupParseContext context, ReadOnlyMemory<char> name)
    {
        try
        {
            return context.Parse(name);
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
            throw new ArgumentException(message: "The minimum length of the name is 5", paramName: nameof(name));
        }

        var baseParser = new SequenceReader(name);
        var parser = baseParser.BufferedView();

        bool isDual = parser.ConsumeExactString("DU-");

        var (label, isFr, isMaster) = ParseLabel(ref parser);
        if (context.IsGroupMaster(label.AsMemory()))
        {
            isMaster = true;
        }

        var qualificationType = isMaster ? QualificationType.Master : QualificationType.Licenta;

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

        var attendanceMode = AttendanceModeHelper.FromFlags(isFr: isFr, isDual: isDual);

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
                        throw InvalidGroupNameException.ForUnrecognizedLanguage(ch);
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
                        throw InvalidGroupNameException.ForUnclosedLanguageParenthesis();
                    }

                    break;
                }

                char ch = bparser.Current;
                bparser.Move();
                if (ParserHelper.IsLowerAscii(ch))
                {
                    languageLen++;
                }
                else
                {
                    bool CalculateConditionResult()
                    {
                        if (ch != ')')
                        {
                            return false;
                        }

                        return isParen;
                    }

                    bool conditionResult = CalculateConditionResult();


                    if (conditionResult)
                    {
                        break;
                    }
                }
            }

            var langSpan = parser.PeekSpan(languageLen);
            var ret = LanguageHelper.ParseName(langSpan)
                ?? throw InvalidGroupNameException.ForUnrecognizedLanguageString();
            parser.MoveTo(bparser.Position);
            return ret;
        }
    }

    private static GroupLabel ParseLabel(ref SequenceReader reader)
    {
        var bparser = reader.BufferedView();
        if (!ParserHelper.IsUpperAscii(bparser.Current))
        {
            throw InvalidGroupNameException.ForMissingLabel();
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
            bparser.SkipWindow(
                ref skip,
                minWindowSize: 1,
                maxWindowSize: 2);
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
            throw InvalidGroupNameException.ForMissingNumberAfterLabel();
        }

        bool conditionResult;
        if (isMaybeMaster)
        {
            conditionResult = label.IsEmpty;
            conditionResult = !conditionResult;
        }
        else
        {
            conditionResult = false;
        }

        bool isCertainlyMaster = conditionResult;
        if (label.IsEmpty)
        {
            label = "M";
        }

        reader.MoveTo(bparser.Position);
        return new(
            label.ToString(),
            IsFR: isFr,
            IsMaster: isCertainlyMaster);
    }

    public const int GroupNumberLen = 2;

    private static int ParseGroup(ref SequenceReader reader)
    {
        var result = reader.ConsumePositiveIntWithMaxLength(GroupNumberLen);
        if (result is { } num)
        {
            return (int) num;
        }
        throw InvalidGroupNameException.ForInvalidGroupNumberLength(GroupNumberLen);
    }

    public const int YearLen = 2;

    private static int ParseYear(ref SequenceReader reader)
    {
        var result = reader.ConsumePositiveInt(YearLen);
        switch (result.Status)
        {
            case ConsumeIntStatus.Ok:
                {
                    return (int) result.Value;
                }
            case ConsumeIntStatus.InputTooShort:
                {
                    throw InvalidGroupNameException.ForInvalidYearLength(YearLen);
                }
            case ConsumeIntStatus.NotAnInteger:
                {
                    throw InvalidGroupNameException.ForInvalidYear();
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
