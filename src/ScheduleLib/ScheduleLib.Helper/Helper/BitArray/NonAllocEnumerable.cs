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
            throw new InvalidOperationException("No elements");
        }
        var r = enumerator.Current;
        if (enumerator.MoveNext())
        {
            throw new InvalidOperationException("More than one element");
        }
        return r;
    }
}
