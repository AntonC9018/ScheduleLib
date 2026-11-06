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
        Assert.Equal(expected: CliExit.IoFailure, actual: timeout.ExitCode);
        string expected2 = "Schedule provider request timed out.";
        string actual2 = Assert.Single(timeout.Errors);
        Assert.Equal(expected: expected2, actual: actual2);
        context.Cancellation.Cancel();
        var cancelled = context.ClassifyInputFailure(new OperationCanceledException());
        Assert.Equal(expected: CliExit.Cancelled, actual: cancelled.ExitCode);
        string expected3 = "Cancelled.";
        string actual3 = Assert.Single(cancelled.Errors);
        Assert.Equal(expected: expected3, actual: actual3);
    }

    [Fact]
    public void ConfigurationAndStorageFailuresRemainDistinct()
    {
        using var context = new CliCommandContext(
            "test",
            new(),
            CancellationToken.None);

        var fileNotFoundClassification = context.ClassifyInputFailure(new FileNotFoundException());
        Assert.Equal(
            expected: CliExit.InvalidConfiguration,
            actual: fileNotFoundClassification.ExitCode);

        var jsonFailureClassification = context.ClassifyInputFailure(new JsonException());
        Assert.Equal(
            expected: CliExit.InvalidConfiguration,
            actual: jsonFailureClassification.ExitCode);

        var ioFailureClassification = context.ClassifyInputFailure(new IOException());
        Assert.Equal(expected: CliExit.IoFailure, actual: ioFailureClassification.ExitCode);

        var argumentFailureClassification = context.ClassifyInputFailure(new ArgumentException());
        Assert.Equal(
            expected: CliExit.InvalidArguments,
            actual: argumentFailureClassification.ExitCode);
    }

    [Fact]
    public async Task PublishedArtifactsSurviveFailureWithPartialManifestAndReleaseTheirLease()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var directory = temp.Path;
        try
        {
            using (var context = new CliCommandContext(
                       "test",
                       new(),
                       CancellationToken.None))
            {
                context.Artifacts = await RunOutput.Create(
                    output: directory,
                    command: context.Command,
                    runId: context.RunId,
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
                Assert.Equal(expected: CliExit.Partial, actual: failure.ExitCode);

                var renderingFailure = context.ClassifyInputFailure(
                    new GeneralCliOutputStreamingException("Output failed."),
                    export: true);
                Assert.Equal(expected: CliExit.Partial, actual: renderingFailure.ExitCode);
                var paths = await context.CompleteArtifacts(failure.ExitCode);
                Assert.Contains(context.Artifacts.ManifestPath, paths);
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(context.Artifacts.ManifestPath));
                Assert.Equal(
                    expected: "partial",
                    actual: manifest.RootElement.GetProperty("Status")
                        .GetString());
            }

            using var next = await RunOutput.Create(
                output: directory,
                command: "test",
                runId: Guid.NewGuid()
                    .ToString("N"),
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
