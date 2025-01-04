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
        return ToRoman(timeSlotIndex);
    }

    public string IntervalDisplay(TimeSlotInterval i)
    {
        return $"{i.Start.Hour}:{i.Start.Minute:00}-{i.End.Hour}:{i.End.Minute:00}";
    }

    private static string ToRoman(int num)
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
