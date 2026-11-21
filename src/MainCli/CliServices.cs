using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using CommandDotNet;
using CommandDotNet.Builders;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

public static class CliServices
{
    /// <summary>
    /// Command classes resolved from the invocation scope. Commands not listed here are created
    /// by CommandDotNet and build containers of their own until they move to scope plans (#247).
    /// </summary>
    public static IReadOnlyList<Type> CommandClasses { get; } = [typeof(QueryLessonsCommand), typeof(QueryFreeHoursCommand)];

    public static void Configure(IServiceCollection services, CliRun run)
    {
        AppConfiguration.ConfigureServices(services);
        services.AddSingleton(run);
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
/// Built the first time a command resolved from it runs, so help, parse errors and commands with
/// containers of their own never build it.
/// </summary>
internal sealed class CliRootServices(CliRun run, Action<IServiceCollection>? overrides) : IAsyncDisposable
{
    private ServiceProvider? _root;
    private ExceptionDispatchInfo? _setupFailure;
    private bool _ended;

    /// <summary>
    /// The root container, built and its settings tree populated on first use. The container is
    /// owned from the moment it is built, so a failed setup still ends it; later uses rethrow.
    /// </summary>
    public IServiceProvider Root
    {
        get
        {
            ObjectDisposedException.ThrowIf(_ended, this);
            _setupFailure?.Throw();
            if (_root is not null)
            {
                return _root;
            }

            try
            {
                _root = Build();
                AppConfiguration.ConfigureLayeredConfig(_root);
            }
            catch (Exception failure)
            {
                _setupFailure = ExceptionDispatchInfo.Capture(failure);
                throw;
            }
            return _root;
        }
    }

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
        CliServices.Configure(services, run);
        overrides?.Invoke(services);
        return AppConfiguration.BuildServiceProvider(services);
    }
}

/// <summary>
/// Outside the scope nothing resolves, so CommandDotNet creates the other command classes and
/// argument models itself.
/// </summary>
internal sealed class InvocationScopeResolver(CliRootServices services, CliRun run) : IDependencyResolver
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

        // The scope ends once the command has reported its result; the using only ends it when the
        // command throws instead.
        await using var scope = await InvocationScope.OpenAsync(services.Root);
        _scope = scope;
        int exit;
        try
        {
            exit = await next(context);
        }
        finally
        {
            _scope = null;
        }

        var request = StartupRequest.FromParsedInput(context.ParseResult);
        return await CliTeardown.EndAfterResult(
            scope,
            description: "the command's services",
            exit,
            run.ResultOutput(request));
    }

    private static bool IsResolvedFromScope(InvocationStep step)
    {
        var commandClass = step.Invocation.MethodInfo.DeclaringType;
        return commandClass is not null && CliServices.CommandClasses.Contains(commandClass);
    }
}
