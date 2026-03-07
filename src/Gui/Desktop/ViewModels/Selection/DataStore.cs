using System.Collections.Concurrent;
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
    private readonly ConcurrentQueue<Action> _callbackQueue = new();

    public void StartQueueing()
    {
        if (!_callbackQueue.IsEmpty
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

            while (true)
            {
                if (!_callbackQueue.TryDequeue(out var item))
                {
                    break;
                }
                item();
            }
        }
        catch
        {
            _callbackQueue.Clear();
        }
    }

    public void Post<T>(object callerIdentity, Action<T> a, T arg)
    {
        if (Volatile.Read(ref _isQueueing) == false)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException("Cannot execute action immediately when not on UI thread");
            }
            a(arg);
            return;
        }

        _callbackQueue.Enqueue(() => a(arg));

        // It could happen that this executes after the final item has been removed.
        if (Volatile.Read(ref _isQueueing) == false)
        {
            // We can't really do any better, I don't think.
            _callbackQueue.TryDequeue(out _);

            throw new InvalidOperationException("Queueing ended before the final message was processed");
        }
    }
}
