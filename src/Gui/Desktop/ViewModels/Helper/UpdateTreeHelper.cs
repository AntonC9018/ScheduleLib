using System.Collections.Immutable;
using System.Diagnostics;
using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;
using AutoConstructor.Attributes;
using Avalonia.Threading;

namespace Desktop.ViewModels;

[AutoConstructor]
public sealed partial class UpdateTreeHelper
{
    private readonly SelectedNodePathModel _path;
    private readonly TreeContext _treeContext;

    public void ExecTreeAction(Func<NodePath> change)
    {
        ExecTreeActionAsync(() =>
        {
            var ret = change();
            return ValueTask.FromResult(ret);
        }).EnsureCompletedSync();
    }

    public async ValueTask ExecTreeActionAsync(Func<ValueTask<NodePath>> change)
    {
        _treeContext.Dispatcher.StartQueueing();

        var previousPath = _path.NodePath.Get();
        var path = await change();
        if (path == default)
        {
            path = CreateNewPath(previousPath);
        }
        Debug.Assert(path != default);
        _path.NodePath.Set(path);
        await Dispatcher.UIThread.InvokeSyncFallingBackToAsync(Continue);

        void Continue()
        {
            _treeContext.Dispatcher.EndQueueing();
        }
    }

    private NodePath CreateNewPath(NodePath prevNodePath)
    {
        var prevPath = prevNodePath.Path;
        int currentIndex = 0;
        while (true)
        {
            var currentNode = prevPath[currentIndex];
            int nextIndex = currentIndex + 1;
            if (nextIndex >= prevPath.Length)
            {
                break;
            }

            var nextNode = prevPath[nextIndex];
            if (!currentNode.ChildNodes.Contains(nextNode))
            {
                // Done.
                // We don't detect moving nodes between paths,
                // but that is never used in the program either way.
                // Can make the function return the new path, if such control is desired.
                return new(prevPath[.. nextIndex]);
            }

            currentIndex++;
        }

        {
            // Will include the whole thing in the output.
            // Now scan until a leaf.
            var lastNode = prevPath[^1];
            ImmutableArray<MutableNode>.Builder? builder = null;
            var currentNode = lastNode;
            while (true)
            {
                var count = currentNode.ChildNodes.Count;
                if (count == 0)
                {
                    if (builder is null)
                    {
                        return prevNodePath;
                    }
                    break;
                }
                if (count == 1)
                {
                    currentNode = currentNode.ChildNodes[0];
                    if (builder is null)
                    {
                        builder = ImmutableArray.CreateBuilder<MutableNode>();
                        builder.AddRange(prevPath);
                    }
                    builder.Add(currentNode);
                    continue;
                }
                // We only have branching on the first level currently, so this is impossible.
                Debug.Assert(_treeContext.Tree.BaseNode == currentNode);
                return prevNodePath;
            }
            return new(builder.DrainToImmutable());
        }
    }
}
