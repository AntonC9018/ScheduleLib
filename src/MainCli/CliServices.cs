using System.Diagnostics.CodeAnalysis;
using CommandDotNet;
using CommandDotNet.Builders;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

/// <summary>The registrations of the CLI's root container.</summary>
public static class CliServices
{
    /// <summary>
    /// Command classes resolved from the invocation scope. Commands not listed here are created
    /// by CommandDotNet and build containers of their own until they move to scope plans (#247).
    /// </summary>
    public static IReadOnlyList<Type> CommandClasses { get; } = [typeof(QueryLessonsCommand), typeof(QueryFreeHoursCommand)];

    public static void Configure(IServiceCollection services)
    {
        AppConfiguration.ConfigureServices(services);
        CliRuntime.AddCliServices(services);
        ResolvedSettings.AddScopeValues(services);
        ScheduleSourceSelection.AddScopeValues(services);
        QueryLessonsWorkflow.Register(services);
        QueryFreeHoursWorkflow.Register(services);
        foreach (var commandClass in CommandClasses)
        {
            services.AddScoped(commandClass);
        }
    }
}

/// <summary>
/// The root container of one CLI run, built the first time a command resolved from it runs,
/// so help, parse errors and commands with containers of their own never build it.
/// <paramref name="overrides"/> replaces registrations after the CLI's own, for tests.
/// </summary>
internal sealed class CliRootServices(Action<IServiceCollection>? overrides) : IAsyncDisposable
{
    private ServiceProvider? _root;
    private bool _ended;

    public IServiceProvider Root
    {
        get
        {
            ObjectDisposedException.ThrowIf(_ended, this);
            return _root ??= Build();
        }
    }

    /// <summary>Ends the root container the first time; later calls do nothing, even after a failure.</summary>
    public ValueTask DisposeAsync()
    {
        if (_ended)
        {
            return ValueTask.CompletedTask;
        }

        _ended = true;
        if (_root is null)
        {
            return ValueTask.CompletedTask;
        }

        return _root.DisposeAsync();
    }

    private ServiceProvider Build()
    {
        var services = new ServiceCollection();
        CliServices.Configure(services);
        overrides?.Invoke(services);
        var root = AppConfiguration.BuildServiceProvider(services);
        AppConfiguration.ConfigureLayeredConfig(root);
        return root;
    }
}

/// <summary>
/// Resolves command classes from the invocation scope: one <see cref="InvocationScope"/> of the
/// root container, opened before CommandDotNet resolves the command classes and ended after the
/// command runs. Outside it nothing resolves, so CommandDotNet creates the other classes and
/// argument models itself.
/// </summary>
internal sealed class InvocationScopeResolver(CliRootServices services) : IDependencyResolver
{
    private InvocationScope? _scope;

    public void Register(AppRunner runner)
    {
        runner.UseDependencyResolver(this, commandClassResolveStrategy: ResolveStrategy.TryResolve);
        runner.Configure(builder => builder.UseMiddleware(RunInScope, MiddlewareSteps.ResolveCommandClasses - 1));
    }

    public object? Resolve(Type type) => _scope?.Services.GetService(type);

    public bool TryResolve(Type type, [NotNullWhen(true)] out object? item)
    {
        item = Resolve(type);
        return item is not null;
    }

    private async Task<int> RunInScope(CommandContext context, ExecutionDelegate next)
    {
        if (!context.InvocationPipeline.All.Any(IsResolvedFromScope))
        {
            return await next(context);
        }

        await using var scope = await InvocationScope.OpenAsync(services.Root);
        _scope = scope;
        try
        {
            return await next(context);
        }
        finally
        {
            _scope = null;
        }
    }

    private static bool IsResolvedFromScope(InvocationStep step)
    {
        var commandClass = step.Invocation.MethodInfo.DeclaringType;
        return commandClass is not null && CliServices.CommandClasses.Contains(commandClass);
    }
}
