using System.Collections;

namespace Anton.LayeredConfig.TreeEnumeration.Infrastructure;

public static class LayerStateEnumeratorExtensions
{
    extension<T>(ref T e) where T : struct, ILayerStateEnumerator
    {
        public bool SkipCurrentChildren()
        {
            // Maybe do this better.
            e.Action = VisitorAction.KeepPreventingRecursion;
            while (e.MoveNext())
            {
                if (e.Current.State == VisitorState.AfterProcess)
                {
                    e.Action = VisitorAction.Recurse;
                    return true;
                }
            }
            return false;
        }
    }
}

public struct WrappedClassLayerEnumerator<T> : ILayerStateEnumerator
    where T : ILayerStateEnumerator
{
    private T _value;
    public WrappedClassLayerEnumerator(T value)
    {
        _value = value;
    }

    public void Dispose() => _value.Dispose();
    public bool MoveNext() => _value.MoveNext();
    public void Reset() => _value.Reset();
    LayerStateEnumerator.Value IEnumerator<LayerStateEnumerator.Value>.Current => _value.Current;
    object? IEnumerator.Current => _value.Current;
    public VisitorAction Action
    {
        get => _value.Action;
        set => _value.Action = value;
    }
}

public static class LayerStateEnumeratorClassExtensions
{
    extension<T>(T e) where T : class, ILayerStateEnumerator
    {
        public WrappedClassLayerEnumerator<T> WrapAsStruct() => new(e);

        public bool SkipCurrentChildren()
        {
            var s = e.WrapAsStruct();
            return s.SkipCurrentChildren();
        }
    }
}
