using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ScheduleLib.Application.Config;

public sealed class GlobalConfigurationOptions
{
    public Action<IConfigurationBuilder> AddSources { get; set; } = AddUserSecrets;

    private static void AddUserSecrets(IConfigurationBuilder builder) => builder.AddUserSecrets(Assembly.GetEntryAssembly()!);
}

/// <summary>
/// The global configuration, read from its sources the first time a service reads it, so building
/// a container never touches the user-secrets file and a failure to read it reaches only its readers.
/// </summary>
public sealed class GlobalConfiguration(IOptions<GlobalConfigurationOptions> options) : IConfiguration, IDisposable
{
    private readonly Lazy<IConfigurationRoot> _root = new(() => Build(options.Value));

    public string? this[string key]
    {
        get => _root.Value[key];
        set => _root.Value[key] = value;
    }

    public IConfigurationSection GetSection(string key) => _root.Value.GetSection(key);

    public IEnumerable<IConfigurationSection> GetChildren() => _root.Value.GetChildren();

    public IChangeToken GetReloadToken() => _root.Value.GetReloadToken();

    public void Dispose()
    {
        if (!_root.IsValueCreated)
        {
            return;
        }

        (_root.Value as IDisposable)?.Dispose();
    }

    private static IConfigurationRoot Build(GlobalConfigurationOptions options)
    {
        var builder = new ConfigurationBuilder();
        options.AddSources(builder);
        return builder.Build();
    }
}
