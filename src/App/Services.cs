namespace App;

public sealed class DayNameProvider
{
    public string GetDayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "Luni",
            DayOfWeek.Tuesday => "Marți",
            DayOfWeek.Wednesday => "Miercuri",
            DayOfWeek.Thursday => "Joi",
            DayOfWeek.Friday => "Vineri",
            DayOfWeek.Saturday => "Sâmbătă",
            DayOfWeek.Sunday => "Duminică",
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, null),
        };
    }
}

public sealed class TimeSlotDisplayHandler
{
    public string IndexDisplay(int timeSlotIndex)
    {
        return NumberHelper.ToRoman(timeSlotIndex);
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
}
