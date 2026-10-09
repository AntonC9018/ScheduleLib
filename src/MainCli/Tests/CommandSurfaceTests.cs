using System.Text.Json;
using ScheduleLib.Cli;
using Xunit;

public sealed class CommandSurfaceTests
{
    public static TheoryData<string> Operations => new()
    {
        "export pdf", "export ics", "export teachers-excel", "export free-rooms",
        "export lab-deadlines", "export website-schedules", "export website-theses", "export pre-defense",
        "query lessons", "query free-hours", "drive publish", "calendar sync",
        "registry sync", "registry import-grades", "curricula download",
        "config show", "config get", "config set", "config unset", "config remove",
        "config clear", "config validate", "config profiles", "auth login", "auth status", "auth logout",
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryOperationHasHelpWithoutReadingEvenInvalidProjectSettings(string operation)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"schedulelib-help-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var config = Path.Combine(directory, "schedulelib.json");
        await File.WriteAllTextAsync(config, "invalid settings must not be loaded by help");
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var arguments = operation.Split(' ').Concat(["--project", directory, "--help"]).ToArray();
            Assert.Equal(0, await CliHost.Run(arguments));
            Assert.Contains($"schedulelib {operation}", output.ToString());
            Assert.Contains("--json", output.ToString());
            Assert.Equal("invalid settings must not be loaded by help", await File.ReadAllTextAsync(config));
            Assert.Single(Directory.EnumerateFileSystemEntries(directory));
            Assert.Empty(error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string[]> InvalidOperations => new()
    {
        new[] { "config", "show", "--data-dir", "/missing" },
        new[] { "auth", "status", "google", "--output", "/missing" },
        new[] { "export", "pre-defense", "--no-cache" },
        new[] { "curricula", "download", "--apply" },
        new[] { "query", "lessons", "export", "pdf" },
    };

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
            Assert.Equal(2, await CliHost.Run([.. arguments, "--json"]));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, result.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(2, result.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
            Assert.Empty(result.RootElement.GetProperty("outputs").EnumerateArray());
            Assert.Empty(result.RootElement.GetProperty("actions").EnumerateArray());
            Assert.NotEmpty(error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
