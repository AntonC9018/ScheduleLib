using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ScheduleLib.Helper.Parsing;
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
        var mainParser = new SequenceReader(s);
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
        var suffixes = new GroupSuffixes();
        if (label.Length > 2 && label.Span.EndsWith("FR", StringComparison.OrdinalIgnoreCase))
        {
            label = label[..^2];
            suffixes.ReducedAttendance = true;
        }
        bool isMaster = context.IsGroupMaster(label);

        var year = ParseYear(ref mainParser);

        bool isWildcard = false;
        uint? groupNumber = null;
        var subGroup = ParseSubGroup(ref mainParser);
        if (subGroup.Span.Equals("Dual", StringComparison.OrdinalIgnoreCase))
        {
            if (suffixes.ReducedAttendance)
            {
                JustThrow("conflicting attendance");
            }
            suffixes.Dual = true;
        }
        else if (!subGroup.IsEmpty)
        {
            isWildcard = true;
        }
        else
        {
            groupNumber = ParseGroupNumber(ref mainParser);
        }

        ParseSuffixes(ref mainParser, ref suffixes);

        if (!isWildcard)
        {
            subGroup = ParseSubGroup(ref mainParser);
            if (!subGroup.IsEmpty)
            {
                ParseSuffixes(ref mainParser, ref suffixes);
                var subgroupLanguage = LanguageHelper.ParseName(subGroup.Span);
                if (subgroupLanguage is Language.Ru or Language.Ro)
                {
                    SetLanguage(ref suffixes, subgroupLanguage.Value);
                    subGroup = ReadOnlyMemory<char>.Empty;
                }
            }
        }

        mainParser.SkipWhitespace();
        if (!mainParser.IsEmpty && !isRepeat)
        {
            throw new NotSupportedException("Group name not parsed fully.");
        }

        var grade = context.DetermineGrade((int) year);

        // Registry "(en)" denotes English labs within a parent-language group.
        // Keep it as a partition rather than changing the whole group's language.
        if (subGroup.Span.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            subGroup = SpecialSubGroups.Eng.Value.AsMemory();
        }

        return new()
        {
            UnparsedName = s,
            Grade = grade,
            FacultyName = label,
            GroupNumber = (int?) groupNumber,
            QualificationType = isMaster ? QualificationType.Master : QualificationType.Licenta,
            AttendanceMode = suffixes.Dual ? AttendanceMode.Dual
                : suffixes.ReducedAttendance ? AttendanceMode.FrecventaRedusa : AttendanceMode.Zi,
            SubGroupName = subGroup,
            IsRepeat = isRepeat,
            IsWildcard = isWildcard,
            Language = suffixes.Language,
            IsDual = suffixes.Dual,
        };

        static ReadOnlyMemory<char> ParseLabel(ref SequenceReader reader)
        {
            // Only the confirmed combined registry label is accepted; underscores
            // do not introduce a general faculty-combination grammar.
            if (reader.ConsumeExactString(RegistryFacultyLabels.CombinedInformatics))
            {
                return RegistryFacultyLabels.CombinedInformatics.AsMemory();
            }
            var bparser = reader.BufferedView();
            bparser.SkipLetters();
            var ret = reader.SourceUntilExclusive(bparser.Position);
            if (ret.Length == 0)
            {
                JustThrow("no label");
            }
            reader.MoveTo(bparser.Position);
            return ret;
        }

        static uint ParseYear(ref SequenceReader reader)
        {
            var yearResult = reader.ConsumePositiveInt(GroupHelper.YearLen);
            if (yearResult.Status != ConsumeIntStatus.Ok)
            {
                JustThrow("year");
            }
            return yearResult.Value;
        }

        static uint ParseGroupNumber(ref SequenceReader reader)
        {
            // sometimes they don't denote this completely
            var numberResult = reader.ConsumePositiveIntWithMaxLength(GroupHelper.GroupNumberLen);
            if (numberResult is not { } num)
            {
                JustThrow("group number");
            }
            return num;
        }

        static void ParseSuffixes(ref SequenceReader reader, ref GroupSuffixes suffixes)
        {
            while (!reader.IsEmpty)
            {
                if (ParseFR(ref reader))
                {
                    if (suffixes.ReducedAttendance || suffixes.Dual)
                    {
                        JustThrow("duplicate or conflicting attendance");
                    }
                    suffixes.ReducedAttendance = true;
                }
                else if (reader.ConsumeExactString("DU"))
                {
                    if (suffixes.Dual || suffixes.ReducedAttendance)
                    {
                        JustThrow("duplicate or conflicting attendance");
                    }
                    suffixes.Dual = true;
                }
                else if (ConsumeMarker(ref reader, "SE"))
                {
                    // Științele Educației: accepted but not represented in the current domain.
                }
                else if (LanguageHelper.ParseName(ref reader) is { } language)
                {
                    SetLanguage(ref suffixes, language);
                }
                else if (ConsumeMarker(ref reader, "R"))
                {
                    SetLanguage(ref suffixes, Language.Ru);
                }
                else
                {
                    break;
                }
            }
        }

        static void SetLanguage(ref GroupSuffixes suffixes, Language language)
        {
            if (suffixes.Language is not null)
            {
                JustThrow("duplicate language");
            }
            suffixes.Language = language;
        }

        // ReSharper disable once InconsistentNaming
        static bool ParseFR(ref SequenceReader p)
        {
            return ConsumeMarker(ref p, "fr");
        }

        static bool ConsumeMarker(ref SequenceReader p, string marker)
        {
            if (!p.CanPeekCount(marker.Length))
            {
                return false;
            }
            var span = p.PeekSpan(marker.Length);
            if (span.Equals(marker, StringComparison.OrdinalIgnoreCase))
            {
                p.Move(marker.Length);
                return true;
            }
            return false;
        }

        static ReadOnlyMemory<char> ParseSubGroup(ref SequenceReader reader)
        {
            if (reader.IsEmpty)
            {
                return ReadOnlyMemory<char>.Empty;
            }

            // Possible if we're at a whitespace.
            if (reader.Current != '(')
            {
                return ReadOnlyMemory<char>.Empty;
            }

            reader.Move();
            var bparser = reader.BufferedView();
            var skipResult = bparser.SkipUntilAny([')']);
            if (skipResult.EndOfInput)
            {
                JustThrow("subgroup number");
            }

            var ret = reader.SourceUntilExclusive(bparser);
            reader.MovePast(bparser.Position);

            return ret;
        }

        [DoesNotReturn]
        static void JustThrow(string part)
        {
            throw new GroupParsingException(part);
        }
    }
}

public sealed class GroupParsingException : Exception
{
    public GroupParsingException(string part) : base($"Bad {part}")
    {
    }
}

file record struct GroupSuffixes
{
    public Language? Language;
    public bool ReducedAttendance;
    public bool Dual;
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
    public required bool IsDual;
}

/// <summary>Confirmed registry-only combined label, matched to existing faculties.</summary>
internal static class RegistryFacultyLabels
{
    internal const string CombinedInformatics = "I_IA";

    internal static bool IsCombined(ReadOnlyMemory<char> label) =>
        label.Span.Equals(CombinedInformatics, StringComparison.OrdinalIgnoreCase);

    internal static bool Matches(ReadOnlyMemory<char> label, string faculty)
    {
        if (IsCombined(label))
        {
            return faculty.Equals("I", StringComparison.OrdinalIgnoreCase)
                || faculty.Equals("IA", StringComparison.OrdinalIgnoreCase);
        }
        return label.Span.Equals(faculty, StringComparison.OrdinalIgnoreCase);
    }
}
