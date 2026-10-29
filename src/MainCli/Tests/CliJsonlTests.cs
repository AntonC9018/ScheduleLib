using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using Event = Google.Apis.Calendar.v3.Data.Event;

public sealed class CliJsonlTests
{
    /// <summary>A command invocation that must reject combined JSON and JSONL output.</summary>
    private readonly record struct Case(string Name, Func<Task<int>> Run);

    private static JsonSerializerOptions JsonOptions
    {
        get
        {
            return new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
        }
    }

    [Fact]
    public async Task JsonAndJsonlTogetherRejectWithArgumentError()
    {
        await using var fake = new FakeProvider();
        var commands = new TestCommands(fake);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(
                expected: 2,
                actual: await commands.Sync(
                    new(),
                    new()
                    {
                        Profile = "synthetic-teacher",
                    },
                    new()
                    {
                        Json = true,
                        Jsonl = true,
                    },
                    new()));
            Assert.Empty(stdout.ToString());
            Assert.Contains(expectedSubstring: "Only one of --json and --jsonl", actualString: stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        // CliHost routing also rejects the pair.
        Assert.Equal(expected: 2, actual: await CliHost.Run(["query", "lessons", "--json", "--jsonl"]));
    }

    [Fact]
    public async Task BlockedApplyStreamsEarlyItemsBeforeFinal()
    {
        var block = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fake = new FakeProvider
        {
            BlockSecond = block.Task,
        };
        var commands = new TestCommands(fake);
        using var stdoutCapture = new SyncCapture();
        using var stderrCapture = new SyncCapture();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(stdoutCapture);
            Console.SetError(stderrCapture);
            var run = commands.Sync(
                new(),
                new()
                {
                    Profile = "synthetic-teacher",
                },
                new()
                {
                    Jsonl = true,
                },
                new()
                {
                    Apply = true,
                });

            // Wait for early output: start + 3 planned/not-attempted + first completed.
            TimeSpan CalculateTimeout()
            {
                var timeout = TimeSpan.FromSeconds(5);
                return timeout;
            }

            var early = await WaitFor(
                () => stdoutCapture.Snapshot(),
                s =>
                {
                    if (!s.Contains("\"event\":\"start\""))
                    {
                        return false;
                    }
                    if (!s.Contains("\"id\":\"0\""))
                    {
                        return false;
                    }
                    if (!s.Contains("completed"))
                    {
                        return false;
                    }

                    if (!s.Contains("\"event\":\"final\""))
                    {
                        return true;
                    }

                    return false;
                },
                CalculateTimeout());
            Assert.Contains(expectedSubstring: "\"event\":\"start\"", actualString: early);
            Assert.Contains(expectedSubstring: "\"id\":\"0\"", actualString: early);
            Assert.DoesNotContain(expectedSubstring: "\"event\":\"final\"", actualString: early);
            block.SetResult(true);
            Assert.Equal(expected: 0, actual: await run);
            var all = stdoutCapture.Snapshot();
            var lines = all.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int int32 = lines.Length;
            int int322 = 1 + 3;
            int int323 = int322 + 1;
            int int324 = int323 + 1;
            bool condition2 = int32 >= int324;
            string userMessage = $"Expected start + initial + updates + final, got {lines.Length}";
            Assert.True(
                condition2,
                userMessage);
            var final = JsonDocument.Parse(lines[^1]).RootElement;
            Assert.Equal(
                expected: "final",
                actual: final.GetProperty("event")
                    .GetString());
            Assert.Equal(
                expected: "applied",
                actual: final.GetProperty("status")
                    .GetString());

            // Final must not repeat the full outcomes array.
            bool CalculateCondition()
            {
                bool condition = final.TryGetProperty("outcomes", out _);
                return condition;
            }

            Assert.False(CalculateCondition(), "Final JSONL must not repeat emitted items.");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task JsonlSchemaSequenceAndRunCorrelation()
    {
        await using var fake = new FakeProvider();
        var (exit, stdout, stderr) = await CaptureJsonl(() => new TestCommands(fake).Sync(
            new(),
            new()
            {
                Profile = "synthetic-teacher",
            },
            new()
            {
                Jsonl = true,
            },
            new()));
        Assert.Equal(expected: 0, actual: exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 3);
        string? runId = null;
        var actionKinds = new List<string?>();
        for (var i = 0; i < lines.Length; i++)
        {
            using var doc = JsonDocument.Parse(lines[i]);
            var root = doc.RootElement;
            if (root.TryGetProperty("item", out var item))
            {
                actionKinds.Add(item.GetProperty("kind").GetString());
            }
            Assert.Equal(
                expected: 1,
                actual: root.GetProperty("schemaVersion")
                    .GetInt32());
            Assert.Equal(
                expected: "registry sync",
                actual: root.GetProperty("command")
                    .GetString());
            Assert.Equal(
                expected: i,
                actual: root.GetProperty("seq")
                    .GetInt32());
            if (runId is null)
            {
                runId = root.GetProperty("runId")
                    .GetString();
            }
            Assert.Equal(
                expected: runId,
                actual: root.GetProperty("runId")
                    .GetString());
        }
        Assert.Equal(expected: ["create", "update", "delete"], actual: actionKinds);
        Assert.Equal(
            expected: "start",
            actual: JsonDocument.Parse(lines[0]).RootElement
                .GetProperty("event")
                .GetString());
        Assert.Equal(
            expected: "final",
            actual: JsonDocument.Parse(lines[^1]).RootElement
                .GetProperty("event")
                .GetString());
        // Stable identity: each action id appears, preview emits planned once.
        var ids = lines.Skip(1)
            .Take(lines.Length - 2)
            .Select(l => JsonDocument.Parse(l).RootElement
                .GetProperty("id")
                .GetString())
            .ToArray();
        Assert.Equal(
            expected: new[]
            {
                "0",
                "1",
                "2",
            },
            actual: ids);
        // Stderr warnings are JSONL with independent seq starting at 0 and same runId.
        var errLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(errLines);
        for (var i = 0; i < errLines.Length; i++)
        {
            using var doc = JsonDocument.Parse(errLines[i]);
            var root = doc.RootElement;
            Assert.Equal(
                expected: "log",
                actual: root.GetProperty("event")
                    .GetString());
            Assert.Equal(
                expected: i,
                actual: root.GetProperty("seq")
                    .GetInt32());
            Assert.Equal(
                expected: runId,
                actual: root.GetProperty("runId")
                    .GetString());
        }
        // Stdout contains no human text.
        foreach (var line in lines)
        {
            Assert.StartsWith(expectedStartString: "{", actualString: line.TrimStart());
        }
    }

    [Fact]
    public void StructuredLogsStayOnStderrWithoutStdoutContamination()
    {
        var output = new CliOutput(
            command: "synthetic command",
            runId: "synthetic-run",
            json: false,
            jsonl: true);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            using var provider = new CliJsonlLoggerProvider(output);
            var logger = provider.CreateLogger("synthetic");
            logger.LogInformation("Synthetic information {Value}", 42);
            logger.LogWarning("Synthetic warning");

            InvalidOperationException CalculateException()
            {
                var exception = new InvalidOperationException("synthetic failure");
                return exception;
            }

            logger.LogError(CalculateException(), "Synthetic error");
            output.WriteWarning("Synthetic warning line");
            output.WriteError("Synthetic error line");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        Assert.Empty(stdout.ToString());
        var lines = stderr.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected: 5, actual: lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            Assert.Equal(
                expected: 1,
                actual: root.GetProperty("schemaVersion")
                    .GetInt32());
            Assert.Equal(
                expected: "synthetic-run",
                actual: root.GetProperty("runId")
                    .GetString());
            Assert.Equal(
                expected: "log",
                actual: root.GetProperty("event")
                    .GetString());
        }
        Assert.Contains(expectedSubstring: "Information", actualString: lines[0]);
        Assert.Contains(expectedSubstring: "Warning", actualString: lines[1]);
        Assert.Contains(expectedSubstring: "Error", actualString: lines[2]);
    }

    [Fact]
    public async Task PlanLogsMatchUnderPreviewAndApply()
    {
        var previewLogs = await PlanLogs(explicitApply: false);
        var applyLogs = await PlanLogs(explicitApply: true);
        Assert.NotEmpty(previewLogs);
        Assert.Equal(expected: previewLogs, actual: applyLogs);

        static async Task<string[]> PlanLogs(bool explicitApply)
        {
            using var settings = await CliSettings.Load(new()
            {
                Profile = "Curmanschii Anton",
            });
            var services = CliRuntime.CreateServices(
                new()
                {
                    NoCache = true,
                    DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                },
                AppConfiguration.ConfigureServices);
            settings.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                var scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
            using var capture = new LogCapture();
            services.AddLogging(b =>
            {
                b.ClearProviders();
                b.AddProvider(capture);
            });
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(default);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            var lesson = schedule.EnumerateWeeklyLessons()
                .First();

            Uri CalculateUri()
            {
                Uri uri2 = new("https://registry.test/group");
                return uri2;
            }

            Uri CalculateEvaluationUri()
            {
                Uri evaluationUri2 = new("https://registry.test/evaluation");
                return evaluationUri2;
            }

            var navigator = new FixtureNavigator(
                lesson.Lesson.Course,
                new GroupLink(
                    new FoundGroups
                    {
                        IsWildcard = !lesson.Lesson.Groups.IsSingleGroup,
                        Value = lesson.Lesson.Groups,
                    },
                    lesson.Lesson.GroupPartitionKey,
                    uri: CalculateUri(),
                    evaluationUri: CalculateEvaluationUri()));
            var handler = scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>();
            await handler.Plan(
                new()
                {
                    Navigator = navigator,
                    Attendance = new([]),
                    LessonTopics = NoLessonTopics.Instance,
                    Semester = Semester.Sem2,
                },
                explicitApply: explicitApply);
            return capture.Messages.ToArray();
        }
    }

    [Fact]
    public async Task EarlyArgumentFailuresEmitJsonlStartAndFinal()
    {
        // Missing profile fails before any provider work.
        var (exit, stdout, stderr) = await CaptureJsonl(() => new TestCommands(new FakeProvider()).Sync(
            new(),
            new(),
            new()
            {
                Jsonl = true,
            },
            new()));
        Assert.Equal(expected: 3, actual: exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected: 2, actual: lines.Length);
        Assert.Equal(
            expected: "start",
            actual: JsonDocument.Parse(lines[0]).RootElement
                .GetProperty("event")
                .GetString());
        var final = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Equal(
            expected: "final",
            actual: final.GetProperty("event")
                .GetString());
        Assert.Equal(
            expected: "failed",
            actual: final.GetProperty("status")
                .GetString());
        Assert.Equal(
            expected: 3,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        Assert.NotEmpty(final.GetProperty("errors")
            .EnumerateArray());
        using var err = JsonDocument.Parse(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        Assert.Equal(
            expected: "log",
            actual: err.RootElement.GetProperty("event")
                .GetString());

        // Invalid quiz id is an argument error before planning.
        var (gradeExit, gradeStdout, _) = await CaptureJsonl(() =>
            new TestGradeCommands(new FakeGradeProvider()).ImportGrades(
                new()
                {
                    QuizId = "0",
                },
                new(),
                new()
                {
                    Profile = "synthetic-teacher",
                },
                new()
                {
                    Jsonl = true,
                },
                new()));
        Assert.Equal(expected: 2, actual: gradeExit);
        Assert.Equal(
            expected: "final",
            actual: JsonDocument.Parse(gradeStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1]).RootElement
                .GetProperty("event")
                .GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupParseErrorUsesOnlyFormatOptionsParsedBeforeTheError(bool jsonlBeforeError)
    {
        var arguments = jsonlBeforeError
            ? new[] { "query", "lessons", "--jsonl", "--unknown-flag" }
            : new[] { "query", "lessons", "--unknown-flag", "--jsonl" };
        var (exit, output, error) = await CaptureHost(arguments);

        Assert.Equal(expected: 2, actual: exit);
        if (jsonlBeforeError)
        {
            var outLines = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(expected: 2, actual: outLines.Length);
            Assert.Equal(
                expected: "start",
                actual: JsonDocument.Parse(outLines[0]).RootElement
                    .GetProperty("event")
                    .GetString());
            Assert.Equal(
                expected: "final",
                actual: JsonDocument.Parse(outLines[1]).RootElement
                    .GetProperty("event")
                    .GetString());
            var errLines = error
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.NotEmpty(errLines);
            foreach (var line in errLines)
            {
                JsonDocument.Parse(line);
            }
        }
        else
        {
            Assert.DoesNotContain("\"event\"", output);
            Assert.DoesNotContain("\"event\"", error);
            Assert.Contains("unknown", output + error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task HelpAliasTakesPrecedenceOverResultFormats()
    {
        var (exit, output, error) = await CaptureHost(
            ["query", "lessons", "-?", "--jsonl", "--json"]);

        Assert.Equal(expected: CliExit.Success, actual: exit);
        Assert.Contains("Usage:", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"event\"", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task OptionAfterEndOfOptionsDoesNotSelectJsonl()
    {
        var (exit, output, error) = await CaptureHost(["query", "lessons", "--", "--jsonl"]);

        Assert.Equal(expected: CliExit.InvalidArguments, actual: exit);
        Assert.DoesNotContain("\"event\"", output);
        Assert.DoesNotContain("\"event\"", error);
        Assert.NotEmpty(output + error);
    }

    [Fact]
    public async Task JsonlAfterParseErrorDoesNotConflictWithEarlierJson()
    {
        var (exit, output, _) = await CaptureHost(
            ["query", "lessons", "--json", "--unknown-flag", "--jsonl"]);

        Assert.Equal(expected: CliExit.InvalidArguments, actual: exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(
            expected: CliExit.InvalidArguments,
            actual: document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(
            expected: "failed",
            actual: document.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("jsonl")]
    [InlineData("help-jsonl")]
    public async Task PostParseExceptionsKeepDiagnosticsInTheSelectedOutputMode(string mode)
    {
        string[] formats = mode switch
        {
            "json" => ["--json"],
            "jsonl" => ["--jsonl"],
            "help-jsonl" => ["-?", "--jsonl"],
            _ => [],
        };
        string[] arguments = ["query", "lessons", .. formats];
        var (exit, output, error) = await CaptureHost(
            arguments,
            runner => runner.Configure(builder => builder.UseMiddleware(
                (_, _) => throw new InvalidOperationException("failure after parsing"),
                CommandDotNet.Execution.MiddlewareSteps.ParseInput + 2)));

        Assert.Equal(expected: CliExit.Unexpected, actual: exit);
        Assert.Contains("failure after parsing", error);
        if (mode == "jsonl")
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var record = JsonDocument.Parse(line);
            }
            foreach (var line in error.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var record = JsonDocument.Parse(line);
            }
            Assert.Contains("\"event\":\"final\"", output);
            return;
        }

        if (mode == "json")
        {
            using var result = JsonDocument.Parse(output);
            Assert.Equal(expected: CliExit.Unexpected, actual: result.RootElement.GetProperty("exitCode").GetInt32());
            return;
        }

        Assert.Empty(output);
    }

    [Fact]
    public async Task BothParsedFormatsReplaceLaterParserDiagnosticsWithOneConflictMessage()
    {
        var (exit, output, error) = await CaptureHost(
            ["query", "lessons", "--json", "--jsonl", "--unknown-flag"]);

        Assert.Equal(expected: CliExit.InvalidArguments, actual: exit);
        Assert.Empty(output);
        Assert.Equal(expected: CliOutput.ConflictingResultFormatsMessage + Environment.NewLine, actual: error);
    }

    [Fact]
    public async Task FormatFlagConsumedAsAnOptionValueDoesNotSelectStartupJson()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await CliHost.RunCore(
                ["config", "get", "--project", "--json", "example"],
                runner => runner.Configure(builder => builder.UseMiddleware(
                    (_, _) => throw new InvalidOperationException("failure after parsing"),
                    CommandDotNet.Execution.MiddlewareSteps.InvokeCommand - 2)));

            Assert.Equal(expected: CliExit.Unexpected, actual: exit);
            Assert.DoesNotContain("\"exitCode\"", stdout.ToString());
            Assert.DoesNotContain("\"event\"", stdout.ToString());
            Assert.Contains("failure after parsing", stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task FailureBeforeParsingDoesNotUseRawFormatArguments()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await CliHost.RunCore(
                ["query", "lessons", "--jsonl"],
                runner => runner.Configure(builder => builder.UseMiddleware(
                    (_, _) => throw new InvalidOperationException("failure before parsing"),
                    CommandDotNet.Execution.MiddlewareSteps.ParseInput - 1)));

            Assert.Equal(expected: CliExit.Unexpected, actual: exit);
            Assert.Empty(stdout.ToString());
            Assert.Contains("failure before parsing", stderr.ToString());
            Assert.DoesNotContain("\"event\"", stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static async Task<(int Exit, string Output, string Error)> CaptureHost(
        string[] args,
        Action<CommandDotNet.AppRunner<Commands>>? configure = null)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await CliHost.RunCore(args, configure);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task CancellationAndUncertainActionsStreamWithCancelledFinal()
    {
        await using var fake = new FakeProvider
        {
            CancelSecond = true,
        };
        var (exit, stdout, _) = await CaptureJsonl(() => new TestCommands(fake).Sync(
            new(),
            new()
            {
                Profile = "synthetic-teacher",
            },
            new()
            {
                Jsonl = true,
            },
            new()
            {
                Apply = true,
            }));
        Assert.Equal(expected: 130, actual: exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1)
            .Take(lines.Length - 2)
            .Select(l => JsonDocument.Parse(l))
            .Select(d => d.RootElement)
            .ToArray();
        Assert.Contains(
            items,
            x => x.GetProperty("id")
                    .GetString()
                == "0"
                && x.GetProperty("status")
                    .GetString()
                == "completed");
        Assert.Contains(
            items,
            x => x.GetProperty("id")
                    .GetString()
                == "1"
                && x.GetProperty("status")
                    .GetString()
                == "uncertain");
        var final = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal(
            expected: "cancelled",
            actual: final.GetProperty("status")
                .GetString());
        Assert.Equal(
            expected: 130,
            actual: final.GetProperty("exitCode")
                .GetInt32());

        bool CalculateCondition()
        {
            bool condition = final.TryGetProperty("outcomes", out _);
            return condition;
        }

        Assert.False(CalculateCondition());

        IOException CalculateInner()
        {
            var inner = new IOException("synthetic lost response");
            return inner;
        }

        await using var failing = new FakeProvider
        {
            ActionFailure = new RegistryActionExecutionException(true, CalculateInner()),
        };
        var (failExit, failStdout, _) = await CaptureJsonl(() => new TestCommands(failing).Sync(
            new(),
            new()
            {
                Profile = "synthetic-teacher",
            },
            new()
            {
                Jsonl = true,
            },
            new()
            {
                Apply = true,
            }));
        Assert.Equal(expected: 6, actual: failExit);
        string[] split = failStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var index = ^1;
        string json = split[index];
        var parse = JsonDocument.Parse(json);
        var failFinal = parse.RootElement;
        Assert.Equal(
            expected: "partial",
            actual: failFinal.GetProperty("status")
                .GetString());
    }

    [Fact]
    public async Task JsonRemainsSingleCompleteEnvelope()
    {
        await using var fake = new FakeProvider();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(
                expected: 0,
                actual: await new TestCommands(fake).Sync(
                    new(),
                    new()
                    {
                        Profile = "synthetic-teacher",
                    },
                    new()
                    {
                        Json = true,
                    },
                    new()));
            var text = stdout.ToString()
                .Trim();
            Assert.StartsWith(expectedStartString: "{", actualString: text);
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(
                expected: 1,
                actual: doc.RootElement.GetProperty("schemaVersion")
                    .GetInt32());
            Assert.Equal(
                expected: "registry sync",
                actual: doc.RootElement.GetProperty("command")
                    .GetString());
            Assert.Equal(
                expected: "preview",
                actual: doc.RootElement.GetProperty("status")
                    .GetString());
            Assert.Equal(
                expected: 3,
                actual: doc.RootElement.GetProperty("data")
                    .GetProperty("outcomes")
                    .GetArrayLength());
            // Plain --json keeps existing stderr format (human warning text, not JSONL).
            Assert.Contains(expectedSubstring: "positional", actualString: stderr.ToString());
            Assert.DoesNotContain(expectedSubstring: "\"event\":\"log\"", actualString: stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task ConcurrentWritesKeepPerStreamOrderedSequence()
    {
        var output = new CliOutput(
            command: "synthetic command",
            runId: "synthetic-run",
            json: false,
            jsonl: true);
        using var stdout = new SyncCapture();
        using var stderr = new SyncCapture();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            output.EmitStart();
            var writers = Enumerable.Range(start: 0, count: 8)
                .Select((worker) => Task.Run(() =>
                {
                    for (var i = 0; i < 50; i++)
                    {
                        string CalculateId()
                        {
                            string id2 = $"w{worker}-i{i}";
                            return id2;
                        }

                        output.EmitItem(
                            id: CalculateId(),
                            status: "completed",
                            new
                            {
                                worker,
                                i,
                            });

                        string CalculateMessage()
                        {
                            string message = $"synthetic warning w{worker} i{i}";
                            return message;
                        }

                        output.WriteWarning(CalculateMessage());
                    }
                }))
                .ToArray();
            await Task.WhenAll(writers);
            output.EmitFinal(
                "succeeded",
                0,
                warnings: [],
                errors: [],
                new { count = 400 });
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        // Allocation, serialization, write and flush hold one lock per record,
        // so file order matches sequence order on each independent stream.
        var outLines = stdout.Snapshot()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        int CalculateExpected()
        {
            int int32 = 1 + 400;
            int expected2 = int32 + 1;
            return expected2;
        }

        Assert.Equal(expected: CalculateExpected(), actual: outLines.Length);
        for (var i = 0; i < outLines.Length; i++)
        {
            using var doc = JsonDocument.Parse(outLines[i]);
            var root = doc.RootElement;
            Assert.Equal(
                expected: i,
                actual: root.GetProperty("seq")
                    .GetInt32());
            Assert.Equal(
                expected: "synthetic-run",
                actual: root.GetProperty("runId")
                    .GetString());
        }
        Assert.Equal(
            expected: "start",
            actual: JsonDocument.Parse(outLines[0]).RootElement
                .GetProperty("event")
                .GetString());
        Assert.Equal(
            expected: "final",
            actual: JsonDocument.Parse(outLines[^1]).RootElement
                .GetProperty("event")
                .GetString());
        var errLines = stderr.Snapshot()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected: 400, actual: errLines.Length);
        for (var i = 0; i < errLines.Length; i++)
        {
            using var doc = JsonDocument.Parse(errLines[i]);
            Assert.Equal(
                expected: i,
                actual: doc.RootElement.GetProperty("seq")
                    .GetInt32());
        }
    }

    [Fact]
    public void StructuredLogsPreserveContentLevelsPropertiesScopesExceptionAndFiltering()
    {
        var output = new CliOutput(
            command: "synthetic command",
            runId: "synthetic-run",
            json: false,
            jsonl: true);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            using var provider = new CliJsonlLoggerProvider(output);
            var logger = provider.CreateLogger("synthetic");
            logger.LogDebug("Synthetic debug {Value}", 1);
            using (logger.BeginScope("synthetic-scope"))
            {
                logger.LogInformation("Synthetic information {Value}", 42);
            }

            InvalidOperationException CalculateException()
            {
                var exception = new InvalidOperationException("synthetic failure");
                return exception;
            }

            logger.LogError(CalculateException(), "Synthetic error");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        Assert.Empty(stdout.ToString());
        // Debug never reached stderr before and stays filtered; the existing
        // Information/Error content, levels and occurrence are preserved.
        var lines = stderr.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected: 2, actual: lines.Length);
        using var info = JsonDocument.Parse(lines[0]);
        Assert.Equal(
            expected: "Information",
            actual: info.RootElement.GetProperty("level")
                .GetString());
        Assert.Equal(
            expected: "Synthetic information 42",
            actual: info.RootElement.GetProperty("message")
                .GetString());
        Assert.Equal(
            expected: 42,
            actual: info.RootElement.GetProperty("properties")
                .GetProperty("Value")
                .GetInt32());
        Assert.Contains(
            "synthetic-scope",
            info.RootElement.GetProperty("scopes")
                .EnumerateArray()
                .Select(x => x.GetString()));
        Assert.Equal(
            expected: "synthetic",
            actual: info.RootElement.GetProperty("category")
                .GetString());

        bool CalculateCondition()
        {
            if (!info.RootElement.TryGetProperty("timestamp", out var timestamp))
            {
                return false;
            }

            if (DateTimeOffset.TryParse(timestamp.GetString(), out _))
            {
                return true;
            }

            return false;
        }

        bool condition = CalculateCondition();


        Assert.True(
            condition);
        using var error = JsonDocument.Parse(lines[1]);
        Assert.Equal(
            expected: "Error",
            actual: error.RootElement.GetProperty("level")
                .GetString());
        Assert.Contains(
            expectedSubstring: "System.InvalidOperationException: synthetic failure",
            actualString: error.RootElement.GetProperty("exception")
                .GetString());
    }

    [Fact]
    public async Task ConcurrentLogScopesStayWithinTheirAsyncExecution()
    {
        using var stderr = new SyncCapture();
        var originalError = Console.Error;
        Console.SetError(stderr);
        try
        {
            CliOutput CalculateOutput()
            {
                var output = new CliOutput(
                    command: "synthetic",
                    runId: "scope-run",
                    json: false,
                    jsonl: true);
                return output;
            }

            using var provider = new CliJsonlLoggerProvider(
                CalculateOutput());
            var logger = provider.CreateLogger("synthetic");
            using var parent = logger.BeginScope(new Dictionary<string, object?>
            {
                ["Parent"] = "shared",
            });
            var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            await Task.WhenAll(
                Enumerable.Range(start: 0, count: 2)
                    .Select(index => Task.Run(async () =>
                    {
                        using var child = logger.BeginScope("child-" + index);
                        bool conditionResult;
                        int conditionValue = Interlocked.Increment(ref count);
                        conditionResult = conditionValue == 2;
                        if (conditionResult)
                        {
                            bothStarted.SetResult();
                        }

                        await bothStarted.Task;
                        logger.LogInformation("Child {Index}", index);
                    })));
            logger.LogInformation("Parent");
        }
        finally
        {
            Console.SetError(originalError);
        }
        var lines = stderr.Snapshot()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected: 3, actual: lines.Length);
        foreach (var line in lines.Take(2))
        {
            using var record = JsonDocument.Parse(line);
            var properties = record.RootElement.GetProperty("properties");
            Assert.Equal(
                expected: "shared",
                actual: properties.GetProperty("Parent")
                    .GetString());
            var scopes = record.RootElement.GetProperty("scopes")
                .EnumerateArray()
                .Select(scope => scope.GetString())
                .ToArray();
            Assert.Equal(expected: 2, actual: scopes.Length);
            Assert.Contains(
                "child-"
                + properties.GetProperty("Index")
                    .GetInt32(),
                scopes);
        }
        using var last = JsonDocument.Parse(lines[^1]);
        Assert.Single(last.RootElement.GetProperty("scopes")
            .EnumerateArray());
    }

    [Fact]
    public async Task HumanProfileListingEndsWithSummary()
    {
        using var isolation = new XdgIsolation();
        var originalOut = Console.Out;
        using var stdout = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Assert.Equal(expected: 0, actual: await new ConfigCommands().Profiles(new(), new()));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        var lines = stdout.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        int int32 = lines.Length - 1;
        string expected2 = $"succeeded: {int32} profiles.";
        string actual2 = lines[^1].TrimEnd();
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Single(lines, line => line.StartsWith("succeeded:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryLessonsPartialCancellationKeepsStreamedCount()
    {
        using var cancelled = new CancellationTokenSource();
        using var stdout = new CancellingWriter(cancelled, 3);
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        int exit;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exit = await new FixtureQuery().Lessons(
                new(),
                new()
                {
                    NoCache = true,
                    DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                },
                new()
                {
                    Jsonl = true,
                },
                cancelled.Token);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        Assert.Equal(expected: 130, actual: exit);
        var lines = stdout.Snapshot()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1)
            .Take(lines.Length - 2)
            .ToArray();
        var final = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal(
            expected: "cancelled",
            actual: final.GetProperty("status")
                .GetString());
        Assert.Equal(
            expected: 130,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        // Counts persist accurately: the summary count matches emitted rows,
        // never zero when rows already streamed and never the full set.
        Assert.Equal(
            expected: items.Length,
            actual: final.GetProperty("data")
                .GetProperty("count")
                .GetInt32());
        Assert.DoesNotContain(
            "lessons",
            final.GetProperty("data")
                .EnumerateObject()
                .Select(x => x.Name));
    }

    [Fact]
    public async Task CalendarCancellationReportsRemainingActionsWithoutChangingLegacyOutcomes()
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = new CalendarFake();
        using var root = new TempRoot();
        var updates = new List<CalendarReport>();
        var result = await CalendarSync.Run(
            provider,
            name: "Lessons",
            [
                new Event
                {
                    Summary = "Desired",
                },
            ],
            true,
            cancellation.Token,
            lockRoot: root.Path,
            onOutcome: update =>
            {
                updates.Add(update);

                bool CalculateConditionResult()
                {
                    if (update.Id != "0")
                    {
                        return false;
                    }

                    if (update.Status == "completed")
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                if (conditionResult)
                {
                    cancellation.Cancel();
                }
            });
        Assert.Equal(expected: 130, actual: result.ExitCode);
        Assert.Single(result.Outcomes);
        Assert.Equal(expected: "completed", actual: result.Outcomes[0].State);
        Assert.Equal(expected: ["delete old"], actual: provider.Writes);
        Assert.Equal(
            expected: ["1", "2"],
            actual: updates.Where(update => update.Status == "not-attempted")
                .Select(update => update.Id));
    }

    [Fact]
    public async Task CalendarPlanIdsAreStableAcrossPreviewApplyAndTransitions()
    {
        var desired = new[]
        {
            new Event
            {
                Summary = "Desired A",
            },
            new Event
            {
                Summary = "Desired B",
            },
        };
        var previewUpdates = new List<CalendarReport>();
        using var previewProvider = new CalendarFake();
        using var previewLock = new TempRoot();
        var preview = await CalendarSync.Run(
            previewProvider,
            name: "Lessons",
            desired,
            false,
            default,
            lockRoot: previewLock.Path,
            onOutcome: (update) => previewUpdates.Add(update));
        Assert.Empty(preview.Outcomes);
        Assert.Empty(previewProvider.Writes);
        // Planned action ids are plan-order indexes; the read selection
        // streams as items with the deleted/desired payloads.
        Assert.Equal(
            expected: ["0", "1", "2", "3"],
            actual: previewUpdates.Where((update) => update.Status == "planned")
                .Select((update) => update.Id));
        Assert.Equal(expected: "planned", actual: previewUpdates.Single((update) => update.Id == "0").Status);
        var deleted = previewUpdates.Single((update) => update.Id == "deleted-0");

        string CalculateJson()
        {
            string json = JsonSerializer.Serialize(deleted.Item, JsonOptions);
            return json;
        }

        Assert.Equal(
            expected: "Old",
            actual: JsonDocument.Parse(CalculateJson()).RootElement
                .GetProperty("summary")
                .GetString());
        Assert.Equal(
            expected: ["Desired A", "Desired B"],
            actual: previewUpdates
                .Where((update) => update.Id.StartsWith("desired-", StringComparison.Ordinal))
                .OrderBy((update) => update.Id)
                .Select((update) =>
                {
                    string CalculateJson()
                    {
                        string json = JsonSerializer.Serialize(update.Item, JsonOptions);
                        return json;
                    }

                    return JsonDocument.Parse(CalculateJson()).RootElement
                        .GetProperty("summary")
                        .GetString();
                }));

        var applyUpdates = new List<CalendarReport>();
        using var applyProvider = new CalendarFake();
        using var applyLock = new TempRoot();
        var applied = await CalendarSync.Run(
            applyProvider,
            name: "Lessons",
            desired,
            true,
            default,
            lockRoot: applyLock.Path,
            onOutcome: (update) => applyUpdates.Add(update));
        Assert.Equal(expected: 0, actual: applied.ExitCode);
        Assert.Equal(expected: ["delete old", "calendar", "event new", "event new"], actual: applyProvider.Writes);
        Assert.All(applied.Outcomes, (outcome) => Assert.Equal(expected: "completed", actual: outcome.State));
        // Each action id transitions from planned to completed with the same id.
        foreach (var id in new[]
                 {
                     "0",
                     "1",
                     "2",
                     "3",
                 })
        {
            Assert.Equal(
                expected: ["planned", "completed"],
                actual: applyUpdates.Where((update) => update.Id == id)
                    .Select((update) => update.Status));
        }

        Assert.Equal(expected: "new", actual: applied.Outcomes[1].CalendarId);
        Assert.Equal(expected: "event-1", actual: applied.Outcomes[2].EventId);
        Assert.Equal(expected: 0, actual: applied.Outcomes[2].DesiredEventIndex);
    }

    [Fact]
    public async Task CalendarThrowingCallbackPreservesCompletedOutcomeWithoutRetry()
    {
        using var provider = new CalendarFake();
        using var calendarLock = new TempRoot();
        var calls = 0;
        var failure = Assert.Throws<CalendarStreamingException>(() => CalendarSync.Run(
                provider,
                name: "Lessons",
                [
                    new Event
                    {
                        Summary = "Desired",
                    },
                ],
                true,
                default,
                lockRoot: calendarLock.Path,
                onOutcome: (_) =>
                {
                    // Fail on the first terminal transition (after 3 planned plus
                    // 2 selection reports): the delete already completed remotely.
                    bool conditionResult;
                    int conditionValue = ++calls;
                    conditionResult = conditionValue == 6;
                    if (conditionResult)
                    {
                        throw new InvalidOperationException("synthetic render failure");
                    }
                })
            .GetAwaiter()
            .GetResult());
        Assert.IsAssignableFrom<CliOutputStreamingException>(failure);
        // Rendering stopped further mutations without retrying or reclassifying.
        Assert.Equal(expected: ["delete old"], actual: provider.Writes);
        var outcome = Assert.Single(failure.PartialResult.Outcomes);
        Assert.Equal(expected: "completed", actual: outcome.State);
        Assert.Equal(expected: "old", actual: outcome.CalendarId);
        Assert.Equal(expected: 6, actual: failure.PartialResult.ExitCode);
    }

    [Fact]
    public async Task DrivePlanIdsAreStableAcrossPreviewApplyAndTransitions()
    {
        using var root = new TempRoot();

        string CalculatePath()
        {
            string path = Path.Combine(root.Path, "a.pdf");
            return path;
        }

        var artifacts = new[]
        {
            new DriveArtifact(Name: "a.pdf", Path: CalculatePath()),
        };
        var previewUpdates = new List<StreamedUpdate>();
        using var previewProvider = new DriveFake();
        var preview = await DriveSync.Run(
            previewProvider,
            folderName: "Configured",
            artifacts,
            false,
            default,
            lockRoot: root.Path,
            onOutcome: (update) => previewUpdates.Add(update));
        Assert.Empty(preview.Outcomes);
        Assert.Empty(previewProvider.Writes);
        Assert.Equal(expected: ["0", "1"], actual: previewUpdates.Select((update) => update.Id));
        Assert.All(previewUpdates, (update) => Assert.Equal(expected: "planned", actual: update.Status));
        Assert.Equal(expected: "delete", actual: ((DriveOutcome) previewUpdates[0].Item!).Action.Action);
        Assert.Equal(expected: "create", actual: ((DriveOutcome) previewUpdates[1].Item!).Action.Action);

        var applyUpdates = new List<StreamedUpdate>();
        using var applyProvider = new DriveFake();

        string CalculatePath2()
        {
            string path2 = Path.Combine(root.Path, "a.pdf");
            return path2;
        }

        await using var created = File.Create(CalculatePath2());
        await created.WriteAsync("fixture bytes"u8.ToArray());
        await created.FlushAsync();
        await created.DisposeAsync();
        var applied = await DriveSync.Run(
            applyProvider,
            folderName: "Configured",
            artifacts,
            true,
            default,
            lockRoot: root.Path,
            onOutcome: (update) => applyUpdates.Add(update));
        Assert.Equal(expected: 0, actual: applied.ExitCode);
        Assert.Equal(expected: ["delete old", "create a.pdf"], actual: applyProvider.Writes);
        foreach (var id in new[]
                 {
                     "0",
                     "1",
                 })
        {
            Assert.Equal(
                expected: ["planned", "completed"],
                actual: applyUpdates.Where((update) => update.Id == id)
                    .Select((update) => update.Status));
        }
    }

    [Fact]
    public async Task DriveThrowingCallbackPreservesCompletedOutcomeWithoutRetry()
    {
        using var root = new TempRoot();
        using var provider = new DriveFake();

        string CalculatePath()
        {
            string path = Path.Combine(root.Path, "a.pdf");
            return path;
        }

        var artifacts = new[]
        {
            new DriveArtifact(Name: "a.pdf", Path: CalculatePath()),
        };
        var calls = 0;
        var failure = await Assert.ThrowsAsync<DriveStreamingException>(() => DriveSync.Run(
            provider,
            folderName: "Configured",
            artifacts,
            true,
            default,
            lockRoot: root.Path,
            onOutcome: (_) =>
            {
                // Fail on the first terminal transition (after 2 planned
                // reports): the delete already completed remotely.
                bool conditionResult;
                int conditionValue = ++calls;
                conditionResult = conditionValue == 3;
                if (conditionResult)
                {
                    throw new InvalidOperationException("synthetic render failure");
                }
            }));
        Assert.IsAssignableFrom<CliOutputStreamingException>(failure);
        Assert.Equal(expected: ["delete old"], actual: provider.Writes);
        var outcome = Assert.Single(failure.PartialResult.Outcomes);
        Assert.Equal(expected: "completed", actual: outcome.State);
        Assert.Equal(expected: "old", actual: outcome.ResultFileId);
        Assert.Equal(expected: 6, actual: failure.PartialResult.ExitCode);
    }

    [Fact]
    public async Task RegistryApplyStopsAfterRenderingFailureWithPartialCounts()
    {
        await using var fake = new FakeProvider();
        var commands = new TestCommands(fake);
        // Fail exactly one console write mid-apply, then recover so the final
        // summary still renders and the exit code survives.
        using var stdout = new FlakyWriter(6);
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        int exit;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exit = await commands.Sync(
                new(),
                new()
                {
                    Profile = "synthetic-teacher",
                },
                new()
                {
                    Jsonl = true,
                },
                new()
                {
                    Apply = true,
                });
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        Assert.Equal(expected: 6, actual: exit);
        Assert.Equal(expected: [0, 1], actual: fake.Executed.OrderBy((index) => index));
        var lines = stdout.Snapshot()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var final = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal(
            expected: "partial",
            actual: final.GetProperty("status")
                .GetString());
        Assert.Equal(
            expected: 6,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        Assert.Contains(
            expectedSubstring: "Output streaming failed",
            actualString: final.GetProperty("errors")
                .EnumerateArray()
                .First()
                .GetString());
        var data = final.GetProperty("data");
        Assert.Equal(
            expected: 3,
            actual: data.GetProperty("total")
                .GetInt32());
        // Both executed actions keep their completed remote outcomes even
        // though the second one's rendering failed; nothing was retried.
        Assert.Equal(
            expected: 2,
            actual: data.GetProperty("counts")
                .GetProperty("completed")
                .GetInt32());

        bool CalculateCondition()
        {
            bool condition = final.TryGetProperty("outcomes", out _);
            return condition;
        }

        Assert.False(CalculateCondition());
    }

    [Fact]
    public async Task FinalSummariesUseCountsWithoutStreamedArrays()
    {
        await using var fake = new FakeProvider();
        var (exit, stdout, _) = await CaptureJsonl(() => new TestCommands(fake).Sync(
            new(),
            new()
            {
                Profile = "synthetic-teacher",
            },
            new()
            {
                Jsonl = true,
            },
            new()));
        Assert.Equal(expected: 0, actual: exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal(
            expected: 3,
            actual: final.GetProperty("data")
                .GetProperty("total")
                .GetInt32());
        Assert.Equal(
            expected: 3,
            actual: final.GetProperty("data")
                .GetProperty("counts")
                .GetProperty("planned")
                .GetInt32());
        Assert.DoesNotContain(expectedSubstring: "\"outcomes\"", actualString: finalLine);

        var (queryExit, queryStdout, _) = await CaptureJsonl(() => new FixtureQuery().Lessons(
            new(),
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            new()
            {
                Jsonl = true,
            },
            default));
        Assert.Equal(expected: 0, actual: queryExit);
        var queryLines = queryStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var queryFinal = JsonDocument.Parse(queryLines[^1]).RootElement;
        Assert.Equal(
            expected: queryLines.Length - 2,
            actual: queryFinal.GetProperty("data")
                .GetProperty("count")
                .GetInt32());
        Assert.Single(queryFinal.GetProperty("data")
            .EnumerateObject());

        var historyDirectory = NewHistoryDirectory();
        try
        {
            var (historyExit, historyStdout, _) = await CaptureJsonl(() =>
                new HistoryCommands(new HistoryProvider()).History(
                    new()
                    {
                        Profile = "synthetic",
                    },
                    new()
                    {
                        LocalDirectory = historyDirectory,
                    },
                    new()
                    {
                        Jsonl = true,
                    }));
            Assert.Equal(expected: 0, actual: historyExit);
            var historyLines = historyStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            int CalculateExpected()
            {
                int int32 = 1 + 1;
                int expected2 = int32 + 1;
                return expected2;
            }

            Assert.Equal(expected: CalculateExpected(), actual: historyLines.Length);
            var historyFinal = JsonDocument.Parse(historyLines[^1]).RootElement;
            Assert.Equal(
                expected: 1,
                actual: historyFinal.GetProperty("data")
                    .GetProperty("years")
                    .GetInt32());
            Assert.Equal(
                expected: ["courses", "groups", "years"],
                actual: historyFinal.GetProperty("data")
                    .EnumerateObject()
                    .Select((property) => property.Name)
                    .OrderBy((name) => name));
            Assert.DoesNotContain(expectedSubstring: "private-course", actualString: historyStdout);
        }
        finally
        {
            string? historyRoot = Path.GetDirectoryName(historyDirectory);
            Directory.Delete(historyRoot!, recursive: true);
        }
    }

    [Fact]
    public async Task CalendarCommandPreviewStreamsSelectionWithScalarFinal()
    {
        using var fake = new CalendarFake();
        var (exit, stdout, _) = await CaptureJsonl(() => new FixtureCalendar(fake).Sync(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            new()
            {
                Profile = "Nartea Nichita",
            },
            new()
            {
                Jsonl = true,
            },
            new()));
        Assert.Equal(expected: 0, actual: exit);
        Assert.Empty(fake.Writes);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1)
            .Take(lines.Length - 2)
            .Select((line) => JsonDocument.Parse(line).RootElement)
            .ToArray();
        // Planned action ids are plan-order indexes shared with apply mode.
        // (The fixture's configured calendar name differs from the fake's
        // existing calendar, so this preview has no delete step; the
        // sync-layer test above covers deleted-event selection items.)
        var planned = items.Where((item) => item.GetProperty("status")
                    .GetString()
                == "planned")
            .ToArray();
        Assert.Equal(
            expected: Enumerable.Range(start: 0, count: planned.Length)
                .Select((index) =>
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            actual: planned.Select((item) => item.GetProperty("id")
                .GetString()));
        Assert.Contains(
            items,
            (item) => item.GetProperty("id")
                    .GetString()
                    ?.StartsWith("desired-", StringComparison.Ordinal)
                == true);
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal(
            expected: "preview",
            actual: final.GetProperty("status")
                .GetString());
        Assert.True(final.GetProperty("data")
                .GetProperty("desiredCount")
                .GetInt32()
            > 0);
        Assert.DoesNotContain(expectedSubstring: "\"desiredEvents\"", actualString: finalLine);
        Assert.DoesNotContain(expectedSubstring: "\"deletedEvents\"", actualString: finalLine);
        Assert.DoesNotContain(expectedSubstring: "\"outcomes\"", actualString: finalLine);
    }

    [Fact]
    public async Task DriveCommandPreviewStreamsPlanWithScalarFinal()
    {
        using var output = new TempRoot();
        using var fake = new DriveFake();
        var (exit, stdout, _) = await CaptureJsonl(() => new FixtureDrive(fake).Publish(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            new()
            {
                Profile = "Curmanschii Anton",
            },
            new()
            {
                Directory = output.Path,
            },
            new()
            {
                Jsonl = true,
            },
            new()));
        Assert.Equal(expected: 0, actual: exit);
        Assert.Empty(fake.Writes);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1)
            .Take(lines.Length - 2)
            .Select((line) => JsonDocument.Parse(line).RootElement)
            .ToArray();
        Assert.Contains(
            items,
            (item) => item.GetProperty("id")
                    .GetString()
                    ?.StartsWith("artifact:", StringComparison.Ordinal)
                == true);
        var planned = items.Where((item) => item.GetProperty("status")
                    .GetString()
                == "planned")
            .ToArray();
        Assert.NotEmpty(planned);
        Assert.Equal(
            expected: Enumerable.Range(start: 0, count: planned.Length)
                .Select((index) =>
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            actual: planned.Select((item) => item.GetProperty("id")
                .GetString()));
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal(
            expected: "preview",
            actual: final.GetProperty("status")
                .GetString());
        Assert.Equal(
            expected: planned.Length,
            actual: final.GetProperty("data")
                .GetProperty("actionCount")
                .GetInt32());
        Assert.DoesNotContain(expectedSubstring: "\"actions\"", actualString: finalLine);
        Assert.DoesNotContain(expectedSubstring: "\"outcomes\"", actualString: finalLine);
    }

    [Fact]
    public async Task ConfigProfilesAndAuthStatusStreamCollectionsWithScalarFinals()
    {
        using var isolation = new XdgIsolation();
        var (exit, stdout, _) = await CaptureJsonl(() => new ConfigCommands().Profiles(
            new(),
            new()
            {
                Jsonl = true,
            }));
        Assert.Equal(expected: 0, actual: exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1)
            .Take(lines.Length - 2)
            .ToArray();
        Assert.NotEmpty(items);
        foreach (var line in items)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(
                expected: "completed",
                actual: doc.RootElement.GetProperty("status")
                    .GetString());
            Assert.NotEmpty(doc.RootElement.GetProperty("item")
                .GetProperty("profile")
                .GetString()!);
        }
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal(
            expected: items.Length,
            actual: final.GetProperty("data")
                .GetProperty("profileCount")
                .GetInt32());
        Assert.DoesNotContain(expectedSubstring: "\"profiles\"", actualString: finalLine);

        var (authExit, authStdout, _) = await CaptureJsonl(() => new AuthCommands().Status(
            new()
            {
                Provider = AuthProvider.google,
            },
            new()
            {
                Profile = "Curmanschii Anton",
            },
            new()
            {
                Jsonl = true,
            }));
        Assert.Equal(expected: 0, actual: authExit);
        var authLines = authStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var authFinal = JsonDocument.Parse(authLines[^1]).RootElement;
        Assert.Equal(
            expected: "succeeded",
            actual: authFinal.GetProperty("status")
                .GetString());
        Assert.Equal(
            expected: 0,
            actual: authFinal.GetProperty("data")
                .GetProperty("accountCount")
                .GetInt32());
        Assert.False(authFinal.GetProperty("data")
            .GetProperty("provisioned")
            .GetBoolean());
        Assert.DoesNotContain(expectedSubstring: "\"accounts\"", actualString: authLines[^1]);
    }

    [Fact]
    public async Task HistoryTopicsStreamsCollectionsWithScalarFinal()
    {
        var directory = NewHistoryDirectory();
        try
        {
            var (exit, stdout, _) = await CaptureJsonl(() =>
                new HistoryTopicsCommands(new HistoryTopicsProvider()).HistoryTopics(
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
                        Jsonl = true,
                    }));
            Assert.Equal(expected: 0, actual: exit);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var items = lines.Skip(1)
                .Take(lines.Length - 2)
                .Select((line) => JsonDocument.Parse(line).RootElement)
                .ToArray();
            Assert.Equal(
                expected: 2,
                actual: items.Count((item) =>
                    item.GetProperty("id")
                        .GetString()
                        ?.StartsWith("year-", StringComparison.Ordinal)
                    == true));
            Assert.Equal(
                expected: 1,
                actual: items.Count((item) =>
                    item.GetProperty("id")
                        .GetString()
                        ?.StartsWith("candidate-", StringComparison.Ordinal)
                    == true));
            var final = JsonDocument.Parse(lines[^1]).RootElement;
            Assert.Equal(
                expected: 1,
                actual: final.GetProperty("data")
                    .GetProperty("courses")
                    .GetInt32());
            Assert.Equal(
                expected: 1,
                actual: final.GetProperty("data")
                    .GetProperty("candidates")
                    .GetInt32());
            Assert.Equal(
                expected: 0,
                actual: final.GetProperty("data")
                    .GetProperty("unresolved")
                    .GetInt32());
            Assert.DoesNotContain(expectedSubstring: "canary-course", actualString: stdout);
            Assert.DoesNotContain(expectedSubstring: "canary-group", actualString: stdout);
            Assert.DoesNotContain(expectedSubstring: "canary-historical-topic", actualString: stdout);
        }
        finally
        {
            string? root = Path.GetDirectoryName(directory);
            Directory.Delete(root!, recursive: true);
        }
    }

    [Fact]
    public async Task HumanSummariesDoNotDuplicateStreamedItems()
    {
        await using var fake = new FakeProvider();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(
                expected: 0,
                actual: await new TestCommands(fake).Sync(
                    new(),
                    new()
                    {
                        Profile = "synthetic-teacher",
                    },
                    new(),
                    new()));
            var lines = stdout.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(expected: 3, actual: lines.Count((line) => line.Contains(": planned")));
            Assert.Single(lines, (line) => line.StartsWith("preview:", StringComparison.Ordinal));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task EveryCommandRejectsJsonTogetherWithJsonl()
    {
        var both = new ResultArguments
        {
            Json = true,
            Jsonl = true,
        };
        var cases = new Case[]
        {
            new(
                "query lessons",
                () => new QueryCommands().Lessons(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "query free-hours",
                () => new QueryCommands().FreeHours(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "export teachers-excel",
                () => new ExportCommands().TeachersExcel(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "export free-rooms",
                () => new ExportCommands().FreeRooms(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "export pdf",
                () => new ExportCommands().Pdf(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "export ics",
                () => new ExportCommands().Ics(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "export lab-deadlines",
                () => new ExportCommands().LabDeadlines(
                    new(),
                    new(),
                    both,
                    new(),
                    default)),
            new(
                "export pre-defense",
                () => new ExportCommands().PreDefense(
                    new(),
                    both,
                    new(),
                    default)),
            new(
                "export website-schedules",
                () => new ExportCommands().WebsiteSchedules(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "export website-theses",
                () => new ExportCommands().WebsiteTheses(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "curricula download",
                () => new CurriculaCommands().Download(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "config show",
                () => new ConfigCommands().Show(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "config get",
                () => new ConfigCommands().Get(
                    new()
                    {
                        Key = "key",
                    },
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "config validate",
                () => new ConfigCommands().Validate(
                    new(),
                    both,
                    default)),
            new(
                "config profiles",
                () => new ConfigCommands().Profiles(
                    new(),
                    both,
                    default)),
            new(
                "config set",
                () => new ConfigCommands().Set(
                    new()
                    {
                        Key = "key",
                        Value = "1",
                    },
                    new()
                    {
                        Scope = "user",
                    },
                    new(),
                    both,
                    default)),
            new(
                "config unset",
                () => new ConfigCommands().Unset(
                    new()
                    {
                        Key = "key",
                    },
                    new()
                    {
                        Scope = "user",
                    },
                    new(),
                    both,
                    default)),
            new(
                "auth login google",
                () => new AuthCommands().Login(
                    new()
                    {
                        Provider = AuthProvider.google,
                    },
                    new(),
                    both,
                    default)),
            new(
                "auth status google",
                () => new AuthCommands().Status(
                    new()
                    {
                        Provider = AuthProvider.google,
                    },
                    new(),
                    both,
                    default)),
            new(
                "auth logout google",
                () => new AuthCommands().Logout(
                    new()
                    {
                        Provider = AuthProvider.google,
                    },
                    new(),
                    both,
                    default)),
            new(
                "auth login microsoft",
                () => new AuthCommands().Login(
                    new()
                    {
                        Provider = AuthProvider.microsoft,
                    },
                    new(),
                    both,
                    default)),
            new(
                "auth status microsoft",
                () => new AuthCommands().Status(
                    new()
                    {
                        Provider = AuthProvider.microsoft,
                    },
                    new(),
                    both,
                    default)),
            new(
                "auth logout microsoft",
                () => new AuthCommands().Logout(
                    new()
                    {
                        Provider = AuthProvider.microsoft,
                    },
                    new(),
                    both,
                    default)),
            new(
                "calendar sync",
                () => new CalendarCommands().Sync(
                    new(),
                    new(),
                    both,
                    new(),
                    default)),
            new(
                "drive publish",
                () => new DriveCommands().Publish(
                    new(),
                    new(),
                    new(),
                    both,
                    new(),
                    default)),
            new(
                "registry sync",
                () => new RegistryCommands().Sync(
                    new(),
                    new(),
                    both,
                    new(),
                    default)),
            new(
                "registry import-grades",
                () => new RegistryCommands().ImportGrades(
                    new()
                    {
                        QuizId = "1",
                    },
                    new(),
                    new(),
                    both,
                    new(),
                    default)),
            new(
                "registry history",
                () => new RegistryCommands().History(
                    new(),
                    new(),
                    both,
                    default)),
            new(
                "registry history-topics",
                () => new RegistryCommands().HistoryTopics(
                    new(),
                    new(),
                    both,
                    default)),
        };
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            foreach (var (name, run) in cases)
            {
                stdout.GetStringBuilder()
                    .Clear();
                stderr.GetStringBuilder()
                    .Clear();
                Assert.Equal(expected: 2, actual: await run());

                bool CalculateCondition()
                {
                    bool condition = string.IsNullOrWhiteSpace(stdout.ToString());
                    return condition;
                }

                bool condition2 = CalculateCondition();
                string userMessage = $"stdout must stay empty for {name}.";
                Assert.True(condition2, userMessage);
                Assert.Contains(expectedSubstring: "Only one of --json and --jsonl", actualString: stderr.ToString());
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static string NewTempRoot()
    {
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid()
                .ToString("N");
            return path2;
        }

        var path = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NewHistoryDirectory()
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
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = NewTempRoot();

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FixtureQuery : QueryCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                var scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
        }
    }

    private sealed class FixtureCalendar(CalendarFake provider) : CalendarCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                var scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
        }

        protected override Task<ICalendarSyncProvider> Connect(
            IServiceProvider services,
            BuiltGoogleCalendarConfig config,
            CancellationToken token)
            => Task.FromResult<ICalendarSyncProvider>(provider);
    }

    private sealed class FixtureDrive(DriveFake provider) : DriveCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                var scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
        }

        protected override Task<IDriveSyncProvider> Connect(
            IServiceProvider services,
            BuiltGoogleDriveConfig config,
            CancellationToken token)
            => Task.FromResult<IDriveSyncProvider>(provider);
    }

    private sealed class HistoryCommands(HistoryProvider provider) : RegistryCommands
    {
        protected override IRegistryHistoryProvider CreateHistoryProvider() => provider;
    }

    private sealed class HistoryProvider : IRegistryHistoryProvider
    {
        public Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(
            SettingsArguments settings,
            string? year,
            CancellationToken token)
        {
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

    private sealed class HistoryTopicsCommands(HistoryTopicsProvider provider) : RegistryCommands
    {
        protected override IRegistryHistoryTopicsProvider CreateHistoryTopicsProvider() => provider;
    }

    private sealed class HistoryTopicsProvider : IRegistryHistoryTopicsProvider
    {
        public Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token)
        {
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
                        LessonType.Curs,
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
                        LessonType.Curs,
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

    private sealed class CalendarFake : ICalendarSyncProvider
    {
        public List<string> Writes { get; } = [];

        private int _events;
        public Task<string> GetAccount(CancellationToken token) => Task.FromResult("actual@example.test");

        public Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token)
        {
            return Task.FromResult<IReadOnlyList<CalendarDestination>>(
            [
                new(
                    Id: "old",
                    Summary: "Lessons",
                    false),
            ]);
        }

        public Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string id, CancellationToken token)
            => Task.FromResult<IReadOnlyList<CalendarEventIdentity>>([new(Id: "old-event", Summary: "Old")]);

        public Task DeleteCalendar(string id, CancellationToken token)
        {
            string CalculateItem()
            {
                string text = $"delete {id}";
                string item = text;
                return item;
            }

            Writes.Add(CalculateItem());
            return Task.CompletedTask;
        }

        public Task<string> CreateCalendar(string summary, CancellationToken token)
        {
            Writes.Add("calendar");
            return Task.FromResult("new");
        }

        public Task<string> CreateEvent(
            string id,
            Event desired,
            CancellationToken token)
        {
            string CalculateItem()
            {
                string text = $"event {id}";
                string item = text;
                return item;
            }

            Writes.Add(CalculateItem());
            return Task.FromResult($"event-{(++_events).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        public void Dispose()
        {
        }
    }

    private sealed class DriveFake : IDriveSyncProvider
    {
        public List<string> Writes { get; } = [];

        public Task<DriveAccount> GetAccount(CancellationToken token)
        {
            DriveAccount CalculateResult()
            {
                var result = new DriveAccount(Id: "actual-account", Email: "actual@example.test");
                return result;
            }

            return Task.FromResult(CalculateResult());
        }

        public Task<DriveDestination> FindFolder(string name, CancellationToken token)
        {
            DriveDestination CalculateResult()
            {
                var result = new DriveDestination(Id: "actual-folder", Name: "Actual folder");
                return result;
            }

            return Task.FromResult(CalculateResult());
        }

        public Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string id, CancellationToken token)
            => Task.FromResult<IReadOnlyList<DriveRemoteFile>>([new(Id: "old", Name: "obsolete.txt")]);

        public Task Delete(string id, CancellationToken token)
        {
            string CalculateItem()
            {
                string text = $"delete {id}";
                string item = text;
                return item;
            }

            Writes.Add(CalculateItem());
            return Task.CompletedTask;
        }

        public Task<string> Create(
            string folder,
            string name,
            Stream input,
            CancellationToken token)
        {
            string CalculateItem()
            {
                string text = $"create {name}";
                string item = text;
                return item;
            }

            Writes.Add(CalculateItem());
            return Task.FromResult("created");
        }

        public Task<string> Update(
            string id,
            Stream input,
            CancellationToken token)
        {
            string CalculateItem()
            {
                string text = $"update {id}";
                string item = text;
                return item;
            }

            Writes.Add(CalculateItem());
            return Task.FromResult(id);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CancellingWriter(CancellationTokenSource source, int cancelAfter) : StringWriter
    {
        private int _lines;

        public string Snapshot()
        {
            lock (this)
            {
                return GetStringBuilder()
                    .ToString();
            }
        }

        public override void WriteLine(string? value)
        {
            lock (this)
            {
                base.WriteLine(value);
            }

            bool CheckCondition()
            {
                bool conditionResult;
                int conditionValue = Interlocked.Increment(ref _lines);
                conditionResult = conditionValue == cancelAfter;
                return conditionResult;
            }

            if (CheckCondition())
            {
                source.Cancel();
            }
        }
    }

    private sealed class FlakyWriter(params int[] failOn) : StringWriter
    {
        private readonly HashSet<int> _failOn = failOn.ToHashSet();
        private int _lines;

        public string Snapshot()
        {
            return GetStringBuilder()
                .ToString();
        }

        public override void WriteLine(string? value)
        {
            var hashSet = _failOn;
            int item = Interlocked.Increment(ref _lines);
            if (hashSet.Contains(item))
            {
                throw new InvalidOperationException("synthetic console failure");
            }

            base.WriteLine(value);
        }
    }

    private sealed class XdgIsolation : IDisposable
    {
        private readonly string? _previousConfig;
        private readonly string? _previousData;
        private readonly string _previousDirectory;

        public XdgIsolation()
        {
            _previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            _previousData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            _previousDirectory = Environment.CurrentDirectory;

            string CalculatePath2()
            {
                string path2 = Guid.NewGuid()
                    .ToString("N");
                return path2;
            }

            var root = Path.Combine(Path.GetTempPath(), CalculatePath2());
            string path = Path.Combine(root, "config");
            Directory.CreateDirectory(path);
            string path3 = Path.Combine(root, "data");
            Directory.CreateDirectory(path3);
            string path4 = Path.Combine(root, "work");
            Directory.CreateDirectory(path4);
            Root = root;
            string variable2 = "XDG_CONFIG_HOME";
            string value2 = Path.Combine(root, "config");
            Environment.SetEnvironmentVariable(variable: variable2, value: value2);
            string variable3 = "XDG_DATA_HOME";
            string value3 = Path.Combine(root, "data");
            Environment.SetEnvironmentVariable(variable: variable3, value: value3);
            Environment.CurrentDirectory = Path.Combine(root, "work");
        }

        public string Root { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(variable: "XDG_CONFIG_HOME", value: _previousConfig);
            Environment.SetEnvironmentVariable(variable: "XDG_DATA_HOME", value: _previousData);
            Environment.CurrentDirectory = _previousDirectory;
            Directory.Delete(Root, recursive: true);
        }
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> CaptureJsonl(Func<Task<int>> run)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await run();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static async Task<string> WaitFor(
        Func<string> snapshot,
        Func<string, bool> ready,
        TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            var current = snapshot();
            if (ready(current))
            {
                return current;
            }

            await Task.Delay(20);
        }
        return snapshot();
    }

    private sealed class TestCommands(IRegistryProvider provider) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => provider;
        protected override IRegistryAccountLock CreateAccountLock() => new FakeLock();
    }

    private sealed class TestGradeCommands(IRegistryGradePlanner planner) : RegistryCommands
    {
        private sealed class Session : IRegistrySession
        {
            public RegistryDestination Target { get; } =
                new(Account: "synthetic-account", Destination: "https://registry.test/");

            public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) =>
                Task.FromResult<IReadOnlyList<RegistrySyncAction>>([]);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class Provider : IRegistryProvider
        {
            public Task<IRegistrySession> Open(
                SourceArguments source,
                SettingsArguments settings,
                CancellationToken token)
                => Task.FromResult<IRegistrySession>(new Session());
        }

        protected override IRegistryProvider CreateProvider() => new Provider();
        protected override IRegistryGradePlanner CreateGradePlanner() => planner;
        protected override IRegistryAccountLock CreateAccountLock() => new FakeLock();
    }

    private sealed class FakeLock : IRegistryAccountLock, IAsyncDisposable
    {
        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token) =>
            Task.FromResult<IAsyncDisposable>(this);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeProvider : IRegistryProvider, IRegistrySession
    {
        public Task? BlockSecond;
        public bool CancelSecond;
        public Exception? ActionFailure;

        public List<int> Executed { get; } = [];

        public RegistryDestination Target { get; } =
            new(Account: "synthetic-account", Destination: "https://registry.test/");

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token) => Task.FromResult<IRegistrySession>(this);

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token)
        {
            var kinds = new[]
            {
                RegistrySyncActionKind.Create,
                RegistrySyncActionKind.Update,
                RegistrySyncActionKind.Delete,
            };
            IReadOnlyList<RegistrySyncAction> actions = kinds.Select((kind, index) =>
                {
                    RegistrySyncAction ProjectValue()
                    {
                        RegistrySyncActionKind operationValue = kind;
                        string operationValue2 = Target.Destination;
                        string operationValue3 = "synthetic-course";
                        string operationValue4 = "synthetic-group";
                        int operationValue5 = 2026;
                        int operationValue6 = 1;
                        int operationValue7 = index + 1;
                        int operationValue8 = operationValue7;
                        DateTime operationValue9 = new(
                            year: operationValue5,
                            month: operationValue6,
                            day: operationValue8);
                        var operationValue10 = operationValue9;
                        string operationValue11 = "synthetic-topic";
                        int? operationValue12 = 2;
                        bool operationValue13 = true;
                        string? operationValue14 = null;
                        Func<CancellationToken, Task> operationValue15 = async ct =>
                        {
                            lock (Executed)
                            {
                                Executed.Add(index);
                            }

                            if (ActionFailure is not null)
                            {
                                if (index == 0)
                                {
                                    throw ActionFailure;
                                }
                            }

                            if (BlockSecond is not null)
                            {
                                if (index == 1)
                                {
                                    await BlockSecond;
                                }
                            }

                            bool CalculateConditionResult()
                            {
                                if (!CancelSecond)
                                {
                                    return false;
                                }

                                if (index == 1)
                                {
                                    return true;
                                }

                                return false;
                            }

                            bool conditionResult = CalculateConditionResult();


                            if (conditionResult)
                            {
                                throw new OperationCanceledException();
                            }

                            await Task.CompletedTask;
                        };
                        var operationValue16 = new RegistrySyncAction(
                            kind: operationValue,
                            destination: operationValue2,
                            course: operationValue3,
                            groups: operationValue4,
                            operationValue10,
                            topic: operationValue11,
                            operationValue12,
                            operationValue13,
                            omission: operationValue14,
                            operationValue15);
                        return operationValue16;
                    }

                    return ProjectValue();
                })
                .ToArray();
            return Task.FromResult(actions);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGradeProvider : IRegistryGradePlanner
    {
        public Task<RegistryGradePlan> Plan(
            IRegistrySession session,
            string quizId,
            CancellationToken token)
        {
            RegistryGradePlan CalculateResult()
            {
                var result = new RegistryGradePlan([], []);
                return result;
            }

            return Task.FromResult(CalculateResult());
        }
    }

    private sealed class SyncCapture : TextWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly object _gate = new();
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                _builder.AppendLine(value);
            }
        }

        public override void Write(char value)
        {
            lock (_gate)
            {
                _builder.Append(value);
            }
        }

        public string Snapshot()
        {
            lock (_gate)
            {
                return _builder.ToString();
            }
        }
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture parent) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (parent.Messages)
                {
                    parent.Messages.Add($"{logLevel}:{formatter(state, exception)}");
                }
            }
        }
    }

    private sealed class FixtureNavigator(CourseId course, GroupLink group) : IRegistrySyncNavigator
    {
        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester)
        {
            Uri CalculateUrl()
            {
                Uri url = new("https://registry.test/course");
                return url;
            }

            return Task.FromResult<IEnumerable<CourseLink>>([new(course, CalculateUrl())]);
        }

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link) =>
            Task.FromResult<IEnumerable<GroupLink>>([group]);

        public async Task<IDocument> GetHtml(Uri uri)
        {
            string CalculateSource()
            {
                if (uri.AbsolutePath == "/group")
                {
                    return
                        "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>";
                }

                return "<html></html>";
            }

            return await new HtmlParser().ParseDocumentAsync(
                CalculateSource());
        }

        public Task SubmitLesson(IDocument document) =>
            throw new InvalidOperationException("Preview submitted a lesson.");

        public Task SubmitDelete(IDocument document) =>
            throw new InvalidOperationException("Preview submitted a deletion.");
    }
}
