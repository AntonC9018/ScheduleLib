using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MainCli.BuilderNew;

public static class CallMergerHelper
{
    private delegate object MergeDelegate(object merger, object from, object? into);
    private static readonly MethodInfo _genericMethod = typeof(CallMergerHelper)
        .GetMethod(nameof(Merge), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly CallHelper<MergeDelegate> _callHelper = new(
            interfaceType: typeof(IMerger<>),
            methodInfo: _genericMethod);

    private static object Merge<T>(object merger, object from, object? into)
    {
        Debug.Assert(merger.GetType().IsAssignableTo(typeof(IMerger<T>)));
        Debug.Assert(from.GetType().IsAssignableTo(typeof(T)));
        Debug.Assert(into is null || into.GetType().IsAssignableTo(typeof(T)));

        var merger1 = (IMerger<T>) merger;
        var from1 = (T) from;
        var into1 = (T?) into;
        var ret = merger1.Merge(from1, into1);
        return ret!;
    }

    public static object Merge(object merger, object from, object? into)
    {
        var mergerType = merger.GetType();
        var mergeDelegate = _callHelper.Get(mergerType);
        return mergeDelegate(merger, from, into);
    }

    public static T MergeUsingService<T>(IServiceProvider sp, T from, T? into)
    {
        var t = from!.GetType();
        var mergerType = typeof(IMerger<>).MakeGenericType(t);
        var merger = sp.GetRequiredService(mergerType);
        var ret = Merge(merger, from, into);
        return (T) ret;
    }
}

public static class CallCopyHelper
{
    public static T CopyUsingService<T>(IServiceProvider sp, T from)
    {
        var fromType = from!.GetType();
        var basicOperationsType = typeof(IBasicOperations<>).MakeGenericType(fromType);
        var operations = sp.GetRequiredService(basicOperationsType);
        return (T) Copy(operations, from);
    }

    public static object Copy(object operations, object from)
    {
        var operationsType = operations.GetType();
        var copyDelegate = _callHelper.Get(operationsType);
        return copyDelegate(operations, from);
    }

    private delegate object CopyDelegate(object operations, object from);

    private static readonly MethodInfo _genericMethod = typeof(CallCopyHelper)
        .GetMethod(nameof(Copy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static object Copy<T>(object operations, object from)
    {
        Debug.Assert(operations.GetType().IsAssignableTo(typeof(IBasicOperations<T>)));
        Debug.Assert(from.GetType().IsAssignableTo(typeof(T)));
        var operations1 = (IBasicOperations<T>) operations;
        var from1 = (T) from;
        var ret = operations1.Copy(from1);
        return ret!;
    }

    private static readonly CallHelper<CopyDelegate> _callHelper = new(
        typeof(IBasicOperations<>),
        _genericMethod);
}
