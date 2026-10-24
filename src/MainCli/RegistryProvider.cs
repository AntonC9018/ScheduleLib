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

    IReadOnlyList<RegistrySyncDiagnostic> Diagnostics => [];
    RegistrySyncDiagnostics? LiveDiagnostics => null;
    Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token);

    Task PlanIntakeLabCleanup(
        Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
        CancellationToken token)
        => throw new NotSupportedException("Intake lab cleanup is not supported by this registry session.");

    async Task PlanGroups(Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned, CancellationToken token)
    {
        var planned = await Plan(token);
        await onGroupPlanned(planned);
    }
}

/// <summary>Configured runtime access for additional registry operations; ownership remains with the session.</summary>
public interface IConfiguredRegistrySession : IRegistrySession
{
    IServiceProvider Services { get; }

    OnlineRegistryNavigator CreateNavigator(CancellationToken token);
}

public interface IRegistryProvider
{
    Task<IRegistrySession> Open(
        SourceArguments source,
        SettingsArguments settings,
        CancellationToken token);
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
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes((string) $"{target.Destination.TrimEnd('/')}\n{target.Account}")));
        string path1 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string path2 = "ScheduleLib";
        string path3 = "locks";
        string text = $"registry-{hash}.lock";
        string path4 = (string) text;
        var path = Path.Combine(
            path1, path2, path3, path4);
        return await LocalFileLock.Acquire(path, token);
    }
}

public sealed class ConfiguredRegistryProvider : IRegistryProvider
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        category: "Reliability",
        checkId: "CA2000:Dispose objects before losing scope",
        Justification =
            "Successful sessions own settings, service provider, scope and context; the failure path disposes them.")]
    public async Task<IRegistrySession> Open(
        SourceArguments source,
        SettingsArguments settings,
        CancellationToken token)
    {
        var resolved = await CliSettings.Load(settings, cancellationToken: token);
        ServiceProvider? provider = null;
        AsyncServiceScope scope = default;
        var hasScope = false;
        try
        {
            var services = CliRuntime.CreateServices(
                source,
                AppConfiguration.ConfigureServices,
                resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            if (CliAmbient.Current is { Jsonl: true } ambient)
            {
                CliLogging.ReplaceWithJsonl(services, ambient);
            }

            provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(token);
            scope = provider.CreateAsyncScope();
            hasScope = true;
            ScheduleLib.Scraping.Common.Credentials credentials;
            try
            {
                credentials = scope.ServiceProvider.GetRequiredService<CredentialsResolver<BuiltRegistryConfig>>()
                    .Get();
                bool CheckCondition()
                {
                    if (credentials is null)
                    {
                        return true;
                    }

                    if (string.IsNullOrWhiteSpace(credentials.Login))
                    {
                        return true;
                    }

                    if (string.IsNullOrWhiteSpace(credentials.Password))
                    {
                        return true;
                    }

                    return false;
                }

                if (CheckCondition())
                {
                    throw new InvalidOperationException();
                }
            }
            catch (InvalidOperationException)
            {
                throw new RegistryAuthenticationException(
                    "Registry credentials are missing; configure the selected teacher's existing credential source.");
            }
            RegistryScrapingContext context;
            try
            {
                context = await RegistryScrapingContext.Create(
                    scope.ServiceProvider,
                    credentials,
                    token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new RegistryAuthenticationException(
                    "Registry authentication failed using configured credentials.");
            }
            global::ScheduleLib.Cli.ResolvedSettings settings2 = resolved;
            global::Microsoft.Extensions.DependencyInjection.ServiceProvider provider2 = provider;
            global::Microsoft.Extensions.DependencyInjection.AsyncServiceScope scope2 = scope;
            global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = context;
            global::ScheduleLib.Cli.RegistryDestination target = new(Account: credentials.Login, Destination: RegistryScraping.BaseUrl);
            global::ScheduleLib.Cli.ConfiguredRegistryProvider.Session session = new Session(
            settings2, provider2, scope2, context2, target);
            return session;
        }
        catch
        {
            if (hasScope)
            {
                await scope.DisposeAsync();
            }

            if (provider is not null)
            {
                await provider.DisposeAsync();
            }

            resolved.Dispose();
            throw;
        }
    }

    private sealed class Session(
        ResolvedSettings settings,
        ServiceProvider provider,
        AsyncServiceScope scope,
        RegistryScrapingContext context,
        RegistryDestination target) : IConfiguredRegistrySession
    {
        public RegistryDestination Target => target;

        public IReadOnlyList<RegistrySyncDiagnostic> Diagnostics =>
            Services.GetRequiredService<RegistrySyncDiagnostics>().Items;

        public RegistrySyncDiagnostics LiveDiagnostics => Services.GetRequiredService<RegistrySyncDiagnostics>();
        public IServiceProvider Services => scope.ServiceProvider;
        public OnlineRegistryNavigator CreateNavigator(CancellationToken token) => context.Navigator(Services, token);

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token)
        {
            global::ScheduleLib.Application.Core.AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler requiredService = scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler>();
            global::ScheduleLib.OnlineRegistry.OnlineRegistryNavigator navigator = CreateNavigator(token);
            global::ScheduleLib.OnlineRegistry.IRegistrySyncNavigator navigator2 = navigator;
            global::System.Threading.CancellationToken cancellationToken = token;
            global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.OnlineRegistry.RegistrySyncAction>> plan = requiredService.Plan(navigator2, cancellationToken);
            return plan;
        }

        public Task PlanGroups(
            Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
            CancellationToken token)
        {
            global::ScheduleLib.Application.Core.AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler requiredService = scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler>();
            global::ScheduleLib.OnlineRegistry.OnlineRegistryNavigator navigator = CreateNavigator(token);
            global::ScheduleLib.OnlineRegistry.IRegistrySyncNavigator navigator2 = navigator;
            global::System.Func<global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.OnlineRegistry.RegistrySyncAction>, global::System.Threading.Tasks.Task<bool>> onGroupPlanned2 = onGroupPlanned;
            global::System.Threading.CancellationToken cancellationToken = token;
            global::System.Threading.Tasks.Task planGroups = requiredService.PlanGroups(
            navigator2, onGroupPlanned2, cancellationToken);
            return planGroups;
        }

        public Task PlanIntakeLabCleanup(
            Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
            CancellationToken token)
        {
            global::ScheduleLib.Application.Core.AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler requiredService = Services.GetRequiredService<AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler>();
            global::ScheduleLib.OnlineRegistry.OnlineRegistryNavigator navigator = CreateNavigator(token);
            global::ScheduleLib.OnlineRegistry.IRegistrySyncNavigator navigator2 = navigator;
            global::System.Func<global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.OnlineRegistry.RegistrySyncAction>, global::System.Threading.Tasks.Task<bool>> onGroupPlanned2 = onGroupPlanned;
            global::System.Threading.CancellationToken cancellationToken = token;
            global::System.Threading.Tasks.Task planIntakeLabCleanup = requiredService.PlanIntakeLabCleanup(
            navigator2, onGroupPlanned2, cancellationToken);
            return planIntakeLabCleanup;
        }

        public async ValueTask DisposeAsync()
        {
            context.Dispose();
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            settings.Dispose();
        }
    }
}
