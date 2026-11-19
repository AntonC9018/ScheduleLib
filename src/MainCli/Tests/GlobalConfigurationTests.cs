using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Cli;

/// <summary>The global configuration (user secrets) is read only by the services that need it.</summary>
public sealed class GlobalConfigurationTests
{
    private const string FailureMessage = "Access to the user-secrets file is denied.";

    [Fact]
    public async Task UnreadableSecretsDoNotPreemptQueryValidation()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var missingProject = Path.Combine(temp.Path, "missing");
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await CliTestHost.Run(
                ["query", "lessons", "--project", missingProject, "--data-dir", " ", "--json"],
                UnreadableSecrets);
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: CliExit.InvalidConfiguration, actual: exit);
            var error = Assert.Single(result.RootElement.GetProperty("errors")
                .EnumerateArray());
            Assert.Equal(
                expected: $"Project directory does not exist: {missingProject}",
                actual: error.GetString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void ReadingTheConfigurationReportsTheFailure()
    {
        var services = new ServiceCollection();
        services.AddGlobalConfiguration();
        UnreadableSecrets(services);
        using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredService<IConfiguration>();

        var error = Assert.Throws<UnauthorizedAccessException>(() => configuration["Google"]);
        Assert.Equal(expected: FailureMessage, actual: error.Message);
    }

    private static void UnreadableSecrets(IServiceCollection services)
    {
        static void AddUnreadableSource(IConfigurationBuilder builder)
        {
            var source = new UnreadableSource();
            builder.Add(source);
        }

        static void ReadUnreadableSource(GlobalConfigurationOptions options) => options.AddSources = AddUnreadableSource;

        services.Configure<GlobalConfigurationOptions>(ReadUnreadableSource);
    }

    private sealed class UnreadableSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new UnreadableProvider();
    }

    private sealed class UnreadableProvider : ConfigurationProvider
    {
        public override void Load() => throw new UnauthorizedAccessException(FailureMessage);
    }
}
