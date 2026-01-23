using System.Diagnostics.CodeAnalysis;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry;

public static partial class RegistryScraping
{
    // here, the format is different
    // DJ2302ru(II)
    // DJ2301
    // IA2401fr

    // wildcard syntax:
    // IA24(GA2D)ru
    internal static GroupForSearch ParseGroupFromOnlineRegistry(
        GroupParseContext context,
        string s)
    {
        var mainParser = new Parser(s);
        mainParser.SkipWhitespace();

        bool isRepeat = mainParser.ConsumeExactString("Repetare");
        if (isRepeat)
        {
            mainParser.SkipWhitespace();
            if (!mainParser.ConsumeExactString("-"))
            {
                JustThrow("expected dash for repetare");
            }
            mainParser.SkipWhitespace();
        }

        var label = ParseLabel(ref mainParser);
        var year = ParseYear(ref mainParser);

        bool isWildcard = false;
        uint? groupNumber = null;
        var subGroup = ParseSubGroup(ref mainParser);
        if (!subGroup.IsEmpty)
        {
            isWildcard = true;
        }
        else
        {
            groupNumber = ParseGroupNumber(ref mainParser);
        }

        // ReSharper disable once InconsistentNaming
        var languageOrFR = ParseLanguageOrFR(ref mainParser);

        if (!isWildcard)
        {
            subGroup = ParseSubGroup(ref mainParser);
        }
        if (ParseFR(ref mainParser))
        {
            languageOrFR.FR = true;
        }

        mainParser.SkipWhitespace();
        if (!mainParser.IsEmpty && !isRepeat)
        {
            throw new NotSupportedException("Group name not parsed fully.");
        }

        var grade = context.DetermineGrade((int) year);

        return new()
        {
            UnparsedName = s,
            Grade = grade,
            FacultyName = label,
            GroupNumber = (int?) groupNumber,
            // Don't have precedents for master yet.
            QualificationType = QualificationType.Licenta,
            AttendanceMode = languageOrFR.FR ? AttendanceMode.FrecventaRedusa : AttendanceMode.Zi,
            SubGroupName = subGroup,
            IsRepeat = isRepeat,
            IsWildcard = isWildcard,
            Language = languageOrFR.Language,
        };

        static ReadOnlyMemory<char> ParseLabel(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            bparser.SkipLetters();
            var ret = parser.SourceUntilExclusive(bparser.Position);
            if (ret.Length == 0)
            {
                JustThrow("no label");
            }
            parser.MoveTo(bparser.Position);
            return ret;
        }

        static uint ParseYear(ref Parser parser)
        {
            var yearResult = parser.ConsumePositiveInt(GroupHelper.YearLen);
            if (yearResult.Status != ConsumeIntStatus.Ok)
            {
                JustThrow("year");
            }
            return yearResult.Value;
        }

        static uint ParseGroupNumber(ref Parser parser)
        {
            // sometimes they don't denote this completely
            var numberResult = parser.ConsumePositiveIntWithMaxLength(GroupHelper.GroupNumberLen);
            if (numberResult is not { } num)
            {
                JustThrow("group number");
            }
            return num;
        }

        // ReSharper disable once InconsistentNaming
        static LanguageOrFR ParseLanguageOrFR(ref Parser parser)
        {
            var ret = new LanguageOrFR();
            if (ParseFR(ref parser))
            {
                ret.FR = true;
            }
            if (parser.ConsumeExactString("SE"))
            {
                // ignore this
            }

            if (LanguageHelper.ParseName(ref parser) is { } language)
            {
                ret.Language = language;
            }
            else if (parser.ConsumeExactString("R"))
            {
                ret.Language = Language.Ru;
            }

            if (!ret.FR && ret.IsLanguage)
            {
                if (ParseFR(ref parser))
                {
                    ret.FR = true;
                }
            }
            return ret;
        }

        // ReSharper disable once InconsistentNaming
        static bool ParseFR(ref Parser p)
        {
            const string fr = "fr";
            if (!p.CanPeekCount(fr.Length))
            {
                return false;
            }
            var span = p.PeekSpan(fr.Length);
            if (span.Equals(fr, StringComparison.OrdinalIgnoreCase))
            {
                p.Move(fr.Length);
                return true;
            }
            return false;
        }

        static ReadOnlyMemory<char> ParseSubGroup(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                return ReadOnlyMemory<char>.Empty;
            }

            // Possible if we're at a whitespace.
            if (parser.Current != '(')
            {
                return ReadOnlyMemory<char>.Empty;
            }

            parser.Move();
            var bparser = parser.BufferedView();
            var skipResult = bparser.SkipUntilAny([')']);
            if (skipResult.EndOfInput)
            {
                JustThrow("subgroup number");
            }

            var ret = parser.SourceUntilExclusive(bparser);
            parser.MovePast(bparser.Position);

            return ret;
        }

        [DoesNotReturn]
        static void JustThrow(string part)
        {
            throw new NotSupportedException($"Bad {part}");
        }
    }
}

// ReSharper disable once InconsistentNaming
file record struct LanguageOrFR
{
    public Language? Language;
    // ReSharper disable once InconsistentNaming
    public bool FR;
    public readonly bool IsLanguage => Language is not null;
}

internal struct GroupForSearch
{
    public required string UnparsedName;
    public required AttendanceMode AttendanceMode;
    public required Grade Grade;
    public required int? GroupNumber;
    public required ReadOnlyMemory<char> FacultyName;
    public required QualificationType QualificationType;
    public required ReadOnlyMemory<char> SubGroupName;
    public required Language? Language;
    public required bool IsRepeat;
    public required bool IsWildcard;
}
