using System.Collections.Immutable;
using System.Diagnostics;

namespace App.Generation;

public sealed class DayNameProvider
{
    public string GetDayName(DayOfWeek day)
    {
        return Names[(int) day];
    }

    private static string[] CreateNames()
    {
        var ret = new string[7];
        void Set(DayOfWeek day, string name)
        {
            ret[(int) day] = name;
        }

        Set(DayOfWeek.Monday, "Luni");
        Set(DayOfWeek.Tuesday, "Marți");
        Set(DayOfWeek.Wednesday, "Miercuri");
        Set(DayOfWeek.Thursday, "Joi");
        Set(DayOfWeek.Friday, "Vineri");
        Set(DayOfWeek.Saturday, "Sâmbătă");
        Set(DayOfWeek.Sunday, "Duminică");
        Debug.Assert(ret.None(x => x is null));
        return ret;
    }
    private static readonly ImmutableArray<string> _Names = [.. CreateNames()];
    public readonly ImmutableArray<string> Names = _Names;
}

public sealed class TimeSlotDisplayHandler
{
    public string IndexDisplay(int timeSlotIndex)
    {
        return NumberHelper.ToRoman(timeSlotIndex + 1);
    }

    public string IntervalDisplay(TimeSlotInterval i)
    {
        return $"{i.Start.Hour}:{i.Start.Minute:00}-{i.End.Hour}:{i.End.Minute:00}";
    }
}

public sealed class SubGroupNumberDisplayHandler
{
    public string? Get(SubGroupNumber n)
    {
        if (n == SubGroupNumber.All)
        {
            return null;
        }
        return NumberHelper.ToRoman(n.Value);
    }
}

public sealed class ParityDisplayHandler
{
    public string? Get(Parity p)
    {
        return p switch
        {
            Parity.EvenWeek => "par",
            Parity.OddWeek => "impar",
            _ => null,
        };
    }
}

public sealed class LessonTypeDisplayHandler
{
    public string? Get(LessonType type)
    {
        return type switch
        {
            LessonType.Curs => "curs",
            LessonType.Lab => "lab",
            LessonType.Seminar => "sem",
            _ => null,
        };
    }
}

public static class NumberHelper
{
    public static string ToRoman(int num)
    {
        string romanLetter = num switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            9 => "IX",
            10 => "X",
            _ => throw new NotImplementedException("Unimplemented for higher time slots."),
        };
        return romanLetter;
    }

    public static int FromRoman(string roman)
    {
        for (int i = 1; i <= 10; i++)
        {
            var num = ToRoman(i);
            if (num.Equals(roman, StringComparison.Ordinal))
            {
                return i;
            }
        }
        throw new NotImplementedException("Unimplemented for higher time slots.");
    }
}
