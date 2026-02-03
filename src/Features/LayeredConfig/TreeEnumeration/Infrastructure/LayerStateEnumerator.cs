using System.Collections;

namespace Anton.LayeredConfig.TreeEnumeration.Infrastructure;

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

public readonly struct LayerStateEnumerable : ILayerStateEnumerable
{
    private readonly MutableLayer _root;

    public LayerStateEnumerable(MutableLayer root)
    {
        _root = root;
    }

    public LayerStateEnumerator GetEnumerator() => new(_root);
    ILayerStateEnumerator ILayerStateEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<LayerStateEnumerator.Value> IEnumerable<LayerStateEnumerator.Value>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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

public interface ILayerStateEnumerator :
    IEnumerator<LayerStateEnumerator.Value>
{
    VisitorAction Action { get; set; }
}

public interface ILayerStateEnumerable : IEnumerable<LayerStateEnumerator.Value>
{
    new ILayerStateEnumerator GetEnumerator();
}
