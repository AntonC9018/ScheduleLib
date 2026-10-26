using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClosedXML.Excel;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;

public sealed class StorageTests
{
    [Theory]
    [InlineData("--data-dir", "")]
    [InlineData("--cache-dir", "")]
    [InlineData("--data-dir", "\0")]
    [InlineData("--cache-dir", "\0")]
    public async Task InvalidSourcePathsHaveArgumentExitAndJson(string option, string path)
    {
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(expected: 2, actual: await CliHost.Run(["query", "lessons", option, path, "--json"]));
            using var result = JsonDocument.Parse(text.ToString());
            Assert.Equal(expected: 2,
                actual: result.RootElement.GetProperty("exitCode")
                    .GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    public async Task InvalidOutputPathsHaveArgumentExitAndJson(string path)
    {
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(expected: 2,
                actual: await CliHost.Run(["export", "teachers-excel", "--output", path, "--json"]));
            using var result = JsonDocument.Parse(text.ToString());
            Assert.Equal(expected: 2,
                actual: result.RootElement.GetProperty("exitCode")
                    .GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task InvalidAttendanceSourceReturnsInputError()
    {
        using var temp = new TempDirectory();

        string CalculatePath()
        {
            string path = System.IO.Path.Combine(
                temp.Path,
                "2026_sem1",
                "bad");
            return path;
        }

        System.IO.Directory.CreateDirectory(
            CalculatePath());
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(expected: 3,
                actual: await CliHost.Run(["query", "lessons", "--data-dir", temp.Path, "--json"]));
            using var json = JsonDocument.Parse(text.ToString());
            Assert.Equal(expected: 3,
                actual: json.RootElement.GetProperty("exitCode")
                    .GetInt32());
            Assert.Contains(expectedSubstring: "Invalid attendance mode",
                actualString: json.RootElement.GetProperty("errors")[0]
                    .GetString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task OwnershipPreservesUnrelatedFilesAndRejectsModifiedArtifacts()
    {
        using var temp = new TempDirectory();
        var unrelated = Path.Combine(temp.Path, "notes.txt");
        await File.WriteAllTextAsync(path: unrelated, contents: "user file");
        await using (var output = await RunOutput.Create(
                         output: temp.Path,
                         command: "test",
                         runId: "one",
                         CancellationToken.None))
        {
            await output.Publish(
                "book.xlsx",
                (s, t) => s.WriteAsync(
                        new byte[]
                        {
                            1,
                            2,
                            3,
                        },
                        t)
                    .AsTask(),
                CancellationToken.None);
            await output.Complete("succeeded", CancellationToken.None);
        }

        string CalculatePath()
        {
            string path2 = Path.Combine(temp.Path, "book.xlsx");
            return path2;
        }

        await File.WriteAllTextAsync(path: CalculatePath(), contents: "user changed it");
        await using var next = await RunOutput.Create(
            output: temp.Path,
            command: "test",
            runId: "two",
            CancellationToken.None);
        await Assert.ThrowsAsync<RunOutput.OutputArtifactOwnershipConflictException>(() => next.Publish(
            "book.xlsx",
            (s, t) => Task.CompletedTask,
            CancellationToken.None));
        Assert.Equal(expected: "user file", actual: await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task TwoProcessesShareCompleteCacheAndGenerateRepresentativeWorkbooks()
    {
        using var temp = new TempDirectory();
        var cache = Path.Combine(temp.Path, "cache");
        var firstOutput = Path.Combine(temp.Path, "first");
        var secondOutput = Path.Combine(temp.Path, "second");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        using var first = Start(
            "export",
            fixtures,
            cache,
            firstOutput);
        using var second = Start(
            "export",
            fixtures,
            cache,
            secondOutput);
        var results = await Task.WhenAll(Finish(first), Finish(second));
        foreach (var result in results)
        {
            Assert.Equal(expected: 0, actual: result.Exit);
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal(expected: "succeeded",
                actual: json.RootElement.GetProperty("status")
                    .GetString());
            Assert.True(json.RootElement.GetProperty("data")
                    .GetProperty("teacherCount")
                    .GetInt32()
                > 0);
        }
        foreach (var output in new[]
                 {
                     firstOutput,
                     secondOutput,
                 })
        {
            using var workbook = new XLWorkbook(Path.Combine(output, "all-teachers.xlsx"));

            string CalculateActual()
            {
                global::ClosedXML.Excel.IXLWorksheet worksheet = workbook.Worksheet(1);
                string actual2 = worksheet.Name;
                return actual2;
            }

            Assert.Equal(expected: "main", actual: CalculateActual());
            string separator = " ";
            global::ClosedXML.Excel.IXLWorksheet worksheet3 = workbook.Worksheet(1);
            global::ClosedXML.Excel.IXLCells cellsUsed = worksheet3.CellsUsed();
            global::System.Func<global::ClosedXML.Excel.IXLCell, string> selector = x => x.GetFormattedString();
            global::System.Collections.Generic.IEnumerable<string> values = cellsUsed.Select(selector);
            var content = string.Join(separator, values);
            Assert.Contains(expectedSubstring: "423/4", actualString: content);
            Assert.Contains(expectedSubstring: "IA", actualString: content);

            bool CalculateCondition()
            {
                global::ClosedXML.Excel.IXLWorksheet worksheet2 = workbook.Worksheet(1);
                global::ClosedXML.Excel.IXLRows rowsUsed = worksheet2.RowsUsed();
                bool condition = rowsUsed.Count() > 20;
                if (condition)
                {
                    return true;
                }

                return false;
            }

            Assert.True(CalculateCondition());
            string path2 = Path.Combine(output, "schedulelib-manifest.json");
            bool condition2 = File.Exists(path2);
            Assert.True(condition2);
        }
        global::System.Collections.Generic.IEnumerable<string> collection =
            Directory.EnumerateFiles(path: cache, searchPattern: "*.json");
        var cached = Assert.Single(collection);
        var publishedAt = File.GetLastWriteTimeUtc(cached);
        using (var query = Start(
                   "query",
                   fixtures,
                   cache))
        {
            await Finish(query);
        }

        global::System.DateTime expected2 = publishedAt;
        global::System.DateTime actual3 = File.GetLastWriteTimeUtc(cached);
        Assert.Equal(expected: expected2, actual: actual3);
        var bypass = Path.Combine(temp.Path, "bypassed");
        using (var query = Start(
                   "query",
                   fixtures,
                   bypass,
                   "no-cache"))
        {
            await Finish(query);
        }

        bool condition3 = Directory.Exists(bypass);
        Assert.False(condition3);
        await using var input = File.OpenRead(cached);
        var model = await ScheduleSerializer.Deserialize(input, CancellationToken.None);
        Assert.NotNull(model);
        global::System.Collections.Generic.IEnumerable<string> collection2 =
            Directory.EnumerateFiles(path: cache, searchPattern: "*.tmp");
        global::System.Collections.IEnumerable collection3 = collection2;
        Assert.Empty(collection3);
    }

    [Fact]
    public async Task DefaultExportsOwnDistinctRunDirectories()
    {
        using var temp = new TempDirectory();
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var cache = Path.Combine(temp.Path, "cache");
        using var first = Start(
            "export",
            fixtures,
            cache,
            "-",
            temp.Path);
        using var second = Start(
            "export",
            fixtures,
            cache,
            "-",
            temp.Path);
        var results = await Task.WhenAll(Finish(first), Finish(second));
        var directories = results.Select(x =>
            {
                using var json = JsonDocument.Parse(x.Output);
                return json.RootElement.GetProperty("data")
                    .GetProperty("outputDirectory")
                    .GetString()!;
            })
            .ToArray();
        Assert.NotEqual(expected: directories[0], actual: directories[1]);
        foreach (var directory in directories)
        {
            string CalculateExpected()
            {
                string expected2 = Path.Combine(temp.Path, "output");
                return expected2;
            }

            string expected3 = CalculateExpected();
            string? actual2 = Path.GetDirectoryName(directory);
            Assert.Equal(expected: expected3, actual: actual2);
            string path = Path.Combine(directory, "all-teachers.xlsx");
            bool condition = File.Exists(path);
            Assert.True(condition);
            string path2 = Path.Combine(directory, "schedulelib-manifest.json");
            bool condition2 = File.Exists(path2);
            Assert.True(condition2);
        }
    }

    [Fact]
    public async Task FailedScheduleRebuildKeepsPreviouslyDeserializableCache()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "sources");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var file in Directory.EnumerateFiles(
                     path: fixtures,
                     searchPattern: "*",
                     SearchOption.AllDirectories))
        {
            string path1 = source;
            string path2 = Path.GetRelativePath(relativeTo: fixtures, path: file);
            var destination = Path.Combine(path1, path2);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFileName: file, destFileName: destination);
        }
        var cache = Path.Combine(temp.Path, "cache");
        using (var query = Start(
                   "query",
                   source,
                   cache))
        {
            await Finish(query);
        }

        global::System.Collections.Generic.IEnumerable<string> collection =
            Directory.EnumerateFiles(path: cache, searchPattern: "*.json");
        var cacheFile = Assert.Single(collection);
        var previous = await File.ReadAllBytesAsync(cacheFile);
        await File.WriteAllTextAsync(
            path: Path.Combine(
                source,
                "2025_sem2",
                "zi",
                "master.xlsx"),
            contents: "damaged source");
        using (var query = Start(
                   "query",
                   source,
                   cache))
        {
            var stdout = query.StandardOutput.ReadToEndAsync();
            var stderr = query.StandardError.ReadToEndAsync();

            global::System.TimeSpan CalculateTimeout()
            {
                global::System.TimeSpan timeout = TimeSpan.FromSeconds(60);
                return timeout;
            }

            await query.WaitForExitAsync()
                .WaitAsync(CalculateTimeout());
            Assert.NotEqual(expected: 0, actual: query.ExitCode);
            using var json = JsonDocument.Parse(await stdout);
            Assert.Equal(expected: "failed",
                actual: json.RootElement.GetProperty("status")
                    .GetString());
            await stderr;
        }
        Assert.Equal(expected: previous, actual: await File.ReadAllBytesAsync(cacheFile));
        await using var input = File.OpenRead(cacheFile);
        Assert.NotNull(await ScheduleSerializer.Deserialize(input, CancellationToken.None));
    }

    [Fact]
    public async Task SeparateProcessOwnsDestinationAndInterruptedPublicationPreservesPreviousCache()
    {
        using var temp = new TempDirectory();
        var cacheFile = Path.Combine(temp.Path, "schedule.json");
        await File.WriteAllTextAsync(path: cacheFile, contents: "previous complete cache");
        using (var writer = Start("interrupt", cacheFile))
        {
            global::System.TimeSpan CalculateTimeout()
            {
                global::System.TimeSpan timeout = TimeSpan.FromSeconds(30);
                return timeout;
            }

            Assert.Equal(expected: "staged",
                actual: await writer.StandardOutput.ReadLineAsync()
                    .WaitAsync(CalculateTimeout()));
            Assert.Equal(expected: "previous complete cache", actual: await File.ReadAllTextAsync(cacheFile));
            writer.Kill(entireProcessTree: true);
            await writer.WaitForExitAsync();
        }
        Assert.Equal(expected: "previous complete cache", actual: await File.ReadAllTextAsync(cacheFile));
        await using (var lease = await LocalFileLock.Acquire((string) $"{cacheFile}.lock", CancellationToken.None))
        {
        }
        var lockPath = Path.Combine(temp.Path, ".schedulelib-output.lock");
        using var owner = Start("lock", lockPath);

        global::System.TimeSpan CalculateTimeout2()
        {
            global::System.TimeSpan timeout2 = TimeSpan.FromSeconds(30);
            return timeout2;
        }

        Assert.Equal(expected: "locked",
            actual: await owner.StandardOutput.ReadLineAsync()
                .WaitAsync(CalculateTimeout2()));
        try
        {
            await Assert.ThrowsAsync<LocalOperationBusyException>(() => RunOutput.Create(
                output: temp.Path,
                command: "test",
                runId: "two",
                CancellationToken.None));
            Assert.Equal(
                expected: 7,
                actual: await CliHost.Run(
                [
                    "export", "teachers-excel", "--data-dir", Path.Combine(AppContext.BaseDirectory, "fixtures"),
                    "--output", temp.Path, "--json",
                ]));
        }
        finally
        {
            owner.Kill(entireProcessTree: true);
            await owner.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task FailedAndCancelledWritesKeepPreviousFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "cache.json");
        await File.WriteAllTextAsync(path: path, contents: "previous");
        await Assert.ThrowsAsync<IOException>(() => AtomicFile.Publish(
            path,
            async (s, t) =>
            {
                await s.WriteAsync(new byte[10], t);
                throw new IOException("write failed");
            },
            CancellationToken.None));
        Assert.Equal(expected: "previous", actual: await File.ReadAllTextAsync(path));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.Publish(
            path,
            async (s, t) =>
            {
                await s.WriteAsync(new byte[10], t);
                cancellation.Cancel();
            },
            cancellation.Token));
        Assert.Equal(expected: "previous", actual: await File.ReadAllTextAsync(path));

        global::System.Collections.Generic.IEnumerable<string> CalculateCollection()
        {
            global::System.Collections.Generic.IEnumerable<string> collection =
                Directory.EnumerateFiles(path: temp.Path, searchPattern: "*.tmp");
            return collection;
        }

        Assert.Empty(CalculateCollection());
    }

    private static Process Start(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (args.Length > 4)
        {
            info.WorkingDirectory = args[4];
        }

        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)!;
    }

    private static async Task<(int Exit, string Output, string Error)> Finish(Process process)
    {
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        global::System.TimeSpan CalculateTimeout()
        {
            global::System.TimeSpan timeout = TimeSpan.FromSeconds(60);
            return timeout;
        }

        await process.WaitForExitAsync()
            .WaitAsync(CalculateTimeout());
        var result = (Exit: process.ExitCode, Output: await stdout, Error: await stderr);
        Assert.True(result.Exit == 0, result.Error);
        return result;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), CalculatePath2());

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static string CalculatePath2()
        {
            string path2 = $"schedulelib-storage-{Guid.NewGuid():N}";
            return path2;
        }
    }
}
