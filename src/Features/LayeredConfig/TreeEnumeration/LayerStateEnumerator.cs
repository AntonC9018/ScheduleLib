using System.Collections;

namespace Anton.LayeredConfig;

public enum VisitorAction
{
    Recurse,
    PreventRecursionOnce,
    KeepPreventingRecursion,
}

public enum VisitorState
{
    Start,
    BeforeProcess,
    Process,
    ProcessChild,
    AfterProcess,
}


public struct ConsumerWrappedLayerEnumerator<TEnumerator, TConsumer> : ILayerStateEnumerator
    where TConsumer : ILayerStateEnumerationConsumer
    where TEnumerator : ILayerStateEnumerator
{
    // Making this a struct is way too complicated, impossible to maintain.
    private readonly TConsumer _consumer;
    public TEnumerator InnerE;

    public ConsumerWrappedLayerEnumerator(
        TEnumerator innerE,
        TConsumer consumer)
    {
        _consumer = consumer;
        InnerE = innerE;
    }

    public bool MoveNext()
    {
        if (InnerE.MoveNext())
        {
            _consumer.Consume(Current);
            return true;
        }
        return false;
    }

    public void Reset() => InnerE.MoveNext();
    public LayerStateEnumerator.Value Current => InnerE.Current;
    object? IEnumerator.Current => InnerE.Current;
    public void Dispose() => InnerE.Dispose();
    public VisitorAction Action
    {
        get => InnerE.Action;
        set => InnerE.Action = value;
    }
}

public interface ILayerStateEnumerationConsumer
{
    void Consume(LayerStateEnumerator.Value value);
}

public interface ILayerStateEnumerator : IEnumerator<LayerStateEnumerator.Value>
{
    VisitorAction Action { get; set; }
}

public readonly struct ActionConsumer(Action<LayerStateEnumerator.Value> action) : ILayerStateEnumerationConsumer
{
    public void Consume(LayerStateEnumerator.Value value) => action(value);
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

public static class LayerStateEnumeratorBuilderExtensions
{
    extension<T>(T e) where T : ILayerStateEnumerator
    {
        public ConsumerWrappedLayerEnumerator<T, TConsumer> WithConsumer<TConsumer>(TConsumer consumer)
            where TConsumer : ILayerStateEnumerationConsumer
        {
            return new(e, consumer);
        }
        public ConsumerWrappedLayerEnumerator<T, ActionConsumer> WithConsumer(Action<LayerStateEnumerator.Value> action)
        {
            return new(e, new(action));
        }
    }
}

public struct LayerStateEnumerator() : ILayerStateEnumerator
{
    public VisitorAction Action { get; set; } = VisitorAction.Recurse;
    public Value Current { get; private set; } = new()
    {
        Layer = null!,
        State = VisitorState.Start,
    };
    private readonly Stack<StackFrame> _stack = new();

    public LayerStateEnumerator(MutableLayer root) : this()
    {
        _stack.Push(new()
        {
            Layer = root,
            State = VisitorState.BeforeProcess,
        });
    }

    public readonly struct Value
    {
        public required MutableLayer Layer { get; init; }
        public required VisitorState State { get; init; }
    }

    public readonly struct StackFrame
    {
        public required MutableLayer Layer { get; init; }
        public VisitorState State { get; init; }
        public int ChildIndex { get; init; }
    }

    private void SetCurrent(in StackFrame frame)
    {
        Current = new()
        {
            Layer = frame.Layer,
            State = frame.State,
        };
    }

    private bool TryConsumePreventRecursion()
    {
        if (Action is VisitorAction.KeepPreventingRecursion or VisitorAction.PreventRecursionOnce)
        {
            if (Action == VisitorAction.PreventRecursionOnce)
            {
                Action = VisitorAction.Recurse;
            }
            return true;
        }
        return false;
    }

    public bool MoveNext()
    {
        while (true)
        {
            if (_stack.Count == 0)
            {
                return false;
            }
            var frame = _stack.Pop();

            switch (frame.State)
            {
                case VisitorState.BeforeProcess:
                {
                    SetCurrent(frame);
                    _stack.Push(frame with
                    {
                        State = VisitorState.Process,
                    });
                    return true;
                }
                case VisitorState.Process:
                {
                    if (TryConsumePreventRecursion())
                    {
                        continue;
                    }
                    SetCurrent(frame);
                    _stack.Push(frame with
                    {
                        State = VisitorState.AfterProcess,
                    });

                    if (frame.Layer.ChildLayers.Count > 0)
                    {
                        _stack.Push(frame with
                        {
                            ChildIndex = 0,
                            State = VisitorState.ProcessChild,
                        });
                    }
                    return true;
                }

                case VisitorState.ProcessChild:
                {
                    if (TryConsumePreventRecursion())
                    {
                        continue;
                    }

                    var nextChildIndex = frame.ChildIndex + 1;
                    var childLayers = frame.Layer.ChildLayers;
                    if (nextChildIndex < childLayers.Count)
                    {
                        _stack.Push(frame with
                        {
                            ChildIndex = nextChildIndex,
                        });
                    }

                    var child = childLayers[frame.ChildIndex].Model;
                    _stack.Push(new StackFrame
                    {
                        Layer = child,
                        State = VisitorState.BeforeProcess,
                    });
                    continue;
                }

                case VisitorState.AfterProcess:
                {
                    SetCurrent(frame);
                    return true;
                }

                default:
                {
                    throw Unreachable();
                }
            }
        }
    }

    public void Dispose()
    {
    }
    public void Reset()
    {
        throw new NotImplementedException();
    }

    object? IEnumerator.Current => Current;
}
