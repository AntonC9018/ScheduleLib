using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Desktop.MvvmEssentials;

namespace Desktop.MainWindow;

public sealed class ManualObservableList<T> : IList<T>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly List<T> _items = new();
    private readonly IDispatcher _dispatcher;

    public ManualObservableList(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void TriggerChanged()
    {
        _ = PropertyChanged;
        var callerId = new CallerIdentity(this);

        // _dispatcher.Post<PropertyChangedEventArgs>(new(
        //     callerId,
        //     x => PropertyChanged?.Invoke(this, x),
        //     new("Item[]")));
        // _dispatcher.Post<PropertyChangedEventArgs>(new(
        //     callerId,
        //     x => PropertyChanged?.Invoke(this, x),
        //     new(nameof(Count))));
        _dispatcher.Post<NotifyCollectionChangedEventArgs>(new(
            callerId,
            x => CollectionChanged?.Invoke(this, x),
            new(NotifyCollectionChangedAction.Reset)));
    }

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(T item) => _items.Add(item);
    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => _items.Clear();
    public bool Contains(object? value) => throw new NotSupportedException();
    public int IndexOf(object? value) => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => throw new NotSupportedException();

    public int Count => _items.Count;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public bool IsReadOnly => false;
    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (T) value!;
    }

    public int IndexOf(T item) => _items.IndexOf(item);
    public void Insert(int index, T item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public bool IsFixedSize => false;

    public T this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException();
    }
}
