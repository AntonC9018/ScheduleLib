using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Cli;

public sealed class RegistryAuthenticationException(string message) : Exception(message);
public sealed record RegistryDestination(string Account, string Destination);

/// <summary>A session identifies the actual account, independently of the selected teacher alias.</summary>
public interface IRegistrySession : IAsyncDisposable
{
    RegistryDestination Target { get; }
    Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token);
}

/// <summary>Configured runtime access for additional registry operations; ownership remains with the session.</summary>
public interface IConfiguredRegistrySession : IRegistrySession
{
    IServiceProvider Services { get; }
    OnlineRegistryNavigator CreateNavigator(CancellationToken token);
}

public interface IRegistryProvider
{
    Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token);
}

public interface IRegistryAccountLock
{
    Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token);
}

public sealed class RegistryAccountLock : IRegistryAccountLock
{
    public async Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
    {
        // Coordinate both registry commands on the same real account and registry, across project/profile aliases.
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target.Destination.TrimEnd('/') + "\n" + target.Account)));
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLib", "locks", "registry-" + hash + ".lock");
        return await LocalFileLock.Acquire(path, token);
    }
}

public sealed class ConfiguredRegistryProvider : IRegistryProvider
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Successful sessions own settings, service provider, scope and context; the failure path disposes them.")]
    public async Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token)
    {
        var resolved = await CliSettings.Load(settings, cancellationToken: token);
        ServiceProvider? provider = null;
        AsyncServiceScope scope = default;
        var hasScope = false;
        try
        {
            var services = CliRuntime.CreateServices(source, AppConfiguration.ConfigureServices, resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(token);
            scope = provider.CreateAsyncScope();
            hasScope = true;
            ScheduleLib.Scraping.Common.Credentials credentials;
            try
            {
                credentials = scope.ServiceProvider.GetRequiredService<CredentialsResolver<BuiltRegistryConfig>>().Get();
                if (credentials is null || string.IsNullOrWhiteSpace(credentials.Login) || string.IsNullOrWhiteSpace(credentials.Password))
                    throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                throw new RegistryAuthenticationException("Registry credentials are missing; configure the selected teacher's existing credential source.");
            }
            RegistryScrapingContext context;
            try { context = await RegistryScrapingContext.Create(scope.ServiceProvider, credentials, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { throw new RegistryAuthenticationException("Registry authentication failed using configured credentials."); }
            return new Session(resolved, provider, scope, context, new(credentials.Login, RegistryScraping.BaseUrl));
        }
        catch
        {
            if (hasScope) await scope.DisposeAsync();
            if (provider is not null) await provider.DisposeAsync();
            resolved.Dispose();
            throw;
        }
    }

    private sealed class Session(ResolvedSettings settings, ServiceProvider provider, AsyncServiceScope scope,
        RegistryScrapingContext context, RegistryDestination target) : IConfiguredRegistrySession
    {
        public RegistryDestination Target => target;
        public IServiceProvider Services => scope.ServiceProvider;
        public OnlineRegistryNavigator CreateNavigator(CancellationToken token) => context.Navigator(Services, token);
        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) =>
            scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler>()
                .Plan(CreateNavigator(token), token);
        public async ValueTask DisposeAsync()
        {
            context.Dispose();
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            settings.Dispose();
        }
    }
}
