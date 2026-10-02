using System.Collections.Immutable;
using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Builders;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Dates;

public readonly record struct StudyWeek
{
    public readonly DateOnly MondayDate;
    public readonly bool IsOddWeek;

    public StudyWeek(DateOnly monday, bool isOddWeek)
    {
        Debug.Assert(monday.DayOfWeek == DayOfWeek.Monday);

        MondayDate = monday;
        IsOddWeek = isOddWeek;
    }
}

// TODO: Move somewhere
public static class DayHelper
{
    public static DateOnly GetDayOfThisWeek(this StudyWeek d, DayOfWeek day)
    {
        return GetDayOfWeekFromMonday(d.MondayDate, day);
    }
    public static DateOnly GetDayOfThisWeek(this DateOnly d, DayOfWeek day)
    {
        var monday = d.AddDays(-(int) d.DayOfWeek + (int) DayOfWeek.Monday);
        return GetDayOfWeekFromMonday(monday, day);
    }

    private const int weekdayCount = 7;
    private static DateOnly GetDayOfWeekFromMonday(DateOnly monday, DayOfWeek day)
    {
        var offset = (day - DayOfWeek.Monday + weekdayCount) % weekdayCount;
        var ret = monday.AddDays(offset);
        return ret;
    }
}

public static class ParityExcelParser
{
    public static IEnumerable<StudyWeek> Parse(
        WordprocessingDocument word)
    {
        if (word.MainDocumentPart?.Document is not { } document)
        {
            throw InvalidParityDocumentException.ForNoDocument();
        }
        if (document.Body is not { } body)
        {
            throw InvalidParityDocumentException.ForNoBody();
        }

        var table = body.Descendants<Table>().First();
        var rows = table.ChildElements.OfType<TableRow>();
        using var rowEnumerator = rows.GetEnumerator();

        DateOnly? previousWeekEnd = null;

        VerifyHeader();
        while (rowEnumerator.MoveNext())
        {
            var cells = GetDataCells(rowEnumerator.Current);
            var weekInterval = Week();
            var isOdd = IsOdd();
            previousWeekEnd = weekInterval.End;
            yield return new(
                monday: weekInterval.Start,
                isOddWeek: isOdd);
            continue;

            WeekInterval Week()
            {
                var weekText = cells.Week.InnerText;
                var parser = new SequenceReader(weekText);
                var ret = ParseWeekInterval(ref parser);
                parser.SkipWhitespace();
                if (!parser.IsEmpty)
                {
                    throw new NotSupportedException("Invalid date range format.");
                }

                {
                    int dayCount = ret.End.DayNumber - ret.Start.DayNumber + 1;
                    if (dayCount != 6)
                    {
                        throw new NotSupportedException("The date range must have 6 days in total");
                    }
                }

                if (previousWeekEnd is { } prevEnd)
                {
                    var dayDiff = ret.Start.DayNumber - prevEnd.DayNumber;
                    if (dayDiff < 0)
                    {
                        throw new NotSupportedException("The week intervals must be in order.");
                    }
                }

                if (ret.Start.DayOfWeek != DayOfWeek.Monday)
                {
                    throw new NotSupportedException("The week interval must start on a Monday.");
                }

                return ret;
            }

            bool IsOdd()
            {
                var parityText = cells.Parity.InnerText;
                var parser = new SequenceReader(parityText);
                var ret = ParseIsOdd(ref parser);
                parser.SkipWhitespace();
                if (!parser.IsEmpty)
                {
                    throw new NotSupportedException("Invalid parity format.");
                }
                return ret;
            }
        }

        yield break;


        void VerifyHeader()
        {
            bool hasHeader = rowEnumerator.MoveNext();
            if (!hasHeader)
            {
                throw new NotSupportedException("No header found.");
            }

            // Check has 3 columns
            // Săptămâna
            // ...
            // Paritatea

            var header = rowEnumerator.Current;
            var headerCells = header.ChildElements.OfType<TableCell>();
            using var cellEnumerator = headerCells.GetEnumerator();

            bool CompareHeader(string expectedText)
            {
                var weekHeaderText = cellEnumerator.Current.InnerText.AsSpan().Trim();
                return IgnoreDiacriticsAndCaseComparer.Instance.Equals(weekHeaderText, expectedText.AsSpan());
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (!moved)
                {
                    throw new NotSupportedException("No row for the week string");
                }

                if (!CompareHeader("Saptamana"))
                {
                    throw new NotSupportedException("Week header should be first. It had unexpected name.");
                }
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (!moved)
                {
                    throw new NotSupportedException("An insignificant column must follow after the week column.");
                }
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (!moved)
                {
                    throw new NotSupportedException("No row for the parity string");
                }

                if (!CompareHeader("Paritatea"))
                {
                    throw new NotSupportedException("Week header should be first. It had unexpected name.");
                }
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (moved)
                {
                    throw new NotSupportedException("Must only have 3 columns");
                }
            }
        }

        static (TableCell Week, TableCell Parity) GetDataCells(TableRow dataRow)
        {
            var cells = dataRow.ChildElements.OfType<TableCell>();
            using var cellsEnumerator = cells.GetEnumerator();

            var weekCell = NextCell(cellsEnumerator);
            _ = NextCell(cellsEnumerator);
            var parityCell = NextCell(cellsEnumerator);
            NoNextCell(cellsEnumerator);
            return (
                Week: weekCell,
                Parity: parityCell);
        }

        static TableCell NextCell(IEnumerator<TableCell> cells)
        {
            bool moved = cells.MoveNext();
            if (!moved)
            {
                throw new NotSupportedException("Expected a cell??");
            }
            return cells.Current;
        }
        static void NoNextCell(IEnumerator<TableCell> cells)
        {
            bool moved = cells.MoveNext();
            if (moved)
            {
                throw new NotSupportedException("Expected no more cells.");
            }
        }
    }

