namespace Desktop.ViewModels;

public readonly record struct CallerIdentity(object Value);
public readonly record struct PostArgs<T>(
    CallerIdentity CallerId,
    Action<T> Action,
    T Arg)
{
    public void Invoke() => Action(Arg);
    public Action GetInvoker()
    {
        var a = Action;
        var arg = Arg;
        return () => a(arg);
    }
}

public interface IDispatcher
{
    public IDispatcher<T> GetDispatcher<T>();

    // Caller identity is needed to collapse duplicate events in the queue.
    // With this it's possible to only keep the latest events.
    public void Post<T>(PostArgs<T> args);
}

public interface IDispatcher<T>
{
    public void Post(PostArgs<T> args);
}

public sealed class ImmediateDispatcher<T> : IDispatcher<T>
{
    public void Post(PostArgs<T> args) => args.Action(args.Arg);
}

public sealed class DelegatingDispatcher<T> : IDispatcher<T>
{
    private IDispatcher _impl;
    public DelegatingDispatcher(IDispatcher impl) => _impl = impl;
    public void Post(PostArgs<T> args)
    {
        _impl.Post(args);
    }
}

