using ScheduleLib.Cli;

public sealed class CliRuntimeTests
{
    [Fact]
    public void ResolveSourceCapturesRelativeDirectoriesAtEntry()
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
            var source = CliRuntime.ResolveSource(new SourceArguments
            {
                DataDirectory = "data",
                CacheDirectory = "cache",
                NoCache = true,
            });
            Environment.CurrentDirectory = laterDirectory;

            Assert.Equal(expected: dataDirectory, actual: source.DataDirectory);
            Assert.Equal(expected: expectedCacheDirectory, actual: source.CacheDirectory);
            Assert.False(source.UseCache);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }
}
