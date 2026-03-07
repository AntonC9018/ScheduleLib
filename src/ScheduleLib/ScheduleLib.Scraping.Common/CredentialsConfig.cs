using System.Diagnostics.CodeAnalysis;
using Anton.LayeredData;
using Anton.LayeredData.Options;
using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Scraping.Common.Config;

public interface ICredentialsHolder
{
    public CredentialsSource? Credentials { get; set; }
}

public sealed class AppConfigCredentialsSource : CredentialsSource
{
}

public sealed class ValueCredentialsSource : CredentialsSource
{
    public Credentials? Value { get; set; }
}

public abstract class CredentialsSource
{
    public static void Register(IServiceCollection services)
    {
        var hierarchy = services.AddOpenHierarchy<CredentialsSource>();
        hierarchy.AddDerived<ValueCredentialsSource>().SetImmutable();
        hierarchy.AddDerived<AppConfigCredentialsSource>().SetImmutable();
    }
}

public readonly struct CredentialsSourceBuilder
{
    public readonly ICredentialsHolder _storage;

    public CredentialsSourceBuilder(ICredentialsHolder storage)
    {
        _storage = storage;
    }
}

public static class CredentialsBuilderExtensions
{
    extension<T> (NodeDataBuilder<T> builder)
        where T : class, ICredentialsHolder
    {
        public CredentialsSourceBuilder Credentials()
        {
            var t = new CredentialsSourceBuilder(builder.Value());
            return t;
        }
    }

    extension (CredentialsSourceBuilder builder)
    {
        public void FromConfig()
        {
            builder._storage.Credentials = new AppConfigCredentialsSource();
        }

        public ValueCredentialsSource Value(
            Action<ValueCredentialsSource>? configure = null)
        {
            var ret = builder.Value(overwriteIfAnother: true)!;
            configure?.Invoke(ret);
            return ret;
        }

        public ValueCredentialsSource? Value(bool overwriteIfAnother)
        {
            var s = builder._storage;
            if (s.Credentials is not ValueCredentialsSource ret)
            {
                if (!overwriteIfAnother)
                {
                    return null;
                }
                ret = new ValueCredentialsSource();
                s.Credentials = ret;
            }
            return ret;
        }
    }

    extension (IServiceCollection services)
    {
        public void AddCredentialsResolver<T>(
            string serviceKey,
            Func<T, CredentialsSource> getter)

            where T : class
        {
            services.AddScoped<CredentialsResolver<T>>(sp =>
            {
                return new(
                    serviceKey: serviceKey,
                    generalResolver: sp.GetRequiredService<ICredentialsResolver>(),
                    provider: sp.GetRequiredService<DataProvider<T>>(),
                    sourceGetter: getter);
            });
        }
    }
}

public interface ICredentialsResolver
{
    public Credentials? Resolve(
        string serviceKey,
        CredentialsSource source);
}

[AutoConstructor]
public sealed partial class CredentialsResolver : ICredentialsResolver
{
    private readonly MarkedDynamicOptionsResolver<Credentials> _resolver;

    public Credentials? Resolve(
        string serviceKey,
        CredentialsSource source)
    {
        switch (source)
        {
            case AppConfigCredentialsSource:
            {
                // TODO: Support other types of sources.
                var credentials = _resolver.Resolve(serviceKey);
                if (credentials != null)
                {
                    return credentials;
                }
                {
                    throw new InvalidOperationException("Credentials must be given as per configuration");
                }
            }
            case ValueCredentialsSource v:
            {
                return v.Value;
            }
            default:
            {
                throw Unreachable();
            }
        }
    }
}

[AutoConstructor]
public sealed partial class CredentialsResolver<T>
    where T : class
{
    private readonly string _serviceKey;
    private readonly ICredentialsResolver _generalResolver;
    private readonly DataProvider<T> _provider;
    private readonly Func<T, CredentialsSource> _sourceGetter;

    public Credentials Get()
    {
        var config = _provider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("No credential config found");
        }
        var source = _sourceGetter(config);
        var ret = _generalResolver.Resolve(_serviceKey, source);
        return ret!;
    }
}
