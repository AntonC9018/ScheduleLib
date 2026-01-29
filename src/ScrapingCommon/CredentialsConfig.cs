using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Scraping.Common.Config;

public interface ICredentialsConfig
{
    public CredentialsSource? Credentials { get; set; }
}

public sealed class CredentialsSource
{
    public bool? IsRequired { get; set; }
}

public readonly struct CredentialsSourceBuilder
{
    internal readonly CredentialsSource _source;

    public CredentialsSourceBuilder(CredentialsSource source)
    {
        _source = source;
    }
}

public static class CredentialsBuilderExtensions
{
    extension<T> (ConfigBuilder<T> builder)
        where T : class, ICredentialsConfig, IConfig<T>, new()
    {
        public CredentialsSourceBuilder Credentials()
        {
            var t = new CredentialsSource();
            builder.Value().Credentials = t;
            return new(t);
        }
    }

    extension (CredentialsSourceBuilder builder)
    {
        public void FromConfig(bool isRequired = false)
        {
            builder._source.IsRequired = isRequired;
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
                    provider: sp.GetRequiredService<ConfigProvider<T>>(),
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
        // TODO: Support other types of sources.
        var credentials = _resolver.Resolve(serviceKey);
        if (credentials != null)
        {
            return credentials;
        }
        if (source.IsRequired == false)
        {
            throw new InvalidOperationException("Credentials must be given as per configuration");
        }
        return null;
    }
}

[AutoConstructor]
public sealed partial class CredentialsResolver<T>
    where T : class
{
    private readonly string _serviceKey;
    private readonly ICredentialsResolver _generalResolver;
    private readonly ConfigProvider<T> _provider;
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
