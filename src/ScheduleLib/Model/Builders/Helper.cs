using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace ScheduleLib.Builders;

public readonly struct ListBuilder<T>()
{
    public readonly List<T> List = new();

    public readonly ref struct AddResult
    {
        public readonly ref T Value;
        public readonly int Id;

        public AddResult(int id, ref T value)
        {
            Id = id;
            Value = ref value;
        }
    }

    // The reference is invalidated after another call.
    public AddResult New()
    {
        List.Add(default!);
        var span = CollectionsMarshal.AsSpan(List);
        return new(List.Count - 1, ref span[^1]);
    }

    public ImmutableArray<T> Build() => [.. List];
    public ImmutableArray<U> Build<U>(Func<T, U> f)
    {
        var builder = ImmutableArray.CreateBuilder<U>(List.Count);
        foreach (var item in List)
        {
            builder.Add(f(item));
        }
        return builder.ToImmutable();
    }

    public int Count => List.Count;

    public ref T Ref(int id) => ref CollectionsMarshal.AsSpan(List)[id];
}

