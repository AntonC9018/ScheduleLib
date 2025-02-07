using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ScheduleLib;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;

namespace ReaderApp.OnlineRegistry;

public static partial class RegistryScraping
{
    // here, the format is different
    // DJ2302ru(II)
    // DJ2301
    // IA2401fr
    internal static GroupForSearch ParseGroupFromOnlineRegistry(
        GroupParseContext context,
        string s)
    {
        var mainParser = new Parser(s);
        mainParser.SkipWhitespace();

        var bparser = mainParser.BufferedView();

        var label = ParseLabel(ref bparser);
        var year = ParseYear(ref bparser);
        var groupNumber = ParseGroupNumber(ref bparser);
        var nameWithoutFR = mainParser.PeekSpanUntilPosition(bparser.Position);
        _ = nameWithoutFR;

        mainParser.MoveTo(bparser.Position);

        var languageOrFR = ParseLanguageOrFR(ref mainParser);
        var subGroup = ParseSubGroup(ref mainParser);

        mainParser.SkipWhitespace();
        if (!mainParser.IsEmpty)
        {
            throw new NotSupportedException("Group name not parsed fully.");
        }

        var grade = context.DetermineGrade((int) year);

        return new()
        {
            Grade = grade,
            FacultyName = label,
            GroupNumber = (int) groupNumber,
            // Don't have precedents for master yet.
            QualificationType = QualificationType.Licenta,
            AttendanceMode = languageOrFR.FR ? AttendanceMode.FrecventaRedusa : AttendanceMode.Zi,
            SubGroupName = subGroup,
        };

        static ReadOnlyMemory<char> ParseLabel(ref Parser parser)
        {
            if (!parser.CanPeekCount(2))
            {
                JustThrow("group label");
            }
            var ret = parser.PeekSource(2);
            parser.Move(2);
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
            var numberResult = parser.ConsumePositiveInt(GroupHelper.GroupNumberLen);
            if (numberResult.Status != ConsumeIntStatus.Ok)
            {
                JustThrow("group number");
            }
            return numberResult.Value;
        }

        static LanguageOrFR ParseLanguageOrFR(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            var skipResult = bparser.Skip(new SkipUntilOpenParenOrWhiteSpace());
            if (!skipResult.SkippedAny)
            {
                return default;
            }

            var languageOrFRName = parser.PeekSpanUntilPosition(bparser.Position);
            var ret = DetermineIfLabelIsLanguageOrFR(languageOrFRName);
            parser.MoveTo(bparser.Position);
            return ret;
        }

        static LanguageOrFR DetermineIfLabelIsLanguageOrFR(ReadOnlySpan<char> languageOrFRName)
        {
            LanguageOrFR ret = default;
            if (languageOrFRName.Equals("fr", StringComparison.OrdinalIgnoreCase))
            {
                ret.FR = true;
                return ret;
            }

            var maybeLang = LanguageHelper.ParseName(languageOrFRName);
            if (maybeLang is not { } lang)
            {
                JustThrow("language");
            }
            ret.Language = lang;
            return ret;
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
            var skipResult = bparser.SkipUntil([')']);
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

    private struct SkipUntilOpenParenOrWhiteSpace : IShouldSkip
    {
        public bool ShouldSkip(char c)
        {
            if (c == '(')
            {
                return false;
            }
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
            return true;
        }
    }

    // Intentionally duplicated, because the strings are actually different.
    internal static LessonType ParseLessonType(
        string s,
        IRegistryErrorHandler errorHandler)
    {
        var parser = new Parser(s);
        parser.SkipWhitespace();
        if (parser.IsEmpty)
        {
            return LessonType.Unspecified;
        }
        var bparser = parser.BufferedView();
        _ = parser.SkipNotWhitespace();
        var lessonTypeSpan = parser.PeekSpanUntilPosition(bparser.Position);
        var lessonType = Get(lessonTypeSpan);
        if (lessonType == LessonType.Custom)
        {
            errorHandler.CustomLessonType(lessonTypeSpan);
        }

        parser.MoveTo(bparser.Position);

        parser.SkipWhitespace();
        if (!parser.IsEmpty)
        {
            throw new NotSupportedException("Lesson type not parsed fully.");
        }
        return lessonType;

        LessonType Get(ReadOnlySpan<char> str)
        {
            static bool Equal(
                ReadOnlySpan<char> str,
                string literal)
            {
                return str.Equals(
                    literal.AsSpan(),
                    StringComparison.Ordinal);
            }

            for (var i = 0; i < LessonTypeNames.Length; i++)
            {
                if (Equal(str, LessonTypeNames[i]))
                {
                    return (LessonType) i;
                }
            }
            return LessonType.Custom;
        }
    }

    internal static string? GetLessonTypeName(LessonType type)
    {
        if (LessonTypeNames.Length <= (int) type)
        {
            return null;
        }
        return LessonTypeNames[(int) type];
    }


    private static readonly ImmutableArray<string> LessonTypeNames = CreateLessonTypeNames();
    private static ImmutableArray<string> CreateLessonTypeNames()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var ret = ImmutableArray.CreateBuilder<string>();
        ret.Capacity = 3;
        ret.Count = 3;

        Set(LessonType.Lab, "laborator");
        Set(LessonType.Curs, "curs");
        Set(LessonType.Seminar, "seminar");

        Debug.Assert(ret.All(x => x != null));

        return ret.ToImmutable();

        void Set(LessonType t, string value)
        {
            ret[(int) t] = value;
        }
    }
}

internal record struct LanguageOrFR
{
    public Language? Language;
    public bool FR;
    public readonly bool IsLanguage => Language is not null;
}

internal struct GroupForSearch
{
    public required AttendanceMode AttendanceMode;
    public required Grade Grade;
    public required int GroupNumber;
    public required ReadOnlyMemory<char> FacultyName;
    public required QualificationType QualificationType;
    public required ReadOnlyMemory<char> SubGroupName;
}
