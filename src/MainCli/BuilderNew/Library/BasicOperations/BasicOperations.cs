using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MainCli.BuilderNew;

public interface IMerger
{
    public object Merge(object from, object into)
    {
        var type = from.GetType();
        var otherType = into.GetType();
        Debug.Assert(otherType == type || otherType.IsSubclassOf(type));

        var interfaces = this.GetType().GetInterfaces()
            .Where(x =>
            {
                if (!x.IsGenericType)
                {
                    return false;
                }
                if (x.GetGenericTypeDefinition() != typeof(IMerger<>))
                {
                    return false;
                }
                return true;
            })
            .Select(x =>
            {
                var args = x.GetGenericArguments();
                if (args.Length == 0)
                {
                    throw Unreachable();
                }
                return args[0];
            });
        var self = interfaces.Single();
        Debug.Assert(self == type);

        var genericInterface = typeof(IMerger<>).MakeGenericType(self);
        var interfaceMap = self.GetInterfaceMap(genericInterface);
        var method = interfaceMap.TargetMethods[^1];
        var ret = method.Invoke(this, [from, into]);
        return ret!;
    }
}

public interface IMerger<T> : IMerger
{
    public T Merge(T from, T? into);
}

internal static class CallMergerHelper
{
    private delegate object MergeDelegate(object merger, object from, object? into);
    private static readonly ConcurrentDictionary<Type, MergeDelegate> _mergeDelegateCache = new();
    private static readonly MethodInfo _genericMethod = typeof(CallCopyHelper)
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
}

internal static class CallCopyHelper
{
    public static object CopyUsingService(IServiceProvider sp, object from)
    {
        var fromType = from.GetType();
        var basicOperationsType = typeof(IBasicOperations<>).MakeGenericType(fromType);
        var operations = sp.GetRequiredService(basicOperationsType);
        return Copy(operations, from);
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

public sealed class ReflectionMerger<T> : IMerger<T>
{
    private readonly IBasicOperations<T> _basicOperations;
    private readonly IServiceProvider _serviceProvider;
    private readonly PropertyInfo[] _writableProperties;

    public ReflectionMerger(
        IServiceProvider serviceProvider,
        IBasicOperations<T> basicOperations)
    {
        _serviceProvider = serviceProvider;
        _basicOperations = basicOperations;

        // Get all writable properties
        _writableProperties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.CanRead)
            .ToArray();
    }

    public T Merge(T from, T? to)
    {
        if (to == null)
        {
            to = _basicOperations.Empty();
        }
        if (from == null || to == null)
        {
            throw new ArgumentNullException(from == null ? nameof(from) : nameof(to));
        }

        foreach (var property in _writableProperties)
        {
            var sourceValue = property.GetValue(from);
            if (sourceValue is null)
            {
                continue;
            }

            var targetValue = property.GetValue(to);
            var merger = _serviceProvider.GetService(typeof(IMerger<>).MakeGenericType(property.PropertyType));
            if (merger != null)
            {
                targetValue = CallMergerHelper.Merge(merger, sourceValue, targetValue);
                property.SetValue(to, targetValue);
                continue;
            }

            var copier = _serviceProvider.GetService(typeof(IBasicOperations<>).MakeGenericType(property.PropertyType));
            if (copier != null)
            {
                targetValue = CallCopyHelper.Copy(copier, sourceValue);
                property.SetValue(to, targetValue);
                continue;
            }

            property.SetValue(to, sourceValue);
        }
        return to;
    }
}

// TODO: Source generate these for all types.
// claude
public sealed class ReflectionBasicOperations<T> : IBasicOperations<T>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConstructorInfo? _parameterlessConstructor;

    public ReflectionBasicOperations(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        // Get the parameterless constructor
        _parameterlessConstructor = typeof(T).GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);
    }

    public T Empty()
    {
        if (_parameterlessConstructor != null)
        {
            return (T) _parameterlessConstructor.Invoke(null);
        }
        if (typeof(T).IsValueType)
        {
            return default(T)!;
        }
        throw new InvalidOperationException(
            $"Type {typeof(T).Name} does not have a parameterless constructor and is not a value type.");
    }

    public T Copy(T from)
    {
        if (from == null)
        {
            throw new ArgumentNullException(nameof(from));
        }

        T newInstance = Empty();
        // can't inject, because then it's going to be circular.
        var merger = _serviceProvider.GetRequiredService<IMerger<T>>();
        return merger.Merge(from, newInstance);
    }

    public T Reset(T? item)
    {
        // TODO: Actually reset.
        return Empty();
    }
}

public interface IBasicOperations<T>
{
    public T? Empty();
    public T Copy(T from);
    public T? Reset(T? item);
}

