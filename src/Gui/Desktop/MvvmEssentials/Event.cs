// Needed to implement disposable helpers that unsub from events.
// TODO: Try to learn the messenger system in MVVM Toolkit, when I figure out the arch.

namespace Desktop.MvvmEssentials;

public readonly struct EventSubscription<T> : IDisposable
{
    private readonly Event<T> _ev;
    private readonly Action<T> _action;

    public EventSubscription(Event<T> ev, Action<T> action)
    {
        _ev = ev;
        _action = action;
    }

    public void Dispose() => _ev.Unsub(_action);
}

public readonly struct EventSubscription(EventSubscription<Nothing> impl) : IDisposable
{
    private readonly EventSubscription<Nothing> _impl = impl;
    public void Dispose() => _impl.Dispose();
    public static implicit operator EventSubscription<Nothing>(EventSubscription s) => s._impl;
    public static implicit operator EventSubscription(EventSubscription<Nothing> s) => new(s);
}

public readonly struct Event
{
    private readonly Event<Nothing> _impl;
    public Event(Event<Nothing> impl) => _impl = impl;
    public EventSubscription Sub(Action action) => _impl.Sub(action);
    public static implicit operator Event(EventSource<Nothing> impl) => new(impl);
}

public readonly struct Event<T>
{
    private readonly EventSource<T> _impl;
    public Event(EventSource<T> impl) => _impl = impl;
    public EventSubscription<T> Sub(Action<T> action) => _impl.Sub(action);
    internal void Unsub(Action<T> action) => _impl.Unsub(action);
    public static implicit operator Event<T>(EventSource<T> impl) => new(impl);
}

public readonly struct Nothing();

public static class EventSourceExtensions
{
    extension (EventSource<Nothing> s)
    {
        public void Invoke() => s.Invoke(default);
    }
    extension (Event<Nothing> s)
    {
        public EventSubscription<Nothing> Sub(Action a)
        {
            var ret = s.Sub(x =>
            {
                _ = x;
                a();
            });
            return ret;
        }
    }
    extension (IDispatcher d)
    {
        public EventSource<T> CreateEvent<T>() => new(d.GetDispatcher<T>());
        public EventSource<Nothing> CreateEvent() => new(d.GetDispatcher<Nothing>());
    }
    extension<T>(IDispatcher<T> d)
    {
        public EventSource<T> CreateEvent() => new(d);
    }
}

public sealed class EventSource<T>
{
    private Action<T>? _impl;
    private readonly IDispatcher<T> _dispatcher;

    public EventSource(IDispatcher<T> dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Invoke(T val)
    {
        if (_impl != null)
        {
            var callerId = new CallerIdentity(this);
            _dispatcher.Post(new(callerId, _impl, val));
        }
    }

    public EventSubscription<T> Sub(Action<T> action)
    {
        _impl += action;
        return new(this, action);
    }

    internal void Unsub(Action<T> action)
    {
        _impl -= action;
    }

    public Event<T> As() => this;
}
