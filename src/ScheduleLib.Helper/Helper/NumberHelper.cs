namespace ScheduleLib.Helper;

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
