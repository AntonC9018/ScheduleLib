using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib;

public static class ComparisonHelper
{
    public static bool NullGuard(
        // NOTE:
        // this is technically a lie, but the user is supposed to return immediately,
        // so it's close enough.
        [NotNullWhen(false)] object? x,
        [NotNullWhen(false)] object? y,
        out bool ret)
    {
        if (ReferenceEquals(x, y))
        {
            ret = true;
            return true;
        }
        if (x is null)
        {
            ret = false;
            return true;
        }
        if (y is null)
        {
            ret = false;
            return true;
        }
        ret = false;
        return false;
    }
}
