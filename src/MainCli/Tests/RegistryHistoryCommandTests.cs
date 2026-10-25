using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;

public sealed class RegistryHistoryCommandTests
{
    [Fact]
    public async Task MissingPrivateOutputConfigurationDoesNotOpenProvider()
    {
        var provider = new Provider();
        var commands = new Commands(provider);
        Assert.Equal(
            expected: 2,
            actual: await commands.History(
                new() { Profile = "synthetic" },
                new(),
                new()));
        Assert.Equal(expected: 0, actual: provider.Calls);
    }

    [Fact]
    public async Task SummaryIsLocalAndConsoleContainsOnlyCounts()
    {
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid()
                .ToString("N");
            return path2;
        }

        var directory = Path.Combine(
                    Path.GetTempPath(),
                    CalculatePath2(),
                    ".registry-history");
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(
                expected: 0,
                actual: await new Commands(new Provider()).History(
                    new() { Profile = "synthetic" },
                    new()
                    {
                        LocalDirectory = directory,
                        Year = "2024/25",
                    },
                    new() { Json = true }));
            Assert.DoesNotContain(expectedSubstring: "private-course", actualString: output.ToString());
            Assert.DoesNotContain(expectedSubstring: "private-group", actualString: output.ToString());
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 1, actual: result.RootElement.GetProperty("schemaVersion")
                .GetInt32());
            string[] collection = Directory.GetFiles(directory);
            global::System.Collections.Generic.IEnumerable<string> collection2 = collection;
            var file = Assert.Single(collection2);
            var summary = await File.ReadAllTextAsync(file);
            Assert.Contains(expectedSubstring: "private-course", actualString: summary);
            Assert.Contains(expectedSubstring: "2024/25", actualString: summary);
            Assert.DoesNotContain(expectedSubstring: "Uri", actualString: summary);
            if (!OperatingSystem.IsWindows())
            {
                global::System.IO.UnixFileMode expected2 = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                global::System.IO.UnixFileMode actual2 = File.GetUnixFileMode(file);
                Assert.Equal(expected: expected2, actual: actual2);
                global::System.IO.UnixFileMode expected3 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                global::System.IO.UnixFileMode actual3 = File.GetUnixFileMode(directory);
                Assert.Equal(
                    expected: expected3, actual: actual3);
            }
        }
        finally
        {
            Console.SetOut(original);
            string? path = Path.GetDirectoryName(directory);
            if (Directory.Exists(path))
            {
                Directory.Delete(Path.GetDirectoryName(directory)!, true);
            }
        }
    }

    [Fact]
    public async Task SymbolicPrivateDirectoryIsRejectedBeforeProviderOpens()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string CalculatePath2()
        {
            string path2 = Guid.NewGuid()
                .ToString("N");
            return path2;
        }

        var parent = Path.Combine(Path.GetTempPath(), CalculatePath2());
        var target = Path.Combine(parent, "target");
        var directory = Path.Combine(parent, ".registry-history");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(path: directory, pathToTarget: target);
        try
        {
            var provider = new Provider();
            Assert.Equal(
                expected: 2,
                actual: await new Commands(provider).History(
                    new() { Profile = "synthetic" },
                    new() { LocalDirectory = directory },
                    new()));
            Assert.Equal(expected: 0, actual: provider.Calls);
        }
        finally
        {
            Directory.Delete(directory);
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task SymbolicParentIsRejectedBeforeProviderOpens()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string CalculatePath2()
        {
            string path2 = Guid.NewGuid()
                .ToString("N");
            return path2;
        }

        var parent = Path.Combine(Path.GetTempPath(), CalculatePath2());
        var target = Path.Combine(parent, "target");
        var link = Path.Combine(parent, "link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(path: link, pathToTarget: target);
        try
        {
            var provider = new Provider();
            Assert.Equal(
                expected: 2,
                actual: await new Commands(provider).History(
                    new() { Profile = "synthetic" },
                    new() { LocalDirectory = Path.Combine(link, ".registry-history") },
                    new()));
            Assert.Equal(expected: 0, actual: provider.Calls);
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task ProviderExceptionsDoNotDisclosePrivateValues()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(
                expected: 5,
                actual: await new Commands(new Provider { Fail = true }).History(
                    new() { Profile = "synthetic" },
                    new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") },
                    new()));
            Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
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
            Assert.Equal(
                expected: 5,
                actual: await new Commands(new Provider { Failure = failure }).History(
                    new() { Profile = "synthetic" },
                    new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") },
                    new()));
            Assert.Contains(expectedSubstring: failure.ToString(), actualString: error.ToString());
            Assert.Contains(expectedSubstring: "phase Unknown; diagnostic None; HTTP unknown", actualString: error.ToString());
            Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task HttpDiagnosticPrintsOnlyFixedCodesAndNumericStatus()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(
                expected: 5,
                actual: await new Commands(
                    new Provider
                    {
                        Failure = RegistryHistoryFailure.AccessRejected,
                        Phase = RegistryHistoryPhase.Login,
                        Diagnostic = RegistryHistoryDiagnostic.HttpRejected,
                        StatusCode = 403,
                    }).History(
                    new() { Profile = "synthetic" },
                    new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") },
                    new()));
            Assert.Contains(expectedSubstring: "AccessRejected; phase Login; diagnostic HttpRejected; HTTP 403", actualString: error.ToString());
            Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.ToString());
            Assert.DoesNotContain(expectedSubstring: "https://", actualString: error.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task UnsupportedLayoutReportsFixedDiagnosticWithoutPrivateMarkup()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            Assert.Equal(
                expected: 5,
                actual: await new Commands(
                    new Provider
                    {
                        Failure = RegistryHistoryFailure.MarkupContract,
                        Phase = RegistryHistoryPhase.Groups,
                        Diagnostic = RegistryHistoryDiagnostic.GroupLayout,
                    }).History(
                    new() { Profile = "synthetic" },
                    new() { LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history") },
                    new() { Json = true }));
            Assert.Contains(expectedSubstring: "diagnostic GroupLayout", actualString: error.ToString());
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            var errors = result.RootElement.GetProperty("errors");
            Assert.Equal(expected: 1, actual: errors.GetArrayLength());
            var jsonError = errors[0].GetString();
            Assert.Contains(expectedSubstring: "diagnostic GroupLayout", actualString: jsonError);
            Assert.DoesNotContain(expectedSubstring: "canary", actualString: output.ToString() + error);
            Assert.DoesNotContain(expectedSubstring: "https://", actualString: output.ToString() + error);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed class Commands(Provider provider) : RegistryCommands
    {
        protected override IRegistryHistoryProvider CreateHistoryProvider() => provider;
    }

    private sealed class Provider : IRegistryHistoryProvider
    {
        public int Calls { get; private set; }

        public bool Fail { get; init; }

        public RegistryHistoryFailure? Failure { get; init; }

        public RegistryHistoryPhase Phase { get; init; } = RegistryHistoryPhase.Unknown;

        public RegistryHistoryDiagnostic Diagnostic { get; init; } = RegistryHistoryDiagnostic.None;

        public int? StatusCode { get; init; }

        public Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(
            SettingsArguments settings,
            string? year,
            CancellationToken token)
        {
            Calls++;
            if (Failure is { } failure)
            {
                throw new RegistryHistoryException(
                    "canary-password canary-student https://private.test/?token=canary",
                    failure,
                    Phase,
                    StatusCode,
                    Diagnostic);
            }

            if (Fail)
            {
                throw new Exception("canary-password canary-token canary-student");
            }

            return Task.FromResult<IReadOnlyList<RegistryHistoryYearSummary>>(
            [
                new(
                    "2024/25",
                    [
                        new(
                            "private-course",
                            1,
                            ["private-group"]),
                    ]),
            ]);
        }
    }
}
