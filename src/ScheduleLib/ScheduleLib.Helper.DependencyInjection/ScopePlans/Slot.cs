namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>Untyped view of <see cref="Slot{T}"/>, so the opener needs no reflection.</summary>
internal interface ISlot
{
    void Fill(object value);
}

internal sealed class Slot<T> : ISlot
    where T : class
{
    private T? _value;

    public T Value => _value ?? throw new ScopeValueNotAvailableException(typeof(T));

    void ISlot.Fill(object value)
    {
        if (_value is not null)
        {
            throw new ScopeValueAlreadyFilledException(typeof(T));
        }
        _value = (T) value;
    }
}
