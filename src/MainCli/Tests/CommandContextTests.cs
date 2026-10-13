using System.Text.Json;
using ScheduleLib.Cli;

public sealed class CommandContextTests
{
    [Fact]
    public void ProviderTimeoutAndUserCancellationHaveDifferentExitCodes()
    {
        using var context = new CliCommandContext(
            "test",
            new(),
            CancellationToken.None);
        var timeout = context.ClassifyInputFailure(new OperationCanceledException());
        Assert.Equal(CliExit.IoFailure, timeout.ExitCode);
        Assert.Equal("Schedule provider request timed out.", Assert.Single(timeout.Errors));
        context.Cancellation.Cancel();
        var cancelled = context.ClassifyInputFailure(new OperationCanceledException());
        Assert.Equal(CliExit.Cancelled, cancelled.ExitCode);
        Assert.Equal("Cancelled.", Assert.Single(cancelled.Errors));
    }

    [Fact]
    public void ConfigurationAndStorageFailuresRemainDistinct()
    {
        using var context = new CliCommandContext(
            "test",
            new(),
            CancellationToken.None);
        Assert.Equal(CliExit.InvalidConfiguration, context.ClassifyInputFailure(new FileNotFoundException()).ExitCode);
        Assert.Equal(CliExit.InvalidConfiguration, context.ClassifyInputFailure(new JsonException()).ExitCode);
        Assert.Equal(CliExit.IoFailure, context.ClassifyInputFailure(new IOException()).ExitCode);
        Assert.Equal(CliExit.InvalidArguments, context.ClassifyInputFailure(new ArgumentException()).ExitCode);
    }

    [Fact]
    public async Task PublishedArtifactsSurviveFailureWithPartialManifestAndReleaseTheirLease()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cli-context-{Guid.NewGuid():N}");
        try
        {
            using (var context = new CliCommandContext(
                       "test",
                       new(),
                       CancellationToken.None))
            {
                context.Artifacts = await RunOutput.Create(
                    directory,
                    context.Command,
                    context.RunId,
                    CancellationToken.None);
                await context.Artifacts.Publish(
                    "example.txt",
                    async (stream, token) =>
                    {
                        await stream.WriteAsync(
                            new byte[]
                            {
                                42,
                            },
                            token);
                    },
                    CancellationToken.None);
                var failure = context.ClassifyInputFailure(new IOException("Disk failure."), export: true);
                Assert.Equal(CliExit.Partial, failure.ExitCode);
                var renderingFailure = context.ClassifyInputFailure(
                    new CliOutputStreamingException("Output failed."),
                    export: true);
                Assert.Equal(CliExit.Partial, renderingFailure.ExitCode);
                var paths = await context.CompleteArtifacts(failure.ExitCode);
                Assert.Contains(context.Artifacts.ManifestPath, paths);
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(context.Artifacts.ManifestPath));
                Assert.Equal("partial", manifest.RootElement.GetProperty("Status").GetString());
            }
            using var next = await RunOutput.Create(
                directory,
                "test",
                Guid.NewGuid().ToString("N"),
                CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
