using System.Runtime.InteropServices;

namespace App;

public static class DictionaryHelper
{
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
}
