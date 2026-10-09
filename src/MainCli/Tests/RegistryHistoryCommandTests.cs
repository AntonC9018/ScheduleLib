using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;
using Xunit;

public sealed class RegistryHistoryCommandTests
{
    [Fact]
    public async Task MissingPrivateOutputConfigurationDoesNotOpenProvider()
    {
        var provider = new Provider();
        var commands = new Commands(provider);
        Assert.Equal(2, await commands.History(new() { Profile = "synthetic" }, new(), new()));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task SummaryIsLocalAndConsoleContainsOnlyCounts()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".registry-history");
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await new Commands(new Provider()).History(new() { Profile = "synthetic" },
                new() { LocalDirectory = directory, Year = "2024/25" }, new() { Json = true }));
            Assert.DoesNotContain("private-course", output.ToString());
            Assert.DoesNotContain("private-group", output.ToString());
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(1, result.RootElement.GetProperty("schemaVersion").GetInt32());
            var file = Assert.Single(Directory.GetFiles(directory));
            var summary = await File.ReadAllTextAsync(file);
            Assert.Contains("private-course", summary);
            Assert.Contains("2024/25", summary);
            Assert.DoesNotContain("Uri", summary);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
            }
        }
        finally { Console.SetOut(original); if (Directory.Exists(Path.GetDirectoryName(directory))) Directory.Delete(Path.GetDirectoryName(directory)!, true); }
    }

    [Fact]
    public async Task SymbolicPrivateDirectoryIsRejectedBeforeProviderOpens()
    {
        if (OperatingSystem.IsWindows()) return;
        var parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(parent, "target");
        var directory = Path.Combine(parent, ".registry-history");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(directory, target);
        try
        {
            var provider = new Provider();
            Assert.Equal(2, await new Commands(provider).History(new() { Profile = "synthetic" }, new() { LocalDirectory = directory }, new()));
            Assert.Equal(0, provider.Calls);
        }
        finally { Directory.Delete(directory); Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task SymbolicParentIsRejectedBeforeProviderOpens()
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
            Assert.Equal(2, await new Commands(provider).History(new() { Profile = "synthetic" },
                new() { LocalDirectory = Path.Combine(link, ".registry-history") }, new()));
            Assert.Equal(0, provider.Calls);
        }
        finally { Directory.Delete(link); Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task ProviderExceptionsDoNotDisclosePrivateValues()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(5, await new Commands(new Provider { Fail = true }).History(new() { Profile = "synthetic" },
                new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") }, new()));
            Assert.DoesNotContain("canary", error.ToString());
        }
        finally { Console.SetError(original); }
    }

    [Theory]
    [InlineData(RegistryHistoryFailure.MarkupContract)]
    [InlineData(RegistryHistoryFailure.AccessRejected)]
    [InlineData(RegistryHistoryFailure.AuthenticationNotConfirmed)]
    public async Task FailureReasonIsReportedWithoutProviderMessage(RegistryHistoryFailure failure)
    {
        var original = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(5, await new Commands(new Provider { Failure = failure }).History(new() { Profile = "synthetic" },
                new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") }, new()));
            Assert.Contains(failure.ToString(), error.ToString());
            Assert.Contains("phase Unknown; HTTP unknown", error.ToString());
            Assert.DoesNotContain("canary", error.ToString());
        }
        finally { Console.SetError(original); }
    }

    [Fact]
    public async Task HttpDiagnosticPrintsOnlyFailureFixedPhaseAndNumericStatus()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(5, await new Commands(new Provider
            {
                Failure = RegistryHistoryFailure.AccessRejected,
                Phase = RegistryHistoryPhase.Login,
                StatusCode = 403
            }).History(new() { Profile = "synthetic" },
                new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") }, new()));
            Assert.Contains("AccessRejected; phase Login; HTTP 403", error.ToString());
            Assert.DoesNotContain("canary", error.ToString());
            Assert.DoesNotContain("https://", error.ToString());
        }
        finally { Console.SetError(original); }
    }

    private sealed class Commands(Provider provider) : RegistryCommands
    { protected override IRegistryHistoryProvider CreateHistoryProvider() => provider; }
    private sealed class Provider : IRegistryHistoryProvider
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public RegistryHistoryFailure? Failure { get; init; }
        public RegistryHistoryPhase Phase { get; init; } = RegistryHistoryPhase.Unknown;
        public int? StatusCode { get; init; }
        public Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(SettingsArguments settings, string? year, CancellationToken token)
        {
            Calls++;
            if (Failure is { } failure) throw new RegistryHistoryException("canary-password canary-student https://private.test/?token=canary", failure, Phase, StatusCode);
            if (Fail) throw new Exception("canary-password canary-token canary-student");
            return Task.FromResult<IReadOnlyList<RegistryHistoryYearSummary>>([new("2024/25", [new("private-course", 1, ["private-group"])])]);
        }
    }
}
