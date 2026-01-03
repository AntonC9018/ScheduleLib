namespace ScheduleLib.Helper;

public readonly record struct MaybeAsyncDisposable<T> : IAsyncDisposable
    where T : IAsyncDisposable
{
    public T? Value { get; }

    public MaybeAsyncDisposable(T? value)
    {
        Value = value;
    }

    public async ValueTask DisposeAsync()
    {
        if (Value != null)
        {
            await Value.DisposeAsync();
        }
    }
}
