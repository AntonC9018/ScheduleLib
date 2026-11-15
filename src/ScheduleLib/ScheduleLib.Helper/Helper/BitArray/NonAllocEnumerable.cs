using ScheduleLib.Helper;

internal static class NonAllocEnumerable
{
    public static T? First<T, E>(E e) where E : IEnumerator<T>
    {
        using var enumerator = e;
        if (!enumerator.MoveNext())
        {
            return default;
        }
        return enumerator.Current;
    }

    public static T Single<T, E>(E e) where E : IEnumerator<T>
    {
        using var enumerator = e;
        if (!enumerator.MoveNext())
        {
            throw NonAllocSequenceSingleException.Empty(typeof(T));
        }
        var r = enumerator.Current;
        if (enumerator.MoveNext())
        {
            throw NonAllocSequenceSingleException.MultipleElements(typeof(T));
        }
        return r;
    }
}
