using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Anton.LayeredData;
using Desktop.MvvmEssentials;
using IDispatcher = Desktop.MvvmEssentials.IDispatcher;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Desktop.ViewModelData;

public sealed class DataStore : IDisposable
{
    public TreeContext TreeContext { get; }
    public SelectedNodePathModel SelectedNodePath { get; }
    public NodeDataChangeDispatcher NodeDataChangeDispatcher { get; }
    public UiSelectedNodeViewModel UiSelectedNodeViewModel { get; }
    public LayerLevelSelectionViewModel LayerLevelViewModel { get; }
    public EventSource<Nothing> TreeStructureChanged { get; }

    private DataStore(
        TreeContext t,
        SelectedNodePathModel selectedNodePath,
        NodeDataChangeDispatcher nodeDataChangeDispatcher,
        UiSelectedNodeViewModel uiSelectedNodeViewModel,
        EventSource<Nothing> treeStructureChanged,
        LayerLevelSelectionViewModel layerLevelViewModel)
    {
        TreeContext = t;
        SelectedNodePath = selectedNodePath;
        NodeDataChangeDispatcher = nodeDataChangeDispatcher;
        UiSelectedNodeViewModel = uiSelectedNodeViewModel;
        TreeStructureChanged = treeStructureChanged;
        LayerLevelViewModel = layerLevelViewModel;
    }

    public static DataStore Create(TreeContext t)
    {
        var nodePath = new SelectedNodePathModel(t);
        var dispatcher = new NodeDataChangeDispatcher(
            t.Dispatcher,
            nodePath.SelectedNode.Changed);
        var uiNode = new UiSelectedNodeViewModel(t, nodePath);
        var treeStructureChanged = t.Dispatcher.CreateEvent();
        var layerLevelVm = new LayerLevelSelectionViewModel(t.Dispatcher, nodePath);
        return new(
            t: t,
            nodeDataChangeDispatcher: dispatcher,
            selectedNodePath: nodePath,
            uiSelectedNodeViewModel: uiNode,
            treeStructureChanged: treeStructureChanged,
            layerLevelViewModel: layerLevelVm);
    }

    public void Dispose()
    {
        NodeDataChangeDispatcher.Dispose();
        UiSelectedNodeViewModel.Dispose();
    }
}

public sealed class TreeContext
{
    public TreeBuilder Tree { get; }
    public TreeEventDispatcher Dispatcher { get; }

    public TreeContext(
        TreeBuilder tree,
        TreeEventDispatcher dispatcher)
    {
        Tree = tree;
        Dispatcher = dispatcher;
    }
}

public sealed class TreeEventDispatcher : IDispatcher
{
    public IDispatcher<T> GetDispatcher<T>() => new DelegatingDispatcher<T>(this);

    private enum QueueingState
    {
        NotQueueing,
        Queueing,
        EmptyingQueue,
    }

    private QueueingState _isQueueing;

    private readonly record struct Key(CallerIdentity CallerId, Type Type, string? PropertyName = null);

    private readonly ConcurrentQueue<Key> _keyQueue = new();
    private readonly ConcurrentDictionary<Key, Action> _latestActions = new();

    public void StartQueueing()
    {
        bool CalculateConditionResult()
        {
            if (_keyQueue.IsEmpty)
            {
                return _latestActions.IsEmpty;
            }
            else
            {
                return false;
            }
        }

        bool conditionResult = CalculateConditionResult();


        bool areQueuesEmpty = conditionResult;
        var prevState = Interlocked.CompareExchange(
            location1: ref _isQueueing,
            value: QueueingState.Queueing,
            comparand: QueueingState.NotQueueing);
        bool conditionResult2;
        if (prevState != QueueingState.NotQueueing)
        {
            conditionResult2 = true;
        }
        else
        {
            conditionResult2 = areQueuesEmpty;
            conditionResult2 = !(conditionResult2);
        }

        if (conditionResult2)
        {
            Debug.Fail("Started two queue ops at once?");
            return;
        }
        Debug.Assert(Volatile.Read(ref _isQueueing) == QueueingState.Queueing);
    }

