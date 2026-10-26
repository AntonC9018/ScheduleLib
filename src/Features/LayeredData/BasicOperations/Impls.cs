using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredData;

public sealed class ListMerger<T> : IMerger<List<T>>
{
    private readonly IKeyEqualityComparer<T> _equalityComparer;
    private readonly IMerger<T> _merger;
    private readonly IBasicOperations<T> _basicOperations;

    public ListMerger(
        IKeyEqualityComparer<T> equalityComparer,
        IMerger<T> merger,
        IBasicOperations<T> basicOperations)
    {
        _equalityComparer = equalityComparer;
        _merger = merger;
        _basicOperations = basicOperations;
    }

    public List<T> Merge(List<T> from, List<T>? into)
    {
        var containedInFrom = from.ToHashSet(_equalityComparer);
        foreach (var targetItem in into!)
        {
            if (containedInFrom.TryGetValue(equalValue: targetItem, actualValue: out var sourceItem))
            {
                containedInFrom.Remove(sourceItem);
                _merger.Merge(from: sourceItem, into: targetItem);
            }
        }
        foreach (var fromNoMatched in containedInFrom)
        {
            var copy = _basicOperations.Copy(fromNoMatched);
            into.Add(copy);
        }
        return into;
    }
}

// string
public sealed class ImmutableClassBasicOperations<T> : IBasicOperations<T>
{
    public T? Empty() => default;
    public T Copy(T from) => from;
    public T? Reset(T? item) => default;
}

public sealed class ImmutableStructBasicOperations<T> : IBasicOperations<T>
    where T : struct
{
    public T Empty() => new();
    public T Copy(T from) => from;
    public T Reset(T item) => new();
}

public sealed class NullableStructBasicOperations<T> : IBasicOperations<T?>
    where T : struct
{
    public T? Empty() => null;
    public T? Copy(T? from) => from;
    public T? Reset(T? item) => null;
}

public sealed class ListBasicOperations<T> : IBasicOperations<List<T>>
{
    public List<T>? Empty() => new();
    public List<T> Copy(List<T> from) => [.. from];

    public List<T> Reset(List<T>? item)
    {
        item!.Clear();
        return item;
    }
}

public sealed class ReflectionMerger<T> : IMerger<T>
{
    private readonly IBasicOperations<T> _basicOperations;
    private readonly IServiceProvider _serviceProvider;
    private static readonly PropertyInfo[] _writableProperties;

    static ReflectionMerger()
    {
        // Get all writable properties
        _writableProperties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
            {
                if (!p.CanWrite)
                {
                    return false;
                }
                return p.CanRead;
            })
            .ToArray();

        // error if there are public fields
        if (typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Any())
        {
            throw new TypeHasPublicInstanceFieldsException(typeof(T));
        }
    }

    public ReflectionMerger(
        IServiceProvider serviceProvider,
        IBasicOperations<T> basicOperations)
    {
        _serviceProvider = serviceProvider;
        _basicOperations = basicOperations;
    }

    public T Merge(T from, T? to)
    {
        if (to == null)
        {
            to = _basicOperations.Empty();
        }
        if (from == null)
        {
            string paramName;
            if (from == null)
            {
                paramName = nameof(from);
            }
            else
            {
                paramName = nameof(to);
            }

            throw new ArgumentNullException(paramName);
        }
        if (to == null)
        {
            string paramName2;
            if (from == null)
            {
                paramName2 = nameof(from);
            }
            else
            {
                paramName2 = nameof(to);
            }

            throw new ArgumentNullException(paramName2);
        }

        foreach (var property in _writableProperties)
        {
            var sourceValue = property.GetValue(from);
            if (sourceValue is null)
            {
                continue;
            }

            var targetValue = property.GetValue(to);
            var iServiceProvider = _serviceProvider;
            var serviceType = typeof(IMerger<>).MakeGenericType(property.PropertyType);
            var merger = iServiceProvider.GetService(serviceType);
            if (merger != null)
            {
                targetValue = CallMergerHelper.Merge(merger: merger,
                    @from: sourceValue,
                    @into: targetValue);
                property.SetValue(obj: to, value: targetValue);
                continue;
            }

            var iServiceProvider2 = _serviceProvider;
            var serviceType2 = typeof(IBasicOperations<>).MakeGenericType(property.PropertyType);
            var copier = iServiceProvider2.GetService(serviceType2);
            if (copier != null)
            {
                targetValue = CallCopyHelper.Copy(operations: copier, @from: sourceValue);
                property.SetValue(obj: to, value: targetValue);
                continue;
            }

            property.SetValue(obj: to, value: sourceValue);
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
            return default!;
        }
        throw new TypeMissingParameterlessConstructorException(typeof(T));
    }

    public T Copy(T from)
    {
        if (from == null)
        {
            throw new ArgumentNullException(nameof(from));
        }

        var newInstance = Empty();
        // can't inject, because then it's going to be circular.
        var merger = _serviceProvider.GetRequiredService<IMerger<T>>();
        return merger.Merge(@from: from, @into: newInstance);
    }

    public T Reset(T? item)
    {
        // TODO: Actually reset.
        return Empty();
    }
}
