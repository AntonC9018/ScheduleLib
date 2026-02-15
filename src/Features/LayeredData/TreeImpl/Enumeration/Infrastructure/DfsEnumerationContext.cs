namespace Anton.LayeredData.TreeEnumeration.Infrastructure;

public readonly struct DfsEnumerationContext
{
    public readonly DfsEnumerator.Value Value;
    public MutableNode Node => Value.Node;
    public DfsVisitationState State => Value.State;

    public readonly IDfsController Controller;
    public readonly EnumerationContextCollection ContextCollection;

    public DfsEnumerationContext(
        DfsEnumerator.Value value,
        IDfsController controller,
        EnumerationContextCollection contextCollection)
    {
        Value = value;
        Controller = controller;
        ContextCollection = contextCollection;
    }

    public T Get<T>(EnumerationContextKey<T> key) => ContextCollection.Get(key);
    public bool IsNull => Controller == null;
}

public sealed class EnumerationContextKeyRegistry
{
    private readonly NameRegistry<EnumerationContextKey> _impl = new();

    public EnumerationContextKey<T> Register<T>() where T : class
    {
        var ret = Register<T>(typeof(T).Name);
        return ret;
    }
    public EnumerationContextKey<T> Register<T>(string name) where T : class
    {
        var ret = _impl.Register(name);
        return new(ret);
    }
}

public readonly record struct EnumerationContextKey(string Value) : ICreateFromString<EnumerationContextKey>
{
    public static readonly EnumerationContextKeyRegistry Registry = new();
    public static EnumerationContextKey Create(string val) => new(val);
}
public readonly record struct EnumerationContextKey<T>(EnumerationContextKey Value);

public interface IEnumerationContext
{
}

public readonly record struct KeyedContext(IEnumerationContext Value, EnumerationContextKey Key);

public readonly struct EnumerationContextCollection : IDisposable
{
    private readonly KeyedContext[] _items;

    public EnumerationContextCollection(KeyedContext[] items)
    {
        _items = items;
    }

    public T Get<T>(EnumerationContextKey<T> key)
    {
        foreach (var x in _items)
        {
            if (x.Key == key.Value)
            {
                return (T) x.Value;
            }
        }
        throw new KeyNotFoundException($"{key.Value.Value} not found");
    }

    public void Dispose()
    {
        foreach (var x in _items)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            if (x.Value is IDisposable d)
            {
                d.Dispose();
            }
        }
    }

    public ItemsEnumerable EnumerateItems() => new(this);

    public readonly struct ItemsEnumerable(EnumerationContextCollection self)
    {
        public ItemsEnumerator GetEnumerator() => new(self);
    }

    public struct ItemsEnumerator(EnumerationContextCollection self)
    {
        private int _index = -1;

        public bool MoveNext()
        {
            _index++;
            if (_index < self._items.Length)
            {
                return true;
            }
            return false;
        }

        public KeyedContext Current => self._items[_index];
    }
}
