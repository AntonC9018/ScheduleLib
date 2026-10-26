using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;

public sealed class RegistryHistoryTopicsCommandTests
{
    [Fact]
    public async Task InvalidOutputCannotOpenProvider()
    {
        var provider = new Provider();
        Assert.Equal(
            expected: 2,
            actual: await new Commands(provider).HistoryTopics(
                new()
                {
                    Profile = "synthetic",
                },
                new(),
                new()));
        Assert.Equal(expected: 0, actual: provider.Calls);
    }

    [Fact]
    public async Task TopicsAndProvenanceRemainPrivateAndConsoleContainsCounts()
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
                actual: await new Commands(new Provider()).HistoryTopics(
                    new()
                    {
                        Profile = "synthetic",
                    },
                    new()
                    {
                        LocalDirectory = directory,
                    },
                    new()
                    {
                        Json = true,
                    }));
            Assert.DoesNotContain(expectedSubstring: "canary", actualString: output.ToString());
            Assert.DoesNotContain(expectedSubstring: "private.test", actualString: output.ToString());
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 1,
                actual: result.RootElement.GetProperty("schemaVersion")
                    .GetInt32());
            string[] collection = Directory.GetFiles(path: directory, searchPattern: "*.json");
            IEnumerable<string> collection2 = collection;
            var inventory = Assert.Single(collection2);
            Assert.Contains(expectedSubstring: "canary-current-topic",
                actualString: await File.ReadAllTextAsync(inventory));
            Assert.Contains(
                expectedSubstring: "canary-historical-topic",
                actualString: await File.ReadAllTextAsync(
                    Assert.Single(Directory.GetFiles(path: directory, searchPattern: "*.csv"))));
            if (!OperatingSystem.IsWindows())
            {
                foreach (var file in Directory.GetFiles(directory))
                {
                    var expected2 = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    var actual2 = File.GetUnixFileMode(file);
                    Assert.Equal(expected: expected2, actual: actual2);
                }

                var expected3 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                var actual3 = File.GetUnixFileMode(directory);
                Assert.Equal(
                    expected: expected3,
                    actual: actual3);
            }
        }
        finally
        {
            Console.SetOut(original);
            string? path3 = Path.GetDirectoryName(directory);
            if (Directory.Exists(path3))
            {
                Directory.Delete(Path.GetDirectoryName(directory)!, true);
            }
        }
    }

    [Fact]
    public async Task SymbolicParentIsRejectedBeforeOpeningProvider()
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
                actual: await new Commands(provider).HistoryTopics(
                    new()
                    {
                        Profile = "synthetic",
                    },
                    new()
                    {
                        LocalDirectory = Path.Combine(link, ".registry-history"),
                    },
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
    public async Task ProviderFailuresDoNotDiscloseTopicsOrCredentials()
    {
        var original = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(
                expected: 5,
                actual: await new Commands(new Provider
                {
                    Fail = true,
                }).HistoryTopics(
                    new()
                    {
                        Profile = "synthetic",
                    },
                    new()
                    {
                        LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history"),
                    },
                    new()));
            Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
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
            Assert.Equal(
                expected: 5,
                actual: await new Commands(new Provider
                {
                    Diagnostic = RegistryHistoryDiagnostic.TopicTable,
                })
                    .HistoryTopics(
                        new()
                        {
                            Profile = "synthetic",
                        },
                        new()
                        {
                            LocalDirectory = Path.Combine(Path.GetTempPath(), ".registry-history"),
                        },
                        new()
                        {
                            Json = true,
                        }));
            Assert.Contains(expectedSubstring: "MarkupContract; phase Lessons; diagnostic TopicTable; HTTP unknown",
                actualString: error.ToString());
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            var errors = result.RootElement.GetProperty("errors");
            Assert.Equal(expected: 1, actual: errors.GetArrayLength());
            Assert.Contains(expectedSubstring: "diagnostic TopicTable", actualString: errors[0].GetString());
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
        protected override IRegistryHistoryTopicsProvider CreateHistoryTopicsProvider() => provider;
    }

    private sealed class Provider : IRegistryHistoryTopicsProvider
    {
        public int Calls { get; private set; }

        public bool Fail { get; init; }

        public RegistryHistoryDiagnostic Diagnostic { get; init; } = RegistryHistoryDiagnostic.None;

        public Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token)
        {
            Calls++;
            if (Diagnostic != RegistryHistoryDiagnostic.None)
            {
                throw new RegistryHistoryException(
                    "canary-student https://private.test/?token=canary",
                    phase: RegistryHistoryPhase.Lessons,
                    diagnostic: Diagnostic);
            }

            if (Fail)
            {
                throw new Exception("canary-password canary-student canary-topic");
            }

            Uri CalculateUri()
            {
                Uri uri = new("https://private.test/groups/1");
                return uri;
            }

            var course = new RegistryHistoryCourse(
                "canary-course",
                1,
                CalculateUri());

            Uri CalculateUri2()
            {
                Uri uri2 = new("https://private.test/LessonsList/1");
                return uri2;
            }

            DateTime CalculateDate()
            {
                var date = new DateTime(
                    year: 2024,
                    month: 10,
                    day: 1);
                return date;
            }

            var group = new RegistryHistoryGroupTopics(
                "canary-group",
                CalculateUri2(),
                [
                    new(
                        CalculateDate(),
                        ScheduleLib.LessonType.Curs,
                        "canary-historical-topic"),
                ]);
            var historical = new RegistryHistoryCourseTopics(
                "2024/25",
                course,
                [group]);

            DateTime CalculateDate2()
            {
                var date2 = new DateTime(
                    year: 2025,
                    month: 10,
                    day: 1);
                return date2;
            }

            var currentGroup = group with
            {
                Lessons =
                [
                    new(
                        CalculateDate2(),
                        ScheduleLib.LessonType.Curs,
                        "canary-current-topic"),
                ],
            };
            var current = new RegistryHistoryCourseTopics(
                "2025/26",
                course,
                [currentGroup]);
            return Task.FromResult(new RegistryHistoryTopicsAnalysis(
                ["2025/26", "2024/25"],
                [current],
                new(
                    "2025/26",
                    [new(course, historical)],
                    [])));
        }
    }
}
