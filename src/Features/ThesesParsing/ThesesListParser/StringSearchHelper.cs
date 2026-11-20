using System.Collections.Immutable;
using System.Diagnostics;
using ScheduleLib.Helper;

namespace ScheduleLib.Theses.Parsing;

/// <summary>Builds arrays indexed by an enum, with one entry per member.</summary>
public static class StringSearchHelper
{
    public struct SearchArrayBuilder<T, U> where T : struct, Enum
    {
        internal readonly ImmutableArray<U>.Builder _builder;

        public SearchArrayBuilder()
        {
            _builder = ImmutableArray.CreateBuilder<U>(EnumMembers<T>.Count);
            _builder.Count = _builder.Capacity;
        }

        public readonly void Set(T tag, U str)
        {
            int index = EnumMembers<T>.EnumAsInt(tag);
            Debug.Assert(index >= 0 && index < _builder.Capacity);
            _builder[index] = str;
        }
    }

    public delegate void BuilderDelegate<T, U>(SearchArrayBuilder<T, U> builder) where T : struct, Enum;

    public static ImmutableArray<string> SetupSearchArray<T>(BuilderDelegate<T, string> f) where T : struct, Enum
    {
        return SetupSearchArray<T, string>(f);
    }

    public static ImmutableArray<U> SetupSearchArray<T, U>(BuilderDelegate<T, U> f) where T : struct, Enum
    {
        var builder = new SearchArrayBuilder<T, U>();
        f(builder);
        if (!builder._builder.All(x => x is not null))
        {
            Debug.Fail("Some members not initialized");
        }
        var ret = builder._builder.MoveToImmutable();
        return ret;
    }
}
