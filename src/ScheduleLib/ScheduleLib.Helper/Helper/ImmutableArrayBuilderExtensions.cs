using System.Collections.Immutable;

namespace ScheduleLib.Helper.Helper;

public static class ImmutableArrayBuilderExtensions
{
    public static void SetExactSize<T>(this ImmutableArray<T>.Builder builder, int size)
    {
        builder.Capacity = size;
        builder.Count = size;
    }
}