    public void EndQueueing()
    {
        var prevQueueing = Interlocked.CompareExchange(
            location1: ref _isQueueing,
            value: QueueingState.EmptyingQueue,
            comparand: QueueingState.Queueing);
        if (prevQueueing != QueueingState.Queueing)
        {
            throw new InvalidOperationException("Cannot end queueing while not queueing");
        }
        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException("Can only end queueing on the UI thread");
            }

            while (true)
            {
                if (!_keyQueue.TryDequeue(out var key))
                {
                    break;
                }
                if (_latestActions.TryRemove(key, out var action))
                {
                    action();
                }
            }
        }
        catch
        {
            _keyQueue.Clear();
            _latestActions.Clear();
            throw;
        }
        finally
        {
            var updatedPrevQueueing = Interlocked.CompareExchange(
                location1: ref _isQueueing,
                value: QueueingState.NotQueueing,
                comparand: QueueingState.EmptyingQueue);
            Debug.Assert(updatedPrevQueueing == QueueingState.EmptyingQueue);
        }
    }

    public void Post<T>(PostArgs<T> args)
    {
        bool conditionResult;
        global::Desktop.ViewModelData.TreeEventDispatcher.QueueingState conditionValue = Volatile.Read(ref _isQueueing);
        conditionResult = conditionValue == QueueingState.NotQueueing;
        if (conditionResult)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException("Cannot execute action immediately when not on UI thread");
            }
            args.Invoke();
            return;
        }

        var key = new Key(args.CallerId, typeof(T));

        // When queueing, just have it reset all properties for now.
        // if (args.Arg is PropertyChangedEventArgs)
        // {
        //     args = args with
        //     {
        //         Arg = (T) (object) new PropertyChangedEventArgs(null),
        //     };
        // }
        // else if (args.Arg is PropertyChangingEventArgs)
        // {
        //     args = args with
        //     {
        //         Arg = (T) (object) new PropertyChangingEventArgs(null),
        //     };
        // }

        if (args.Arg is PropertyChangedEventArgs a1)
        {
            key = key with
            {
                PropertyName = a1.PropertyName,
            };
        }
        else if (args.Arg is PropertyChangingEventArgs a2)
        {
            key = key with
            {
                PropertyName = a2.PropertyName,
            };
        }

        var action = args.GetInvoker();
        _latestActions.AddOrUpdate(
            key,
            addValueFactory: k =>
            {
                _keyQueue.Enqueue(k);
                return action;
            },
            updateValueFactory: (k, prev) =>
            {
                _ = k;
                _ = prev;
                return action;
            });

        bool conditionResult2;
        global::Desktop.ViewModelData.TreeEventDispatcher.QueueingState
            conditionValue2 = Volatile.Read(ref _isQueueing);
        conditionResult2 = conditionValue2 == QueueingState.NotQueueing;
        if (conditionResult2)
        {
            // This is kind of the best we can do.
            _latestActions.TryRemove(key, out _);
            throw new InvalidOperationException("Queueing ended before the final message was processed");
        }
    }
}

public static class VolatileExtensions
{
    extension(Volatile)
    {
        public static T Read<T>(ref T location) where T : struct, Enum
        {
            switch (Unsafe.SizeOf<T>())
            {
                case 1:
                    {
                        ref var x = ref Unsafe.As<T, byte>(ref location);
                        byte ret = Volatile.Read(ref x);
                        ref var ret1 = ref Unsafe.As<byte, T>(ref ret);
                        return ret1;
                    }
                case 2:
                    {
                        ref var x = ref Unsafe.As<T, short>(ref location);
                        short ret = Volatile.Read(ref x);
                        ref var ret1 = ref Unsafe.As<short, T>(ref ret);
                        return ret1;
                    }
                case 4:
                    {
                        ref var x = ref Unsafe.As<T, int>(ref location);
                        int ret = Volatile.Read(ref x);
                        ref var ret1 = ref Unsafe.As<int, T>(ref ret);
                        return ret1;
                    }
                case 8:
                    {
                        ref var x = ref Unsafe.As<T, long>(ref location);
                        long ret = Volatile.Read(ref x);
                        ref var ret1 = ref Unsafe.As<long, T>(ref ret);
                        return ret1;
                    }
                default:
                    {
                        int size = Unsafe.SizeOf<T>();
                        throw new UnsupportedEnumUnderlyingTypeSizeException(size);
                    }
            }
        }
    }
}
