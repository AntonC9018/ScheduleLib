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
    private readonly TreeBuilder _tree;

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
        var path = await change();
        if (path == default)
        {
            path = CreateNewPath(_path.NodePath.Get());
        }
        Debug.Assert(path != default);
        await Dispatcher.UIThread.InvokeSyncFallingBackToAsync(Continue);

        void Continue()
        {
            _path.NodePath.Set(path);
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
                Debug.Assert(_tree.BaseNode == currentNode);
                return prevNodePath;
            }
            return new(builder.DrainToImmutable());
        }
    }
}
