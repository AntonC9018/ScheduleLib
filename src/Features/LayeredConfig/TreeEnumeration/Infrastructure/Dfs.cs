using System.Collections;
using ScheduleLib.Helper;

namespace Anton.LayeredConfig.TreeEnumeration.Infrastructure;

public enum DfsAction
{
    Recurse,
    PreventRecursionOnce,
    KeepPreventingRecursion,
}

public enum DfsVisitationState
{
    Start,
    BeforeProcess,
    Process,
    BeforeChildren,
    ProcessChild,
    AfterChildren,
    AfterProcess,
}

public sealed class DfsEnumerator() : IDfsEnumerator
{
    public DfsAction Action { get; set; } = DfsAction.Recurse;
    private Value _current = new()
    {
        Layer = null!,
        State = DfsVisitationState.Start,
    };
    private readonly EnumerationContextCollection _enumerationContextCollection;
    private readonly Stack<StackFrame> _stack = new();

    public DfsEnumerationContext Current => new(
        _current,
        this,
        _enumerationContextCollection);

    internal DfsEnumerator(
        MutableLayer root,
        EnumerationContextCollection enumerationContextCollection) : this()
    {
        _enumerationContextCollection = enumerationContextCollection;
        _stack.Push(new()
        {
            Layer = root,
            State = DfsVisitationState.BeforeProcess,
        });
    }

    public readonly struct Value
    {
        public required MutableLayer Layer { get; init; }
        public required DfsVisitationState State { get; init; }
    }

    public readonly struct StackFrame
    {
        public required MutableLayer Layer { get; init; }
        public DfsVisitationState State { get; init; }
        public int ChildIndex { get; init; }
    }

    private void SetCurrent(in StackFrame frame)
    {
        _current = new()
        {
            Layer = frame.Layer,
            State = frame.State,
        };
        foreach (var c in _enumerationContextCollection.EnumerateItems())
        {
            ((IDfsEnumerationContext) c.Value).Update(Current);
        }
    }

    private bool TryConsumePreventRecursion()
    {
        if (Action is DfsAction.KeepPreventingRecursion or DfsAction.PreventRecursionOnce)
        {
            if (Action == DfsAction.PreventRecursionOnce)
            {
                Action = DfsAction.Recurse;
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
                case DfsVisitationState.Process:
                case DfsVisitationState.ProcessChild:
                {
                    if (TryConsumePreventRecursion())
                    {
                        continue;
                    }
                    break;
                }
            }

            SetCurrent(frame);

            switch (frame.State)
            {
                case DfsVisitationState.BeforeProcess:
                {
                    _stack.Push(frame with
                    {
                        State = DfsVisitationState.Process,
                    });
                    return true;
                }
                case DfsVisitationState.Process:
                {
                    _stack.Push(frame with
                    {
                        State = DfsVisitationState.AfterProcess,
                    });

                    if (frame.Layer.ChildLayers.Count > 0)
                    {
                        _stack.Push(frame with
                        {
                            State = DfsVisitationState.BeforeChildren,
                        });
                    }
                    return true;
                }
                case DfsVisitationState.BeforeChildren:
                {
                    _stack.Push(frame with
                    {
                        State = DfsVisitationState.AfterChildren,
                    });

                    if (frame.Layer.ChildLayers.Count > 0)
                    {
                        _stack.Push(frame with
                        {
                            ChildIndex = 0,
                            State = DfsVisitationState.ProcessChild,
                        });
                    }
                    return true;
                }
                case DfsVisitationState.ProcessChild:
                {
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
                        State = DfsVisitationState.BeforeProcess,
                    });
                    continue;
                }
                case DfsVisitationState.AfterChildren:
                case DfsVisitationState.AfterProcess:
                {
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

public interface IDfsEnumerationContext : IEnumerationContext
{
    void Update(DfsEnumerationContext context);
}

public readonly struct DfsEnumerable : IDfsEnumerable
{
    private readonly MutableLayer _root;
    private readonly List<(EnumerationContextKey Key, Func<IDfsEnumerationContext> Factory)> _contextFactories = new();

    public DfsEnumerable(MutableLayer root)
    {
        _root = root;
    }

    public DfsEnumerable AddContext<T>(
        EnumerationContextKey<T> key,
        Func<T> factory)
        where T : class, IDfsEnumerationContext
    {
        if (_contextFactories.Any(x => x.Key == key.Value))
        {
            return this;
        }
        _contextFactories.Add((key.Value, factory));
        return this;
    }

    public DfsEnumerator GetEnumerator()
    {
        var contexts = ArrayBuilder.Create<KeyedContext>(_contextFactories.Count);
        foreach (var f in _contextFactories)
        {
            var it = f.Factory();
            contexts.Add(new(it, f.Key));
        }
        var col = new EnumerationContextCollection(contexts.Complete());
        return new(_root, col);
    }

    DfsEnumerator IDfsEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<DfsEnumerationContext> IEnumerable<DfsEnumerationContext>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}


public interface IDfsController
{
    DfsAction Action { get; set; }
}

public interface IDfsEnumerator :
    IDfsController,
    IEnumerator<DfsEnumerationContext>
{
}

public interface IDfsEnumerable :
    IEnumerable<DfsEnumerationContext>
{
    new DfsEnumerator GetEnumerator();
}
