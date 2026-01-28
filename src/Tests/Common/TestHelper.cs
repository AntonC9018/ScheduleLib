using System.Diagnostics;

public static class TestAnyLessonSchedulingDateHelper
{
    public static CancellationTokenSource CreateCts()
    {
        var delay = TimeSpan.FromSeconds(10);
        if (Debugger.IsAttached)
        {
            delay = TimeSpan.FromMinutes(10);
        }
        return new CancellationTokenSource(delay);
    }
}
