// Needed to implement disposable helpers that unsub from events.
// TODO: Try to learn the messenger system in MVVM Toolkit, when I figure out the arch.

namespace Desktop.ViewModels;

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

public readonly struct Event<T>
{
    private readonly EventSource<T> _impl;
    public Event(EventSource<T> impl) => _impl = impl;
    public EventSubscription<T> Sub(Action<T> action) => _impl.Sub(action);
    public void Unsub(Action<T> action) => _impl.Unsub(action);
    public static implicit operator Event<T>(EventSource<T> impl) => new(impl);
}

public sealed class EventSource<T>
{
    private Action<T>? _impl;
    public void Invoke(T val) => _impl?.Invoke(val);
    public EventSubscription<T> Sub(Action<T> action)
    {
        _impl += action;
        return new(this, action);
    }

    public void Unsub(Action<T> action)
    {
        _impl -= action;
    }
}

public readonly struct EventSubscription : IDisposable
{
    private readonly Event _ev;
    private readonly Action _action;

    public EventSubscription(Event ev, Action action)
    {
        _ev = ev;
        _action = action;
    }

    public void Dispose() => _ev.Unsub(_action);
}

public readonly struct Event
{
    private readonly EventSource _impl;
    public Event(EventSource impl) => _impl = impl;
    public EventSubscription Sub(Action action) => _impl.Sub(action);
    public void Unsub(Action action) => _impl.Unsub(action);
    public static implicit operator Event(EventSource impl) => new(impl);
}

public sealed class EventSource
{
    private Action? _impl;
    public void Invoke() => _impl?.Invoke();
    public EventSubscription Sub(Action action)
    {
        _impl += action;
        return new(this, action);
    }

    public void Unsub(Action action) => _impl -= action;
}
