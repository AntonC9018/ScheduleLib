using System.Runtime.InteropServices;

namespace ScheduleLib;

public delegate T AddFunc<T>(ReadOnlySpan<char> key);

public static class DictionaryHelper
{
    public static T GetOrAdd<T>(
        this Dictionary<string, T> dict,
        ReadOnlySpan<char> key,
        AddFunc<T> add)
    {
        var l = dict.GetAlternateLookup<ReadOnlySpan<char>>();
        ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(l, key, out bool exists);
        if (exists)
        {
            return val!;
        }

        val = add(key);
        return val!;
    }

    public static T GetOrAdd<T>(
        this Dictionary<string, T> dict,
        string key,
        Func<string, T> add)
    {
        ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);
        if (exists)
        {
            return val!;
        }

        val = add(key);
        return val!;
    }

    public static T GetOrAdd<T, TState>(
        this Dictionary<string, T> dict,
        string key,
        TState state,
        Func<TState, T> add)
    {
        ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);
        if (exists)
        {
            return val!;
        }

        val = add(state);
        return val!;
    }
}
