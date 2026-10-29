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

        int CalculateActual()
        {
            var classifyInputFailure = context.ClassifyInputFailure(new FileNotFoundException());
            int actual2 = classifyInputFailure.ExitCode;
            return actual2;
        }

        Assert.Equal(expected: CliExit.InvalidConfiguration, actual: CalculateActual());

        int CalculateActual2()
        {
            var classifyInputFailure2 = context.ClassifyInputFailure(new JsonException());
            int actual3 = classifyInputFailure2.ExitCode;
            return actual3;
        }

        Assert.Equal(expected: CliExit.InvalidConfiguration, actual: CalculateActual2());

        int CalculateActual3()
        {
            var classifyInputFailure3 = context.ClassifyInputFailure(new IOException());
            int actual4 = classifyInputFailure3.ExitCode;
            return actual4;
        }

        Assert.Equal(expected: CliExit.IoFailure, actual: CalculateActual3());

        int CalculateActual4()
        {
            var classifyInputFailure4 = context.ClassifyInputFailure(new ArgumentException());
            int actual5 = classifyInputFailure4.ExitCode;
            return actual5;
        }

        Assert.Equal(expected: CliExit.InvalidArguments, actual: CalculateActual4());
    }

    [Fact]
    public async Task PublishedArtifactsSurviveFailureWithPartialManifestAndReleaseTheirLease()
    {
        string CalculatePath2()
        {
            string path2 = $"cli-context-{Guid.NewGuid():N}";
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
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

                IOException CalculateError()
                {
                    var error = new IOException("Disk failure.");
                    return error;
                }

                var failure = context.ClassifyInputFailure(CalculateError(), export: true);
                Assert.Equal(expected: CliExit.Partial, actual: failure.ExitCode);

                GeneralCliOutputStreamingException CalculateError2()
                {
                    var error2 = new GeneralCliOutputStreamingException("Output failed.");
                    return error2;
                }

                var renderingFailure = context.ClassifyInputFailure(
                    CalculateError2(),
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

            string CalculateRunId()
            {
                string runId2 = Guid.NewGuid()
                    .ToString("N");
                return runId2;
            }

            using var next = await RunOutput.Create(
                output: directory,
                command: "test",
                runId: CalculateRunId(),
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
