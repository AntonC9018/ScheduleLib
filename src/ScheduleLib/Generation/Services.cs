using System.Collections.Immutable;
using System.Diagnostics;

namespace ScheduleLib.Generation;

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
        Set(DayOfWeek.Tuesday, "Marţi");
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
    public string? Get(SubGroup n)
    {
        return n.Value;
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

public static class LessonTypeConstants
{
    private static string[] CreateNames()
    {
        var ret = new string[3];
        void Set(LessonType t, string name)
        {
            ret[(int) t] = name;
        }

        Set(LessonType.Curs, "curs");
        Set(LessonType.Lab, "lab");
        Set(LessonType.Seminar, "sem");
        Debug.Assert(ret.None(x => x is null));
        return ret;
    }

    public static readonly ImmutableArray<string> Names = [..CreateNames()];
}

public sealed class LessonTypeDisplayHandler
{
    public string? Get(LessonType type)
    {
        var names = LessonTypeConstants.Names;
        if ((int) type < names.Length)
        {
            return names[(int) type];
        }
        return null;
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

    public static int? FromRoman(ReadOnlySpan<char> roman)
    {
        for (int i = 1; i <= 10; i++)
        {
            var num = ToRoman(i);
            if (num.AsSpan().Equals(roman, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return null;
    }
}
