using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib;

public static class UnreachableHelper
{
    [DoesNotReturn]
    public static Exception Unreachable()
    {
        Debug.Fail("Unreachable");
        throw null!;
    }
}
