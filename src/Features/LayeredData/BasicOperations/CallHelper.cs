using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredData;

public static class CallMergerHelper
{
    private delegate object MergeDelegate(
        object merger,
        object from,
        object? into);

    private static readonly MethodInfo _genericMethod = typeof(CallMergerHelper)
        .GetMethod(nameof(Merge), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly CallHelper<MergeDelegate> _callHelper = new(
        interfaceType: typeof(IMerger<>),
        methodInfo: _genericMethod);

    private static object Merge<T>(
        object merger,
        object from,
        object? into)
    {
        Debug.Assert(
            merger.GetType().IsAssignableTo(typeof(IMerger<T>)),
            "The merger must implement the requested typed merger contract.");
        Debug.Assert(
            from.GetType().IsAssignableTo(typeof(T)),
            "The source value must match the merger input type.");
        Debug.Assert(
            into is null || into.GetType().IsAssignableTo(typeof(T)),
            "A non-null destination value must match the merger input type.");

        var typedMerger = (IMerger<T>) merger;
        var source = (T) from;
        var destination = (T?) into;
        var ret = typedMerger.Merge(@from: source, @into: destination);
        return ret!;
    }

    public static object Merge(
        object merger,
        object from,
        object? into)
    {
        var mergerType = merger.GetType();
        var mergeDelegate = _callHelper.Get(mergerType);
        return mergeDelegate(
            merger: merger,
            @from: from,
            @into: into);
    }

    public static T MergeUsingService<T>(
        IServiceProvider sp,
        T from,
        T? into)
    {
        var t = from!.GetType();
        var mergerType = typeof(IMerger<>).MakeGenericType(t);
        var merger = sp.GetRequiredService(mergerType);
        var ret = Merge(
            merger: merger,
            @from: from,
            @into: into);
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
        return (T) Copy(operations: operations, @from: from);
    }

    public static object Copy(object operations, object from)
    {
        var operationsType = operations.GetType();
        var copyDelegate = _callHelper.Get(operationsType);
        return copyDelegate(operations: operations, @from: from);
    }

    private delegate object CopyDelegate(object operations, object from);

    private static readonly MethodInfo _genericMethod = typeof(CallCopyHelper)
        .GetMethod(nameof(Copy), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static object Copy<T>(object operations, object from)
    {
        Debug.Assert(
            operations.GetType().IsAssignableTo(typeof(IBasicOperations<T>)),
            "The operations value must support the IBasicOperations<T> contract.");
        Debug.Assert(
            from.GetType().IsAssignableTo(typeof(T)),
            "The source value must match the merger input type.");
        var typedOperations = (IBasicOperations<T>) operations;
        var source = (T) from;
        var ret = typedOperations.Copy(source);
        return ret!;
    }

    private static readonly CallHelper<CopyDelegate> _callHelper = new(
        typeof(IBasicOperations<>),
        _genericMethod);
}
