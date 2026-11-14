using Avalonia.Threading;

namespace Desktop.MvvmEssentials;

public static class DispatcherExtensions
{
    public static ValueTask InvokeSyncFallingBackToAsync(this Dispatcher d, Action action)
    {
        if (d.CheckAccess())
        {
            action();
            return ValueTask.CompletedTask;
        }

        return Awaited();

        async ValueTask Awaited() => await d.InvokeAsync(action);
    }

    public static void EnsureCompletedSync(this ValueTask task)
    {
        if (task.IsCompleted)
        {
            task.GetAwaiter()
                .GetResult();
            return;
        }
        throw new AsyncDelegateExecutionException();
    }

    public static T EnsureCompletedSync<T>(this ValueTask<T> task)
    {
        if (task.IsCompleted)
        {
            return task.GetAwaiter()
                .GetResult();
        }
        throw new AsyncDelegateExecutionException();
    }
}
