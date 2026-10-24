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
        bool xdefault = comparer.Equals(x: x, y: default);
        bool ydefault = comparer.Equals(x: y, y: default);
        bool conditionResult;
        if (xdefault)
        {
            conditionResult = ydefault;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
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

    public static bool NullGuardCompare<T>(
        [NotNullWhen(false)] T? x,
        [NotNullWhen(false)] T? y,
        out int score)
    {
        bool conditionResult;
        if (x is null)
        {
            conditionResult = y is null;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
        {
            score = 0;
            return true;
        }
        bool conditionResult2;
        if (x is not null)
        {
            conditionResult2 = y is null;
        }
        else
        {
            conditionResult2 = false;
        }

        if (conditionResult2)
        {
            score = 1;
            return true;
        }
        bool conditionResult3;
        if (x is null)
        {
            conditionResult3 = y is not null;
        }
        else
        {
            conditionResult3 = false;
        }

        if (conditionResult3)
        {
            score = -1;
            return true;
        }
        score = 0;
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
        if (ReferenceEquals(objA: x, objB: y))
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
        if (AtLeastOneIsNull(x: x, y: y, out bool bothNull))
        {
            return bothNull;
        }

        if (comparer(arg1: x, arg2: y))
        {
            return true;
        }

        return false;
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
        bool conditionResult;
        if (x is null)
        {
            conditionResult = y is null;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
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
        var ret = comparer(arg1: xv, arg2: yv);
        return ret;
    }

    public static bool ValueEquals<T>(this T? x, T? y)
        where T : struct, IEquatable<T>
    {
        bool conditionResult;
        if (x is null)
        {
            conditionResult = y is null;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
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
