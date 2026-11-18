namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Builds a derived value synchronously. The constructor lists the inputs,
/// so the scope plan can see them.
/// </summary>
public interface IFactory<out T>
{
    T Create();
}

/// <summary>Untyped view of <see cref="IInitializer{T}"/>, so the opener needs no reflection.</summary>
public interface IInitializer
{
    ValueTask<object> InitializeUntypedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Builds an initialized value asynchronously when a scope opens, before its handler is resolved.
/// The constructor lists the inputs, so the scope plan can see them.
/// </summary>
public interface IInitializer<T> : IInitializer
    where T : class
{
    ValueTask<T> InitializeAsync(CancellationToken cancellationToken);

    async ValueTask<object> IInitializer.InitializeUntypedAsync(CancellationToken cancellationToken)
    {
        return await InitializeAsync(cancellationToken);
    }
}
