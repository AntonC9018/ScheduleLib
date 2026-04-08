using Anton.LayeredData;
using Desktop.NodeData.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Desktop.NodeData.Features.Registry;


public sealed class Registry<T>
{
    private readonly List<Named<T>> _values;
    private readonly IEqualityComparer<T> _comparer;

    public Registry(
        IOptionsMonitor<ThingRegistryOptions<T>> monitor,
        string? optionKey)
    {
        var options = monitor.Get(optionKey);
        _values = options.Values;
        _comparer = options.Comparer;
    }

    public Named<T> Find(T value)
    {
        var x = _values.FirstOrDefault(x => _comparer.Equals(x.Value, value));
        if (x is null)
        {
            throw new NotImplementedException("Implement the proper registry system!");
        }

        return x;
    }

    public IReadOnlyList<Named<T>> Values => _values;
}

public sealed class ThingRegistryOptions<T>
{
    public readonly List<Named<T>> Values = new();
    public IEqualityComparer<T> Comparer = EqualityComparer<T>.Default;
}

public sealed record class Named<T>(
    string Name,
    T Value)
{
    public static readonly Named<T> Default = new("Default", default!);
    public override string ToString() => Name;
}

public readonly record struct RegistryKey(
    NodeDataKey NodeData,
    PropertyId Property);
public readonly record struct RegistryKey<T>(RegistryKey Value)
{
    public override string ToString() => Value.ToString();
}

public static class ThingRegistryHelper
{
    extension(IServiceCollection services)
    {
        public OptionsBuilder<ThingRegistryOptions<T>> AddRegistry<T>(
            RegistryKey<T> key,
            Action<ThingRegistryOptions<T>>? configure = null)
        {
            var keyStr = key == default ? null : key.ToString();
            services.AddKeyedSingleton<Registry<T>>(keyStr, static (sp, keyStr) =>
            {
                var opts = sp.GetRequiredService<IOptionsMonitor<ThingRegistryOptions<T>>>();
                return new(opts, (string?) keyStr);
            });

            var ret = services.AddOptions<ThingRegistryOptions<T>>(keyStr);
            if (configure != null)
            {
                ret.Configure(configure);
            }
            return ret;
        }

        public OptionsBuilder<ThingRegistryOptions<T>> AddRegistry<T>(
            Action<ThingRegistryOptions<T>>? configure = null)
        {
            services.AddSingleton<Registry<T>>();
            var ret = services.AddOptions<ThingRegistryOptions<T>>();
            if (configure != null)
            {
                ret.Configure(configure);
            }

            return ret;
        }

        public void ConfigureRegistry<T>(
            Action<ThingRegistryOptions<T>> configure)
        {
            services.ConfigureRegistry(default, configure);
        }

        public void ConfigureRegistry<T>(
            RegistryKey<T> key,
            Action<ThingRegistryOptions<T>> configure)
        {
            services.Configure(key.ToString(), configure);
        }
    }


    extension<T>(ThingRegistryOptions<T> opts)
    {
        public void Add(string name, T value)
        {
            opts.Values.Add(new(name, value));
        }

        public void UseTypeComparer()
        {
            opts.Comparer = new TypeComparer<T>();
        }
    }
}
