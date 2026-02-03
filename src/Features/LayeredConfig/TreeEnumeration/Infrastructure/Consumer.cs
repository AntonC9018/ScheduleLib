using System.Collections;
using AutoConstructor.Attributes;

namespace Anton.LayeredConfig.TreeEnumeration.Infrastructure;

[AutoConstructor]
public readonly partial struct ContextWrappedLayerEnumerable : IEnumerable<LayerStateEnumerator.Value>
{
    // Should make all of these generic. It just gets so confusing.
    private readonly Func<ILayerStateEnumerationContext> _consumerFactory;
    private readonly IEnumerable<LayerStateEnumerator.Value> _innerE;

    public ContextWrappedLayerEnumerator<IEnumerator<LayerStateEnumerator.Value>, ILayerStateEnumerationContext> GetEnumerator()
    {
        var context = _consumerFactory();
        var e = _innerE.GetEnumerator();
        return new(e, context);
    }

    IEnumerator<LayerStateEnumerator.Value> IEnumerable<LayerStateEnumerator.Value>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public struct ContextWrappedLayerEnumerator<TEnumerator, TContext> : IEnumerator<LayerStateEnumerator.Value>
    where TContext : ILayerStateEnumerationContext
    where TEnumerator : IEnumerator<LayerStateEnumerator.Value>
{
    // Making this a struct is way too complicated, impossible to maintain.
    private readonly TContext _consumer;
    public TEnumerator InnerE;

    public ContextWrappedLayerEnumerator(
        TEnumerator innerE,
        TContext context)
    {
        _consumer = context;
        InnerE = innerE;
    }

    public bool MoveNext()
    {
        if (InnerE.MoveNext())
        {
            _consumer.Consume(Current);
            return true;
        }
        return false;
    }

    public void Reset() => InnerE.MoveNext();
    public LayerStateEnumerator.Value Current => InnerE.Current;
    object? IEnumerator.Current => InnerE.Current;
    public void Dispose() => InnerE.Dispose();
}

public interface ILayerStateEnumerationContext
{
    void Consume(LayerStateEnumerator.Value value);
}

public readonly struct ActionContext(Action<LayerStateEnumerator.Value> action) : ILayerStateEnumerationContext
{
    public void Consume(LayerStateEnumerator.Value value) => action(value);
}

public static class LayerStateEnumeratorBuilderExtensions
{
    extension<T>(T e) where T : IEnumerator<LayerStateEnumerator.Value>
    {
        public ContextWrappedLayerEnumerator<T, TContext> WithContext<TContext>(TContext context)
            where TContext : ILayerStateEnumerationContext
        {
            return new(e, context);
        }
        public ContextWrappedLayerEnumerator<T, ActionContext> WithContext(Action<LayerStateEnumerator.Value> action)
        {
            return new(e, new(action));
        }
    }

    public static ILayerStateEnumerator AsBoxed(this ILayerStateEnumerator e) => e;
}

public static class LayerStateEnumerableBuilderExtensions
{
    // extension<T>(T e) where T : IEnumerable<LayerStateEnumerator.Value>
    extension<T>(T e) where T : IEnumerable<LayerStateEnumerator.Value>
    {
        public ContextWrappedLayerEnumerable WithContext(Func<ILayerStateEnumerationContext> context)
        {
            return new(context, e);
        }
        public ContextWrappedLayerEnumerable WithContext(Func<Action<LayerStateEnumerator.Value>> action)
        {
            return new(() => new ActionContext(action()), e);
        }
    }
}

