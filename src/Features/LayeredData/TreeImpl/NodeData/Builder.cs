using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredData;

public readonly struct NodeDataBuilder<T>
    where T : class
{
    private readonly NodeBuilder _node;
    public NodeDataKey<T> DataKey { get; }
    public MutableNode Node => _node.Node;
    public IServiceProvider SingletonServiceProvider => _node.SingletonServiceProvider;
    public bool IsNull => _node.IsNull;
    public bool IsReadOnly { get; }

    public NodeDataBuilder(
        NodeBuilder node,
        NodeDataKey<T> dataKey,
        bool isReadOnly)
    {
        _node = node;
        DataKey = dataKey;
        IsReadOnly = isReadOnly;
    }
}

public static class BaseExtensions
{
    extension (NodeBuilder builder)
    {
        public NodeDataBuilder<T> Builder<T>()
            where T : class, INodeData<T>
        {
            return builder.Builder(T.Key);
        }

        public NodeDataBuilder<T> Builder<T>(NodeDataKey<T> key)
            where T : class
        {
            return new(builder, key, isReadOnly: false);
        }

        public NodeDataBuilder<T> ReadOnlyBuilder<T>(NodeDataKey<T> key)
            where T : class
        {
            return new(builder, key, isReadOnly: true);
        }

        public void ClearData()
        {
            builder.Node._configs.Clear();
        }
    }

    extension<T> (NodeDataBuilder<T> builder)
        where T : class
    {
        public void MutableGuard()
        {
            // if (builder.IsReadOnly)
            // {
            //     throw new MutatingOperationCalledOnReadOnlyBuilder();
            // }
        }

        public T ConfigureValue(Action<T> configure)
        {
            builder.MutableGuard();
            var val = builder.Value();
            configure(val);
            return val;
        }

        public T CopyValue(T value)
        {
            builder.MutableGuard();
            var x = builder.Enable();
            value = builder.SingletonServiceProvider.GetRequiredService<IBasicOperations<T>>().Copy(value);
            x.SetValue(value);
            return value;
        }

        public T Value()
        {
            builder.MutableGuard();
            var x = builder.Enable();
            if (x.GetValue() is not { } val)
            {
                val = builder.SingletonServiceProvider.GetRequiredService<IBasicOperations<T>>().Empty();
                if (val is null)
                {
                    throw new InvalidOperationException("Root BasicOperations is supposed to create actual objects, not null.");
                }
                x.SetValue(val);
            }
            return val;
        }

        public T? TryGetValue()
        {
            var container = builder.Node.Get(builder.DataKey);
            if (!container.Exists)
            {
                return null;
            }
            if (container.Value.GetValue() is { } val)
            {
                return val;
            }
            return null;
        }

        public NodeDataContainer<T> Enable()
        {
            builder.MutableGuard();
            var ret = builder.Node.GetOrAdd(builder.DataKey);
            return ret;
        }

        public void Configure(Action<NodeDataBuilder<T>> configure)
        {
            builder.MutableGuard();
            configure(builder);
        }
    }
}

// public sealed class MutatingOperationCalledOnReadOnlyBuilder : Exception
// {
//     public MutatingOperationCalledOnReadOnlyBuilder() : base("Mutating operation called on read only builder")
//     {
//     }
// }
