using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib;

public static class ComparisonHelper
{
    public static bool AtLeastOneIsDefault<T>(
        // NOTE:
        // this is technically a lie, but the user is supposed to return immediately,
        // so it's close enough.
        [NotNullWhen(false)] T? x,
        [NotNullWhen(false)] T? y,
        out bool areBothDefault)
    {
        var comparer = EqualityComparer<T?>.Default;
        bool xdefault = comparer.Equals(x, default);
        bool ydefault = comparer.Equals(y, default);
        if (xdefault && ydefault)
        {
            areBothDefault = true;
            return true;
        }
        areBothDefault = false;
        if (xdefault)
        {
            return true;
        }
        if (ydefault)
        {
            return true;
        }
        return false;
    }

    public static bool AtLeastOneIsNull<T>(
        // NOTE:
        // this is technically a lie, but the user is supposed to return immediately,
        // so it's close enough.
        [NotNullWhen(false)] T? x,
        [NotNullWhen(false)] T? y,
        out bool areBothNull)
        where T : class
    {
        if (ReferenceEquals(x, y))
        {
            areBothNull = true;
            return true;
        }
        areBothNull = false;
        if (x is null)
        {
            return true;
        }
        if (y is null)
        {
            return true;
        }
        return false;
    }

    public static bool ValueEquals<T>(
        this T? x,
        T? y,
        Func<T, T, bool> comparer)
        where T : class
    {
        if (AtLeastOneIsNull(x, y, out bool bothNull))
        {
            return bothNull;
        }
        return comparer(x, y);
    }
}

public static class StructComparisonHelper
{
    public static bool ValueEquals<T>(
        this T? x,
        T? y,
        Func<T, T, bool> comparer)
        where T : struct
    {
        if (x is null && y is null)
        {
            return true;
        }
        if (x is not { } xv)
        {
            return false;
        }
        if (y is not { } yv)
        {
            return false;
        }
        var ret = comparer(xv, yv);
        return ret;
    }

    public static bool ValueEquals<T>(this T? x, T? y)
        where T : struct, IEquatable<T>
    {
        if (x is null && y is null)
        {
            return true;
        }
        if (x is not { } xv)
        {
            return false;
        }
        if (y is not { } yv)
        {
            return false;
        }
        var ret = xv.Equals(yv);
        return ret;
    }
}
