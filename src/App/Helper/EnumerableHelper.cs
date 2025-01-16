namespace App;

public static class EnumerableHelper
{
    public static bool None<T>(this IEnumerable<T> source, Func<T, bool> pred)
    {
        return !source.Any(pred);
    }

    public static IEnumerable<(int Index, T Item)> WithIndex<T>(this IEnumerable<T> source)
    {
        return source.Select((x, i) => (i, x));
    }
}
