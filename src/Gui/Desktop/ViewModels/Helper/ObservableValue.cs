namespace Desktop.ViewModels;

public struct ObservableValueSource<T>(T initialValue)
{
    public readonly EventSource<T> Event = new();
    private T _value = initialValue;
    public void SetDirect(T value) => _value = value;
    public T Value
    {
        get => _value;
        set
        {
            if (!EqualityComparer<T>.Default.Equals(_value, value))
            {
                _value = value;
                Event.Invoke(value);
            }
        }
    }
}

public readonly ref struct ObservableValue<T>
{
    private readonly ref ObservableValueSource<T> _source;

    public ObservableValue(ref ObservableValueSource<T> source)
    {
        _source = ref source;
    }

    public void Set(T value)
    {
        _source.Value = value;
    }
    public T Get()
    {
        return _source.Value;
    }
    public Event<T> Changed => _source.Event;
}

public readonly ref struct ReadOnlyObservableValue<T>
{
    private readonly ref readonly ObservableValueSource<T> _source;

    public ReadOnlyObservableValue(ref readonly ObservableValueSource<T> source)
    {
        _source = ref source;
    }

    public T Get() => _source.Value;
    public Event<T> Changed => _source.Event;
}

public static class ObservableValueHelper
{
    public static ObservableValue<T> As<T>(this ref ObservableValueSource<T> s) => new(ref s);
    public static ReadOnlyObservableValue<T> AsReadOnly<T>(this ref ObservableValueSource<T> s) => new(ref s);
}
