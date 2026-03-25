namespace Desktop.MainWindow;

public ref struct ItemBorrow<T> : IDisposable
{
    private ref ItemOwner<T> _owner;

    public T Value
    {
        get
        {
            return _owner._item;
        }
    }

    public ItemBorrow(ref ItemOwner<T> owner)
    {
        _owner = ref owner;
        owner.Borrow();
    }

    public void Dispose()
    {
        _owner.Return();
    }
}

public static class ItemInUseHelper
{
    public static ItemBorrow<T> BorrowHelper<T>(
        this ref ItemOwner<T> owner)
    {
        return new(ref owner);
    }
}

public struct ItemOwner<T>
{
    internal readonly T _item;
    private bool _isInUse;

    public ItemOwner(T item)
    {
        _item = item;
    }

    public T Borrow()
    {
        if (Interlocked.CompareExchange(ref _isInUse, true, false) != false)
        {
            throw new InvalidOperationException("Cannot get this, it's being used already.");
        }

        return _item;
    }

    public void Return()
    {
        if (Interlocked.CompareExchange(ref _isInUse, false, true) != true)
        {
            throw new InvalidOperationException("Trying to return item not in use.");
        }
    }
}
