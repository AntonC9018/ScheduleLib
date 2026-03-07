namespace Desktop.ViewModels;

public interface IDispatcher
{
    public IDispatcher<T> GetDispatcher<T>();

    // Caller identity is needed to collapse duplicate events in the queue.
    // With this it's possible to only keep the latest events.
    public void Post<T>(object callerIdentity, Action<T> a, T arg);
}

public interface IDispatcher<T>
{
    public void Post(object callerIdentity, Action<T> a, T arg);
}

public sealed class ImmediateDispatcher<T> : IDispatcher<T>
{
    public void Post(object callerIdentity, Action<T> a, T arg) => a(arg);
}

public sealed class DelegatingDispatcher<T> : IDispatcher<T>
{
    private IDispatcher _impl;
    public DelegatingDispatcher(IDispatcher impl) => _impl = impl;
    public void Post(object callerIdentity, Action<T> a, T arg)
    {
        _impl.Post(callerIdentity, a, arg);
    }
}

