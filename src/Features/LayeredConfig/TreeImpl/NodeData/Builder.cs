using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredData;

public readonly struct NodeDataBuilder<T>
    where T : class
{
    private readonly NodeBuilder _node;
    public NodeDataKey<T> DataKey { get; }
    internal MutableNode Node => _node.Node;
    public IServiceProvider SingletonServiceProvider => _node.SingletonServiceProvider;

    public NodeDataBuilder(
        NodeBuilder node,
        NodeDataKey<T> dataKey)
    {
        _node = node;
        DataKey = dataKey;
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
            return new(builder, key);
        }

        public void ClearData()
        {
            builder.Node._configs.Clear();
        }
    }

    extension<T> (NodeDataBuilder<T> builder)
        where T : class
    {
        public T ConfigureValue(Action<T> configure)
        {
            var val = builder.Value();
            configure(val);
            return val;
        }
        public T CopyValue(T value)
        {
            var x = builder.Enable();
            value = builder.SingletonServiceProvider.GetRequiredService<IBasicOperations<T>>().Copy(value);
            x.SetValue(value);
            return value;
        }
        public T Value()
        {
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

        public NodeDataContainer<T> Enable()
        {
            return builder.Node.GetOrAdd(builder.DataKey);
        }
        public void Configure(Action<NodeDataBuilder<T>> configure)
        {
            configure(builder);
        }
    }
}
