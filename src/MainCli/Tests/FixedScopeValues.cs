using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>A value a test fixes for every scope, replacing how the services would build it.</summary>
public sealed class Fixed<T>(T value)
    where T : class
{
    public T Value => value;
}

public sealed class FixedFactory<T>(Fixed<T> value) : IFactory<T>
    where T : class
{
    public T Create() => value.Value;
}

public static class FixedScopeValues
{
    /// <summary>Removes the seed, derived or initialized registration of <typeparamref name="T"/> and fixes it to <paramref name="value"/>.</summary>
    public static IServiceCollection AddFixed<T>(this IServiceCollection services, T value)
        where T : class
    {
        services.AddSingleton(new Fixed<T>(value));
        services.RemoveScopeValue<T>();
        return services.AddDerived<T, FixedFactory<T>>();
    }
}
