using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

public sealed class DriveSyncTests
{
    [Fact]
    public async Task PreviewAndApplyPreserveCaseInsensitiveMatchingDuplicatesAndDeletion()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var files = await CreateArtifacts(
            temp,
            "schedule.pdf",
            "new.ics",
            "schedulelib-manifest.json",
            ".schedulelib-output.lock",
            "notes.txt");
        using var fake = new Fake
        {
            Remote =
            [
                new(Id: "old", Name: "obsolete.xlsx"), new(Id: "one", Name: "SCHEDULE.PDF"),
                new(Id: "two", Name: "schedule.pdf"),
            ],
        };
        var preview = await DriveSync.Run(
            fake,
            "Configured",
            files,
            false,
            default);
        Assert.Equal(expected: "actual-account", actual: preview.Account);
        Assert.Equal(expected: "actual@example.test", actual: preview.AccountEmail);
        Assert.Equal(expected: "actual-folder", actual: preview.Folder.Id);
        Assert.Equal(
            expected: new[]
            {
                "delete",
                "create",
                "update",
                "update",
            },
            actual: preview.Actions.Select(x => x.Action));
        Assert.Empty(fake.Writes);
        Assert.Empty(preview.Outcomes);
        fake.Remote = [new(Id: "changed", Name: "obsolete.xlsx"), new(Id: "one", Name: "SCHEDULE.PDF")];
        var applied = await DriveSync.Run(
            fake,
            folderName: "Configured",
            files,
            true,
            default,
            lockRoot: temp.Path);
        Assert.Equal(expected: 0, actual: applied.ExitCode);
        Assert.Equal(
            expected: new[]
            {
                "delete changed",
                "create new.ics",
                "update one",
            },
            actual: fake.Writes);
        Assert.All(applied.Outcomes, x => Assert.Equal(expected: "completed", actual: x.State));
        Assert.Equal(expected: 2, actual: fake.ListCalls);
    }

    [Theory]
    [InlineData(
        "lost",
        6,
        "uncertain")]
    [InlineData(
        "cancel",
        6,
        "uncertain")]
    [InlineData(
        "reject",
        5,
        "failed")]
    public async Task CreateFailureIsNeverRetriedAndRemainingActionsAreReported(
        string fail,
        int exit,
        string state)
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var files = await CreateArtifacts(temp, "first.pdf", "second.ics");
        using var fake = new Fake
        {
            Fail = fail,
        };
        var result = await DriveSync.Run(
            fake,
            folderName: "Configured",
            files,
            true,
            default,
            lockRoot: temp.Path);
        Assert.Equal(expected: exit, actual: result.ExitCode);
        Assert.Equal(expected: state, actual: result.Outcomes[0].State);
        Assert.Equal(expected: "not-attempted", actual: result.Outcomes[1].State);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task RejectedDeleteAndUnauthorizedCreateCannotBecomeSuccess()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        using var rejectedDelete = new Fake
        {
            Remote = [new(Id: "old", Name: "old.txt")],
            FailDelete = true,
        };
        var deletion = await DriveSync.Run(
            rejectedDelete,
            folderName: "Configured",
            await temp.Artifacts("a.pdf"),
            true,
            default,
            lockRoot: temp.Path);
        Assert.Equal(expected: 5, actual: deletion.ExitCode);
        Assert.Equal(expected: "failed", actual: deletion.Outcomes[0].State);
        Assert.Equal(expected: "not-attempted", actual: deletion.Outcomes[1].State);
        Assert.Single(rejectedDelete.Writes);
        using var unauthorized = new Fake
        {
            Fail = "unauthorized",
        };
        var creation = await DriveSync.Run(
            unauthorized,
            folderName: "Configured",
            await temp.Artifacts("a.pdf"),
            true,
            default,
            lockRoot: temp.Path);
        Assert.Equal(expected: 4, actual: creation.ExitCode);
        string expected2 = "failed";
        var single = Assert.Single(creation.Outcomes);
        string actual2 = single.State;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Single(unauthorized.Writes);
    }

    [Fact]
    public async Task PartialDeletionThenLostCreatePreservesCompletedIdentity()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        using var fake = new Fake
        {
            Remote = [new(Id: "removed", Name: "old.txt")],
            Fail = "lost",
        };
        var result = await DriveSync.Run(
            fake,
            folderName: "Configured",
            await temp.Artifacts("a.pdf", "b.pdf"),
            true,
            default,
            lockRoot: temp.Path);
        Assert.Equal(expected: 6, actual: result.ExitCode);
        Assert.Equal(expected: "removed", actual: result.Outcomes[0].ResultFileId);
        Assert.Equal(expected: "completed", actual: result.Outcomes[0].State);
        Assert.Equal(expected: "uncertain", actual: result.Outcomes[1].State);
        Assert.Equal(expected: "not-attempted", actual: result.Outcomes[2].State);
        Assert.Equal(expected: 2, actual: fake.Writes.Count);
    }

    [Fact]
    public async Task CancellationBetweenActionsDoesNotStartNextMutation()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        using var cancellation = new CancellationTokenSource();
        using var fake = new Fake
        {
            AfterWrite = cancellation.Cancel,
        };
        var result = await DriveSync.Run(
            fake,
            folderName: "Configured",
            await temp.Artifacts("a.pdf", "b.pdf"),
            true,
            cancellation.Token,
            lockRoot: temp.Path);
        Assert.Equal(expected: 130, actual: result.ExitCode);
        Assert.Equal(expected: "completed", actual: result.Outcomes[0].State);
        Assert.Equal(expected: "not-attempted", actual: result.Outcomes[1].State);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task AliasesLockActualAccountFolderBeforeListingAndReleaseOnFailure()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        using var fake = new Fake();

        string CalculatePath()
        {
            string path = DriveSync.LockPath(
                root: temp.Path,
                account: "actual-account",
                folderId: "actual-folder");
            return path;
        }

        await using (var lease = await LocalFileLock.Acquire(
                         CalculatePath(),
                         default))
        {
            await Assert.ThrowsAsync<LocalOperationBusyException>(() => DriveSync.Run(
                fake,
                folderName: "Alias",
                [],
                true,
                default,
                lockRoot: temp.Path));
            Assert.Equal(expected: 0, actual: fake.ListCalls);
            Assert.Empty(fake.Writes);
            Assert.Equal(
                expected: 0,
                actual: (await DriveSync.Run(
                    fake,
                    folderName: "Alias",
                    [],
                    false,
                    default,
                    lockRoot: temp.Path)).ExitCode);
        }
        Assert.Equal(
            expected: 0,
            actual: (await DriveSync.Run(
                fake,
                folderName: "Alias",
                [],
                true,
                default,
                lockRoot: temp.Path)).ExitCode);
    }

    [Fact]
    public async Task DestinationChangeUnderLockRefusesAllWrites()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        using var fake = new Fake
        {
            ChangeFolder = true,
        };
        await Assert.ThrowsAsync<IOException>(() => DriveSync.Run(
            fake,
            folderName: "Configured",
            [],
            true,
            default,
            lockRoot: temp.Path));
        Assert.Empty(fake.Writes);
        Assert.Equal(expected: 0, actual: fake.ListCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandBuildsRealBundleAndExcludesManifestsAndCallerFiles(bool apply)
    {
        using var temp = new TestTempDirectory("schedulelib-tests");

        string CalculatePath()
        {
            string path2 = Path.Combine(temp.Path, "caller.pdf");
            return path2;
        }

        await File.WriteAllTextAsync(path: CalculatePath(), contents: "untouched");
        using var fake = new Fake();
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(
                expected: 0,
                actual: await new FixtureDrive(fake).Publish(
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
                        Directory = temp.Path,
                    },
                    new()
                    {
                        Json = true,
                    },
                    new()
                    {
                        Apply = apply,
                    }));
            using var json = System.Text.Json.JsonDocument.Parse(text.ToString());

            string CalculateExpected()
            {
                if (apply)
                {
                    return "applied";
                }

                return "preview";
            }

            Assert.Equal(
                expected: CalculateExpected(),
                actual: json.RootElement.GetProperty("status")
                    .GetString());
            var actions = json.RootElement.GetProperty("data")
                .GetProperty("actions")
                .EnumerateArray()
                .ToArray();
            var names = actions.Select(x => x.GetProperty("name")
                    .GetString())
                .ToArray();
            Assert.Contains("all-teachers.xlsx", names);
            Assert.Contains("free-rooms.xlsx", names);
            Assert.Contains(names, x => x!.EndsWith(".pdf", StringComparison.Ordinal));
            Assert.Contains(names, x => x!.EndsWith(".ics", StringComparison.Ordinal));
            Assert.DoesNotContain("caller.pdf", names);
            Assert.DoesNotContain("schedulelib-manifest.json", names);
            Assert.DoesNotContain(".schedulelib-output.lock", names);
            foreach (var path in Directory.GetFiles(path: temp.Path, searchPattern: "*.xlsx"))
            {
                using var workbook = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false);
                Assert.NotEmpty(workbook.WorkbookPart!.WorksheetParts);
            }
            Assert.StartsWith(
                expectedStartString: "%PDF-",
                actualString: System.Text.Encoding.ASCII.GetString(
                    await File.ReadAllBytesAsync(
                        Directory.GetFiles(path: temp.Path, searchPattern: "*.pdf")
                            .First(x => !x.EndsWith(
                                "caller.pdf",
                                StringComparison.Ordinal)))));

            string CalculatePath2()
            {
                string[] files = Directory.GetFiles(path: temp.Path, searchPattern: "*.ics");
                int int32 = 0;
                string path3 = files[int32];
                return path3;
            }

            Assert.Contains(
                expectedSubstring: "BEGIN:VEVENT",
                actualString: await File.ReadAllTextAsync(CalculatePath2()));
            if (apply)
            {
                Assert.Equal(expected: actions.Length, actual: fake.Writes.Count);
            }
            else
            {
                Assert.Empty(fake.Writes);
            }

            string CalculatePath3()
            {
                string path4 = Path.Combine(temp.Path, "caller.pdf");
                return path4;
            }

            Assert.Equal(expected: "untouched", actual: await File.ReadAllTextAsync(CalculatePath3()));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static async Task<DriveArtifact[]> CreateArtifacts(
        TestTempDirectory temp,
        params string[] names)
    {
        foreach (var name in names)
        {
            await File.WriteAllTextAsync(Path.Combine(temp.Path, name), "fixture bytes");
        }

        return names.Select(name => new DriveArtifact(
                Name: name,
                Path: Path.Combine(temp.Path, name)))
            .ToArray();
    }

    [Theory]
    [InlineData(
        new[]
        {
            "drive",
            "publish",
            "--help",
        },
        0)]
    [InlineData(
        new[]
        {
            "drive",
            "publish",
            "--unknown",
        },
        2)]
    public async Task HelpAndInvalidOptionsNeedNoInitialization(string[] args, int exit) =>
        Assert.Equal(expected: exit, actual: await CliHost.Run(args));

    [Fact]
    public async Task AuthenticationFailureIsExitFourWithOneJsonResult()
    {
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(
                expected: 4,
                actual: await new FixtureDrive(null).Publish(
                    new()
                    {
                        NoCache = true,
                        DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                    },
                    new()
                    {
                        Profile = "Curmanschii Anton",
                    },
                    new(),
                    new()
                    {
                        Json = true,
                    },
                    new()));
            using var json = System.Text.Json.JsonDocument.Parse(text.ToString());
            Assert.Equal(
                expected: 4,
                actual: json.RootElement.GetProperty("exitCode")
                    .GetInt32());
            Assert.Empty(json.RootElement.GetProperty("outputs")
                .EnumerateArray());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    internal sealed class FixtureDrive(IDriveSyncProvider? fake) : DriveCommands
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
        {
            return fake is null
                ? throw new AuthenticationRequiredException(provider: "google", teacher: "Curmanschii Anton")
                : Task.FromResult(fake);
        }
    }

    private sealed class Fake : IDriveSyncProvider
    {
        public IReadOnlyList<DriveRemoteFile> Remote { get; set; } = [];

        public List<string> Writes { get; } = [];

        public string? Fail { get; init; }

        public bool FailDelete { get; init; }

        public Action? AfterWrite { get; init; }

        public bool ChangeFolder { get; init; }

        public int ListCalls { get; private set; }

        private int _folderCalls;

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
            bool CheckCondition()
            {
                if (!ChangeFolder)
                {
                    return false;
                }

                int conditionValue = ++_folderCalls;
                if (conditionValue > 1)
                {
                    return true;
                }

                return false;
            }

            string id;
            if (CheckCondition())
            {
                id = "changed-folder";
            }
            else
            {
                id = "actual-folder";
            }

            string name2 = "Actual folder";
            var result = new DriveDestination(
                Id: id,
                Name: name2);
            var fromResult = Task.FromResult(result);
            return fromResult;
        }

        public Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string id, CancellationToken token)
        {
            ListCalls++;
            return Task.FromResult(Remote);
        }

        public Task Delete(string id, CancellationToken token)
        {
            string CalculateItem()
            {
                string text = $"delete {id}";
                string item = text;
                return item;
            }

            Writes.Add(CalculateItem());
            if (FailDelete)
            {
                throw new Google.GoogleApiException(serviceName: "drive", message: "Rejected")
                {
                    HttpStatusCode = System.Net.HttpStatusCode.Forbidden,
                };
            }

            AfterWrite?.Invoke();
            return Task.CompletedTask;
        }

        public Task<string> Create(
            string folder,
            string name,
            Stream input,
            CancellationToken token)
        {
            Assert.True(input.Length > 0);

            string CalculateItem()
            {
                string text = $"create {name}";
                string item = text;
                return item;
            }

            Writes.Add(CalculateItem());
            if (Fail == "lost")
            {
                throw new IOException("Lost response");
            }

            if (Fail == "cancel")
            {
                throw new OperationCanceledException();
            }

            if (Fail == "unauthorized")
            {
                throw new Google.GoogleApiException(serviceName: "drive", message: "Unauthorized")
                {
                    HttpStatusCode = System.Net.HttpStatusCode.Unauthorized,
                };
            }

            if (Fail == "reject")
            {
                throw new Google.GoogleApiException(serviceName: "drive", message: "Rejected")
                {
                    HttpStatusCode = System.Net.HttpStatusCode.BadRequest,
                };
            }

            AfterWrite?.Invoke();
            return Task.FromResult("created");
        }

        public Task<string> Update(
            string id,
            Stream input,
            CancellationToken token)
        {
            Assert.True(input.Length > 0);

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


}
