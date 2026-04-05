using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Desktop.NodeData.Features.Registry;

public sealed class Registry<T>(IOptions<ThingRegistryOptions<T>> options)
{
    private readonly List<Named<T>> _values = options.Value.Values;
    private readonly IEqualityComparer<T> _comparer = options.Value.Comparer;

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

public static class ThingRegistryHelper
{
    extension(IServiceCollection services)
    {
        public OptionsBuilder<ThingRegistryOptions<T>> AddRegistry<T>(Action<ThingRegistryOptions<T>>? configure = null)
        {
            services.AddSingleton<Registry<T>>();
            var ret = services.AddOptions<ThingRegistryOptions<T>>();
            if (configure != null)
            {
                ret.Configure(configure);
            }

            return ret;
        }

        public void ConfigureRegistry<T>(Action<ThingRegistryOptions<T>> configure)
        {
            services.Configure(configure);
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
