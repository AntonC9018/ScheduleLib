using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;
using Xunit;

public sealed class RegistryHistoryTopicsCommandTests
{
    [Fact]
    public async Task InvalidOutputCannotOpenProvider()
    {
        var provider = new Provider();
        Assert.Equal(2, await new Commands(provider).HistoryTopics(new() { Profile = "synthetic" }, new(), new()));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task TopicsAndProvenanceRemainPrivateAndConsoleContainsCounts()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".registry-history");
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await new Commands(new Provider()).HistoryTopics(new() { Profile = "synthetic" },
                new() { LocalDirectory = directory }, new() { Json = true }));
            Assert.DoesNotContain("canary", output.ToString());
            Assert.DoesNotContain("private.test", output.ToString());
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(1, result.RootElement.GetProperty("schemaVersion").GetInt32());
            var inventory = Assert.Single(Directory.GetFiles(directory, "*.json"));
            Assert.Contains("canary-current-topic", await File.ReadAllTextAsync(inventory));
            Assert.Contains("canary-historical-topic", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "*.csv"))));
            if (!OperatingSystem.IsWindows())
            {
                foreach (var file in Directory.GetFiles(directory)) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
            }
        }
        finally { Console.SetOut(original); if (Directory.Exists(Path.GetDirectoryName(directory))) Directory.Delete(Path.GetDirectoryName(directory)!, true); }
    }

    [Fact]
    public async Task SymbolicParentIsRejectedBeforeOpeningProvider()
    {
        if (OperatingSystem.IsWindows()) return;
        var parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(parent, "target");
        var link = Path.Combine(parent, "link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        try
        {
            var provider = new Provider();
            Assert.Equal(2, await new Commands(provider).HistoryTopics(new() { Profile = "synthetic" },
                new() { LocalDirectory = Path.Combine(link, ".registry-history") }, new()));
            Assert.Equal(0, provider.Calls);
        }
        finally { Directory.Delete(link); Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task ProviderFailuresDoNotDiscloseTopicsOrCredentials()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(5, await new Commands(new Provider { Fail = true }).HistoryTopics(new() { Profile = "synthetic" },
                new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") }, new()));
            Assert.DoesNotContain("canary", error.ToString());
        }
        finally { Console.SetError(original); }
    }

    [Fact]
    public async Task UnsupportedTopicLayoutReportsFixedDiagnosticWithoutPrivateContent()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            Assert.Equal(5, await new Commands(new Provider { Diagnostic = RegistryHistoryDiagnostic.TopicTable })
                .HistoryTopics(new() { Profile = "synthetic" },
                    new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") }, new() { Json = true }));
            Assert.Contains("MarkupContract; phase Lessons; diagnostic TopicTable; HTTP unknown", error.ToString());
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            var errors = result.RootElement.GetProperty("errors");
            Assert.Equal(1, errors.GetArrayLength());
            Assert.Contains("diagnostic TopicTable", errors[0].GetString());
            Assert.DoesNotContain("canary", output.ToString() + error);
            Assert.DoesNotContain("https://", output.ToString() + error);
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    private sealed class Commands(Provider provider) : RegistryCommands
    { protected override IRegistryHistoryTopicsProvider CreateHistoryTopicsProvider() => provider; }
    private sealed class Provider : IRegistryHistoryTopicsProvider
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public RegistryHistoryDiagnostic Diagnostic { get; init; } = RegistryHistoryDiagnostic.None;
        public Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token)
        {
            Calls++;
            if (Diagnostic != RegistryHistoryDiagnostic.None)
                throw new RegistryHistoryException("canary-student https://private.test/?token=canary", phase: RegistryHistoryPhase.Lessons,
                    diagnostic: Diagnostic);
            if (Fail) throw new Exception("canary-password canary-student canary-topic");
            var course = new RegistryHistoryCourse("canary-course", 1, new("https://private.test/groups/1"));
            var group = new RegistryHistoryGroupTopics("canary-group", new("https://private.test/LessonsList/1"),
                [new(new DateTime(2024, 10, 1), ScheduleLib.LessonType.Curs, "canary-historical-topic")]);
            var historical = new RegistryHistoryCourseTopics("2024/25", course, [group]);
            var currentGroup = group with { Lessons = [new(new DateTime(2025, 10, 1), ScheduleLib.LessonType.Curs, "canary-current-topic")] };
            var current = new RegistryHistoryCourseTopics("2025/26", course, [currentGroup]);
            return Task.FromResult(new RegistryHistoryTopicsAnalysis(["2025/26", "2024/25"], [current], new("2025/26", [new(course, historical)], [])));
        }
    }
}
