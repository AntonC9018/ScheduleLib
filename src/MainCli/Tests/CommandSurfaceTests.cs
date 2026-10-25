using System.Text.Json;
using ScheduleLib.Cli;

public sealed class CommandSurfaceTests
{
    public static TheoryData<string> Operations
    {
        get
        {
            return new()
            {
                "export pdf",
                "export ics",
                "export teachers-excel",
                "export free-rooms",
                "export lab-deadlines",
                "export website-schedules",
                "export website-theses",
                "export pre-defense",
                "query lessons",
                "query free-hours",
                "drive publish",
                "calendar sync",
                "registry sync",
                "registry import-grades",
                "registry history",
                "curricula download",
                "config show",
                "config get",
                "config set",
                "config unset",
                "config remove",
                "config clear",
                "config validate",
                "config profiles",
                "auth login",
                "auth status",
                "auth logout",
            };
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryOperationHasHelpWithoutReadingEvenInvalidProjectSettings(string operation)
    {
        string CalculatePath2()
        {
            string path2 = $"schedulelib-help-{Guid.NewGuid():N}";
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(directory);
        var config = Path.Combine(directory, "schedulelib.json");
        await File.WriteAllTextAsync(path: config, contents: "invalid settings must not be loaded by help");
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var arguments = operation.Split(' ')
                .Concat(["--project", directory, "--help"])
                .ToArray();
            Assert.Equal(expected: 0, actual: await CliHost.Run(arguments));
            string CalculateExpectedSubstring()
            {
                string expectedSubstring2 = $"schedulelib {operation}";
                return expectedSubstring2;
            }

            Assert.Contains(expectedSubstring: CalculateExpectedSubstring(), actualString: output.ToString());
            Assert.Contains(expectedSubstring: "--json", actualString: output.ToString());
            Assert.Equal(expected: "invalid settings must not be loaded by help", actual: await File.ReadAllTextAsync(config));
            global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFileSystemEntries(directory);
            Assert.Single(collection);
            Assert.Empty(error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string[]> InvalidOperations
    {
        get
        {
            return new()
            {
                new[]
                {
                    "config",
                    "show",
                    "--data-dir",
                    "/missing",
                },
                new[]
                {
                    "auth",
                    "status",
                    "google",
                    "--output",
                    "/missing",
                },
                new[]
                {
                    "export",
                    "pre-defense",
                    "--no-cache",
                },
                new[]
                {
                    "curricula",
                    "download",
                    "--apply",
                },
                new[]
                {
                    "query",
                    "lessons",
                    "export",
                    "pdf",
                },
            };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidOperations))]
    public async Task InapplicableOptionsAndChainedOperationsProduceOneArgumentErrorEnvelope(string[] arguments)
    {
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            Assert.Equal(expected: 2, actual: await CliHost.Run([.. arguments, "--json"]));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 1, actual: result.RootElement.GetProperty("schemaVersion")
                .GetInt32());
            Assert.Equal(expected: 2, actual: result.RootElement.GetProperty("exitCode")
                .GetInt32());
            Assert.Equal(expected: "failed", actual: result.RootElement.GetProperty("status")
                .GetString());
            Assert.Empty(result.RootElement.GetProperty("outputs")
                .EnumerateArray());
            Assert.Empty(result.RootElement.GetProperty("actions")
                .EnumerateArray());
            Assert.NotEmpty(error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
