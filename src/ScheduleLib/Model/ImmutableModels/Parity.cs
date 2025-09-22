public enum Parity
{
    OddWeek,
    EvenWeek,
    EveryWeek,
}

public static class ParityHelper
{
    public static bool IsMatch(this Parity parity, Parity check)
    {
        switch (check)
        {
            case Parity.EveryWeek:
            {
                return true;
            }
            default:
            {
                if (parity == Parity.EveryWeek)
                {
                    return true;
                }
                if (parity == check)
                {
                    return true;
                }
                return false;
            }
        }
    }
}