    private static readonly ImmutableArray<string> MonthNames = GetMonthNames();

    private static ImmutableArray<string> GetMonthNames()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var ret = ImmutableArray.CreateBuilder<string>();
        ret.Capacity = (int) Month.Count;
        ret.Count = (int) Month.Count;
        Set(Month.January, "ianuarie");
        Set(Month.February, "februarie");
        Set(Month.March, "martie");
        Set(Month.April, "aprilie");
        Set(Month.May, "mai");
        Set(Month.June, "iunie");
        Set(Month.July, "iulie");
        Set(Month.August, "august");
        Set(Month.September, "septembrie");
        Set(Month.October, "octombrie");
        Set(Month.November, "noiembrie");
        Set(Month.December, "decembrie");
        Debug.Assert(ret.All(x => x != null));
        return ret.MoveToImmutable();

        void Set(Month m, string name)
        {
            ret[(int) (m - 1)] = name;
        }
    }


    private static Month? ParseMonth(ReadOnlySpan<char> name)
    {
        for (int i = 0; i < MonthNames.Length; i++)
        {
            var monthName = MonthNames[i];
            if (monthName.AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var month = (Month) (i + 1);
                return month;
            }
        }
        return null;
    }

    private record struct WeekInterval(DateOnly Start, DateOnly End);

    private readonly struct SkipPunctuationOrWhite : IShouldSkip
    {
        public bool ShouldSkip(char c)
        {
            if (char.IsPunctuation(c))
            {
                return true;
            }
            if (char.IsWhiteSpace(c))
            {
                return true;
            }
            return false;
        }
    }

    private static WeekInterval ParseWeekInterval(ref SequenceReader reader)
    {
        reader.SkipWhitespace();

        var dayStart = ParseDayNumber(ref reader);
        reader.SkipWhitespace();
        var monthStart = ParseMonth1(ref reader);
        reader.Skip(new SkipPunctuationOrWhite());

        var dayEnd = ParseDayNumber(ref reader);
        reader.SkipWhitespace();
        var monthEnd = ParseMonth1(ref reader);
        reader.SkipWhitespace();

        var year = ParseYear(ref reader);
        var startDate = CreateDate(dayStart, monthStart);
        var endDate = CreateDate(dayEnd, monthEnd);
        return new()
        {
            Start = startDate,
            End = endDate,
        };

        DateOnly CreateDate(uint day, Month month)
        {
            return new DateOnly(
                year: year,
                month: (int) month,
                day: (int) day);
        }

        uint ParseDayNumber(ref SequenceReader reader)
        {
            var bparser = reader.BufferedView();
            var skipResult = bparser.SkipNumbers();
            if (!skipResult.SkippedAny)
            {
                throw new NotSupportedException("Day number expected");
            }
            var daySpan = reader.PeekSpanUntilPosition(bparser.Position);
            if (daySpan.Length > 2)
            {
                throw new NotSupportedException("Day number too long (max 2 numbers)");
            }
            if (!uint.TryParse(daySpan, out uint day))
            {
                Debug.Fail("This should never happen?");
                day = 0;
            }
            reader.MoveTo(bparser.Position);
            return day;
        }

        Month ParseMonth1(ref SequenceReader reader)
        {
            var bparser = reader.BufferedView();
            var skipResult = bparser.SkipLetters();
            if (!skipResult.SkippedAny)
            {
                throw new NotSupportedException("Month name expected");
            }
            var monthSpan = reader.PeekSpanUntilPosition(bparser.Position);
            if (ParseMonth(monthSpan) is not { } month)
            {
                throw new NotSupportedException("Month name not recognized");
            }
            reader.MoveTo(bparser.Position);
            return month;
        }

        int ParseYear(ref SequenceReader reader)
        {
            var yearResult = reader.ConsumePositiveInt(4);
            if (yearResult.Status != ConsumeIntStatus.Ok)
            {
                throw new NotSupportedException("Year not parsed");
            }
            return (int) yearResult.Value;
        }
    }

    private static bool ParseIsOdd(ref SequenceReader reader)
    {
        var bparser = reader.BufferedView();
        bparser.SkipNotWhitespace();
        var span = reader.PeekSpanUntilPosition(bparser.Position);

        static bool Equals1(ReadOnlySpan<char> span, string literal)
        {
            return span.Equals(literal.AsSpan(), StringComparison.CurrentCultureIgnoreCase);
        }

        if (Equals1(span, "Pară"))
        {
            reader.MoveTo(bparser.Position);
            return false;
        }
        if (Equals1(span, "Impară"))
        {
            reader.MoveTo(bparser.Position);
            return true;
        }
        throw new NotSupportedException("Parity not recognized");
    }
}

internal enum Month
{
    January = 1,
    February = 2,
    March = 3,
    April = 4,
    May = 5,
    June = 6,
    July = 7,
    August = 8,
    September = 9,
    October = 10,
    November = 11,
    December = 12,
    Count = 12,
}
