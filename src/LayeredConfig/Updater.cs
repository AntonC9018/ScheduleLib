using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig;

public interface IUpdaterBase
{
}

public interface IUpdater<T> : IUpdaterBase
    where T : class
{
    public T? Update(IServiceProvider scopedServiceProvider, T value);
}

public sealed class MergeValueUpdater<T> : IUpdater<T>
    where T : class
{
    public T Value { get; set; }

    public MergeValueUpdater(T initialValue)
    {
        Value = initialValue;
    }

    public T? Update(IServiceProvider scopedServiceProvider, T value)
    {
        var merger = scopedServiceProvider.GetRequiredService<IMerger<T>>();
        var ret = merger.Merge(from: Value, into: value);
        return ret;
    }
}

public sealed class DelegateUpdater<T> : IUpdater<T>
    where T : class
{
    private readonly Func<IServiceProvider, T, T?> _action;

    public DelegateUpdater(Func<IServiceProvider, T, T?> action)
    {
        _action = action;
    }

    public T? Update(IServiceProvider scopedServiceProvider, T value) => _action(scopedServiceProvider, value);
}

public sealed class RemoveValueUpdater<T> : IUpdater<T>
    where T : class
{
    public static readonly RemoveValueUpdater<T> Instance = new();
    public T? Update(IServiceProvider scopedServiceProvider, T value) => null;
}

public sealed class ResetValueUpdater<T> : IUpdater<T>
    where T : class
{
    public static readonly ResetValueUpdater<T> Instance = new();
    public T? Update(IServiceProvider scopedServiceProvider, T value)
    {
        var basicOps = scopedServiceProvider.GetRequiredService<IBasicOperations<T>>();
        var ret = basicOps.Reset(value);
        return ret;
    }
}

public static class BuilderUpdaterExtensions
{
    extension<T> (ConfigBuilder<T> builder)
        where T : class
    {
        // TODO:
        // Add validation that would deal with using stuff along this thing.
        // They just won't run ever is the problem, so it's probably not desired.
        public void Remove()
        {
            builder.AddUpdate(RemoveValueUpdater<T>.Instance);
        }
        public void AddUpdate(IUpdater<T> updater)
        {
            builder.Enable().UpdateActions.Add(updater);
        }
        public void AddUpdate(Func<IServiceProvider, T, T?> updateAction)
        {
            var update = new DelegateUpdater<T>(updateAction);
            builder.AddUpdate(update);
        }
        public void AddUpdate(Func<T, T?> updateAction)
        {
            builder.AddUpdate((sp, x) =>
            {
                _ = sp;
                return updateAction(x);
            });
        }
        public void AddUpdate(Action<T> updateAction)
        {
            builder.AddUpdate((sp, x) =>
            {
                _ = sp;
                updateAction(x);
                return x;
            });
        }
    }
}
