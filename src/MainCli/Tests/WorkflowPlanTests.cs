using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Cli;
using ScheduleLib.DependencyInjection.ScopePlans;

public sealed class WorkflowPlanTests
{
    public static TheoryData<Type> WorkflowRoots()
    {
        var roots = CliServices.CommandClasses
            .SelectMany(x => x.GetConstructors())
            .SelectMany(x => x.GetParameters())
            .Select(x => x.ParameterType)
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ScopeForkFactory<>))
            .Select(x => x.GetGenericArguments()[0])
            .Distinct();
        return new(roots);
    }

    [Theory]
    [MemberData(nameof(WorkflowRoots))]
    public async Task WorkflowRootPlanHasNoErrors(Type root)
    {
        var services = new ServiceCollection();
        using var run = new CliRun();
        CliServices.Configure(services, run);
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        var plan = provider.GetRequiredService<ScopePlanGraph>()
            .GetPlan(root);
        Assert.Empty(plan.Errors);
    }
}
