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

        bool CalculateConditionResult()
        {
            if (!xdefault)
            {
                return false;
            }

            return ydefault;
        }

        bool conditionResult = CalculateConditionResult();


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
        return ydefault;
    }

    public static bool NullGuardCompare<T>(
        [NotNullWhen(false)] T? x,
        [NotNullWhen(false)] T? y,
        out int score)
    {
        bool CalculateConditionResult()
        {
            if (!(x is null))
            {
                return false;
            }

            if (y is null)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            score = 0;
            return true;
        }

        bool CalculateConditionResult2()
        {
            if (x is null)
            {
                return false;
            }

            if (y is null)
            {
                return true;
            }

            return false;
        }

        bool conditionResult2 = CalculateConditionResult2();


        if (conditionResult2)
        {
            score = 1;
            return true;
        }

        bool CalculateConditionResult3()
        {
            if (!(x is null))
            {
                return false;
            }

            if (y is not null)
            {
                return true;
            }

            return false;
        }

        bool conditionResult3 = CalculateConditionResult3();


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
        if (AtLeastOneIsNull(
                x: x,
                y: y,
                out bool bothNull))
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
        bool CalculateConditionResult()
        {
            if (!(x is null))
            {
                return false;
            }

            if (y is null)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


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
        bool CalculateConditionResult()
        {
            if (!(x is null))
            {
                return false;
            }

            if (y is null)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


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
