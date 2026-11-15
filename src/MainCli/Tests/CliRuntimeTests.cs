using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;

public sealed class CliRuntimeTests
{
    [Fact]
    public void CreateServicesCapturesRelativeCacheDirectoryAtEntry()
    {
        using var temp = new TestTempDirectory("cliruntime-cache-tests");
        var entryPath = Path.Combine(temp.Path, "entry");
        var entryDirectory = Directory.CreateDirectory(entryPath).FullName;
        var dataPath = Path.Combine(entryDirectory, "data");
        var dataDirectory = Directory.CreateDirectory(dataPath).FullName;
        var laterPath = Path.Combine(temp.Path, "later");
        var laterDirectory = Directory.CreateDirectory(laterPath).FullName;
        var expectedCacheDirectory = Path.Combine(entryDirectory, "cache");
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = entryDirectory;
            var services = CliRuntime.CreateServices(
                new SourceArguments
                {
                    DataDirectory = "data",
                    CacheDirectory = "cache",
                },
                _ =>
                {
                    Environment.CurrentDirectory = laterDirectory;
                });

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<ScheduleBuilderInitializerOptions>>().Value;

            Assert.Equal(expected: dataDirectory, actual: options.DataDirectory);
            Assert.Equal(expected: expectedCacheDirectory, actual: options.CacheDirectory);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }
}
