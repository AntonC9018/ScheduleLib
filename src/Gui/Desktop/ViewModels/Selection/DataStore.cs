using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Anton.LayeredData;
using Avalonia.Threading;

namespace Desktop.ViewModels;

public sealed class DataStore : IDisposable
{
    public TreeContext TreeContext { get; }
    public SelectedNodePathModel SelectedNodePath { get; }
    public NodeDataChangeDispatcher NodeDataChangeDispatcher { get; }
    public UiSelectedNodeViewModel UiSelectedNodeView { get; }
    public EventSource<Void> TreeStructureChanged { get; }

    private DataStore(
        TreeContext t,
        SelectedNodePathModel selectedNodePath,
        NodeDataChangeDispatcher nodeDataChangeDispatcher,
        UiSelectedNodeViewModel uiSelectedNodeView,
        EventSource<Void> treeStructureChanged)
    {
        TreeContext = t;
        SelectedNodePath = selectedNodePath;
        NodeDataChangeDispatcher = nodeDataChangeDispatcher;
        UiSelectedNodeView = uiSelectedNodeView;
        TreeStructureChanged = treeStructureChanged;
    }

    public static DataStore Create(TreeContext t)
    {
        var nodePath = new SelectedNodePathModel(t);
        var dispatcher = new NodeDataChangeDispatcher(
            t.Dispatcher,
            nodePath.SelectedNode.Changed);
        var uiNode = new UiSelectedNodeViewModel(t, nodePath);
        var treeStructureChanged = t.Dispatcher.CreateEvent();
        return new(
            t: t,
            nodeDataChangeDispatcher: dispatcher,
            selectedNodePath: nodePath,
            uiSelectedNodeView: uiNode,
            treeStructureChanged: treeStructureChanged);
    }

    public void Dispose()
    {
        NodeDataChangeDispatcher.Dispose();
        UiSelectedNodeView.Dispose();
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

    private bool _isQueueing;

    private readonly record struct Key(CallerIdentity CallerId, Type Type, string? PropertyName);
    private readonly ConcurrentQueue<Key> _keyQueue = new();
    private readonly ConcurrentDictionary<Key, Action> _latestActions = new();

    public void StartQueueing()
    {
        if (!_keyQueue.IsEmpty
            || Interlocked.CompareExchange(ref _isQueueing, true, false))
        {
            Debug.Fail("Started two queue ops at once?");
            return;
        }
    }

    public void EndQueueing()
    {
        if (Interlocked.CompareExchange(ref _isQueueing, false, false))
        {
            throw new InvalidOperationException("Cannot end queueing while not queueing");
        }
        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException("Can only end queueing on the UI thread");
            }

            while (_keyQueue.TryDequeue(out var key))
            {
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
        }
    }

    public void Post<T>(PostArgs<T> args)
    {
        if (Volatile.Read(ref _isQueueing) == false)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException("Cannot execute action immediately when not on UI thread");
            }
            args.Invoke();
            return;
        }

        var key = new Key(args.CallerId, typeof(T), PropertyName: null);
        if (args.Arg is PropertyChangedEventArgs changedArgs)
        {
            key = key with
            {
                PropertyName = changedArgs.PropertyName,
            };
        }
        else if (args.Arg is PropertyChangingEventArgs changingArgs)
        {
            key = key with
            {
                PropertyName = changingArgs.PropertyName,
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

        if (Volatile.Read(ref _isQueueing) == false)
        {
            // This is kind of the best we can do.
            _latestActions.TryRemove(key, out _);
            throw new InvalidOperationException("Queueing ended before the final message was processed");
        }
    }
}
