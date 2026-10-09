using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClosedXML.Excel;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using Xunit;

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
            Assert.Equal(2, await CliHost.Run(["query", "lessons", option, path, "--json"]));
            using var result = JsonDocument.Parse(text.ToString());
            Assert.Equal(2, result.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
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
            Assert.Equal(2, await CliHost.Run(["export", "teachers-excel", "--output", path, "--json"]));
            using var result = JsonDocument.Parse(text.ToString());
            Assert.Equal(2, result.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task InvalidAttendanceSourceReturnsInputError()
    {
        using var temp = new TempDirectory();
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "2026_sem1", "bad"));
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(3, await CliHost.Run(["query", "lessons", "--data-dir", temp.Path, "--json"]));
            using var json = JsonDocument.Parse(text.ToString());
            Assert.Equal(3, json.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Contains("Invalid attendance mode", json.RootElement.GetProperty("errors")[0].GetString());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task OwnershipPreservesUnrelatedFilesAndRejectsModifiedArtifacts()
    {
        using var temp = new TempDirectory();
        var unrelated = Path.Combine(temp.Path, "notes.txt");
        await File.WriteAllTextAsync(unrelated, "user file");
        await using (var output = await RunOutput.Create(temp.Path, "test", "one", CancellationToken.None))
        {
            await output.Publish("book.xlsx", (s, t) => s.WriteAsync(new byte[] { 1, 2, 3 }, t).AsTask(), CancellationToken.None);
            await output.Complete("succeeded", CancellationToken.None);
        }
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "book.xlsx"), "user changed it");
        await using var next = await RunOutput.Create(temp.Path, "test", "two", CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => next.Publish("book.xlsx", (s, t) => Task.CompletedTask, CancellationToken.None));
        Assert.Equal("user file", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task TwoProcessesShareCompleteCacheAndGenerateRepresentativeWorkbooks()
    {
        using var temp = new TempDirectory();
        var cache = Path.Combine(temp.Path, "cache");
        var firstOutput = Path.Combine(temp.Path, "first");
        var secondOutput = Path.Combine(temp.Path, "second");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        using var first = Start("export", fixtures, cache, firstOutput);
        using var second = Start("export", fixtures, cache, secondOutput);
        var results = await Task.WhenAll(Finish(first), Finish(second));
        foreach (var result in results)
        {
            Assert.Equal(0, result.Exit);
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal("succeeded", json.RootElement.GetProperty("status").GetString());
            Assert.True(json.RootElement.GetProperty("data").GetProperty("teacherCount").GetInt32() > 0);
        }
        foreach (var output in new[] { firstOutput, secondOutput })
        {
            using var workbook = new XLWorkbook(Path.Combine(output, "all-teachers.xlsx"));
            Assert.Equal("main", workbook.Worksheet(1).Name);
            var content = string.Join(" ", workbook.Worksheet(1).CellsUsed().Select(x => x.GetFormattedString()));
            Assert.Contains("423/4", content);
            Assert.Contains("IA", content);
            Assert.True(workbook.Worksheet(1).RowsUsed().Count() > 20);
            Assert.True(File.Exists(Path.Combine(output, "schedulelib-manifest.json")));
        }
        var cached = Assert.Single(Directory.EnumerateFiles(cache, "*.json"));
        var publishedAt = File.GetLastWriteTimeUtc(cached);
        using (var query = Start("query", fixtures, cache)) await Finish(query);
        Assert.Equal(publishedAt, File.GetLastWriteTimeUtc(cached));
        var bypass = Path.Combine(temp.Path, "bypassed");
        using (var query = Start("query", fixtures, bypass, "no-cache")) await Finish(query);
        Assert.False(Directory.Exists(bypass));
        await using var input = File.OpenRead(cached);
        var model = await ScheduleSerializer.Deserialize(input, CancellationToken.None);
        Assert.NotNull(model);
        Assert.Empty(Directory.EnumerateFiles(cache, "*.tmp"));
    }

    [Fact]
    public async Task DefaultExportsOwnDistinctRunDirectories()
    {
        using var temp = new TempDirectory();
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var cache = Path.Combine(temp.Path, "cache");
        using var first = Start("export", fixtures, cache, "-", temp.Path);
        using var second = Start("export", fixtures, cache, "-", temp.Path);
        var results = await Task.WhenAll(Finish(first), Finish(second));
        var directories = results.Select(x =>
        {
            using var json = JsonDocument.Parse(x.Output);
            return json.RootElement.GetProperty("data").GetProperty("outputDirectory").GetString()!;
        }).ToArray();
        Assert.NotEqual(directories[0], directories[1]);
        foreach (var directory in directories)
        {
            Assert.Equal(Path.Combine(temp.Path, "output"), Path.GetDirectoryName(directory));
            Assert.True(File.Exists(Path.Combine(directory, "all-teachers.xlsx")));
            Assert.True(File.Exists(Path.Combine(directory, "schedulelib-manifest.json")));
        }
    }

    [Fact]
    public async Task FailedScheduleRebuildKeepsPreviouslyDeserializableCache()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "sources");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var file in Directory.EnumerateFiles(fixtures, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(source, Path.GetRelativePath(fixtures, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        var cache = Path.Combine(temp.Path, "cache");
        using (var query = Start("query", source, cache)) await Finish(query);
        var cacheFile = Assert.Single(Directory.EnumerateFiles(cache, "*.json"));
        var previous = await File.ReadAllBytesAsync(cacheFile);
        await File.WriteAllTextAsync(Path.Combine(source, "2025_sem2", "zi", "master.xlsx"), "damaged source");
        using (var query = Start("query", source, cache))
        {
            var stdout = query.StandardOutput.ReadToEndAsync();
            var stderr = query.StandardError.ReadToEndAsync();
            await query.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.NotEqual(0, query.ExitCode);
            using var json = JsonDocument.Parse(await stdout);
            Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
            await stderr;
        }
        Assert.Equal(previous, await File.ReadAllBytesAsync(cacheFile));
        await using var input = File.OpenRead(cacheFile);
        Assert.NotNull(await ScheduleSerializer.Deserialize(input, CancellationToken.None));
    }

    [Fact]
    public async Task SeparateProcessOwnsDestinationAndInterruptedPublicationPreservesPreviousCache()
    {
        using var temp = new TempDirectory();
        var cacheFile = Path.Combine(temp.Path, "schedule.json");
        await File.WriteAllTextAsync(cacheFile, "previous complete cache");
        using (var writer = Start("interrupt", cacheFile))
        {
            Assert.Equal("staged", await writer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal("previous complete cache", await File.ReadAllTextAsync(cacheFile));
            writer.Kill(entireProcessTree: true);
            await writer.WaitForExitAsync();
        }
        Assert.Equal("previous complete cache", await File.ReadAllTextAsync(cacheFile));
        await using (var lease = await LocalFileLock.Acquire(cacheFile + ".lock", CancellationToken.None)) { }
        var lockPath = Path.Combine(temp.Path, ".schedulelib-output.lock");
        using var owner = Start("lock", lockPath);
        Assert.Equal("locked", await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
        try
        {
            await Assert.ThrowsAsync<LocalOperationBusyException>(() => RunOutput.Create(temp.Path, "test", "two", CancellationToken.None));
            Assert.Equal(7, await CliHost.Run(["export", "teachers-excel", "--data-dir", Path.Combine(AppContext.BaseDirectory, "fixtures"), "--output", temp.Path, "--json"]));
        }
        finally { owner.Kill(entireProcessTree: true); await owner.WaitForExitAsync(); }
    }

    [Fact]
    public async Task FailedAndCancelledWritesKeepPreviousFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "cache.json");
        await File.WriteAllTextAsync(path, "previous");
        await Assert.ThrowsAsync<IOException>(() => AtomicFile.Publish(path, async (s, t) =>
        {
            await s.WriteAsync(new byte[10], t);
            throw new IOException("write failed");
        }, CancellationToken.None));
        Assert.Equal("previous", await File.ReadAllTextAsync(path));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.Publish(path, async (s, t) =>
        {
            await s.WriteAsync(new byte[10], t);
            cancellation.Cancel();
        }, cancellation.Token));
        Assert.Equal("previous", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
    }

    private static Process Start(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (args.Length > 4) info.WorkingDirectory = args[4];
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in args) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    private static async Task<(int Exit, string Output, string Error)> Finish(Process process)
    {
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        var result = (Exit: process.ExitCode, Output: await stdout, Error: await stderr);
        Assert.True(result.Exit == 0, result.Error);
        return result;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"schedulelib-storage-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
