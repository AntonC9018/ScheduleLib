using System.Security.Cryptography;
using System.Text;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.ScheduleDefaults;

public sealed class ScheduleLoaderSerializationTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("empty-hash")]
    [InlineData("corrupt-json")]
    public async Task CacheHitsSkipImportAndChangedIdentityRebuilds(string change)
    {
        using var temp = new TempDirectory();
        var component = new Component();
        var loader = new ScheduleLoader
        {
            CachedPath = temp.FilePath,
            ConfigurationIdentity = "configuration v1",
            Components = [component],
        };
        await loader.Load(CreateContext(), CancellationToken.None);
        Assert.Equal(expected: 1, actual: component.ApplyCount);
        var first = await File.ReadAllBytesAsync(temp.FilePath);
        using (var input = new MemoryStream(first))
        {
            Assert.False(
                string.IsNullOrEmpty((await ScheduleSerializer.Deserialize(input, CancellationToken.None)).Hash));
        }
        var cached = CreateContext();
        await loader.Load(cached, CancellationToken.None);
        Assert.Equal(expected: 1, actual: component.ApplyCount);
        Assert.Equal(expected: "Imported course", actual: Assert.Single(cached.Schedule.Build().Courses).FullName);
        Assert.Equal(expected: first, actual: await File.ReadAllBytesAsync(temp.FilePath));

        switch (change)
        {
            case "source":
                component.Source = "source v2";
                break;
            case "configuration":
                loader.ConfigurationIdentity = "configuration v2";
                break;
            case "empty-hash":
                await ScheduleSerializer.Serialize()
                    .Input(Schedule.Empty)
                    .Output(temp.FilePath)
                    .ExecuteAsync();
                break;
            case "corrupt-json":
                await File.WriteAllTextAsync(path: temp.FilePath, contents: "{corrupt");
                break;
        }
        var rebuilt = CreateContext();
        await loader.Load(rebuilt, CancellationToken.None);
        Assert.Equal(expected: 2, actual: component.ApplyCount);
        Assert.Equal(expected: "Imported course", actual: Assert.Single(rebuilt.Schedule.Build().Courses).FullName);
        await loader.Load(CreateContext(), CancellationToken.None);
        Assert.Equal(expected: 2, actual: component.ApplyCount);
        global::System.Collections.Generic.IEnumerable<string> CalculateCollection()
        {
            global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(path: temp.DirectoryPath, searchPattern: "*.tmp");
            return collection;
        }

        Assert.Empty(CalculateCollection());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrFailedRebuildKeepsCompleteCache(bool cancel)
    {
        using var temp = new TempDirectory();
        var component = new Component();
        var loader = new ScheduleLoader
        {
            CachedPath = temp.FilePath,
            Components = [component],
        };
        await loader.Load(CreateContext(), CancellationToken.None);
        var previous = await File.ReadAllBytesAsync(temp.FilePath);
        using var cancellation = new CancellationTokenSource();
        component.OnApply = () =>
        {
            if (cancel)
            {
                cancellation.Cancel();
                return;
            }
            throw new IOException("source read failed");
        };
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                loader.Load(CreateContext(),
                    cancellation.Token,
                    bypassCache: true)
                    .AsTask());
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => loader.Load(CreateContext(),
                cancellation.Token,
                bypassCache: true)
                .AsTask());
        }
        Assert.Equal(expected: previous, actual: await File.ReadAllBytesAsync(temp.FilePath));
        using var input = new MemoryStream(previous);
        Assert.NotNull(await ScheduleSerializer.Deserialize(input, CancellationToken.None));
        global::System.Collections.Generic.IEnumerable<string> CalculateCollection()
        {
            global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(path: temp.DirectoryPath, searchPattern: "*.tmp");
            return collection;
        }

        Assert.Empty(CalculateCollection());
        // Cancellation/failure releases the loader's cache lock as well.
        component.OnApply = null;
        await loader.Load(CreateContext(),
            CancellationToken.None,
            bypassCache: true);
    }

    private static ScheduleImportContext CreateContext()
    {
        return ScheduleImportContext.Create(new()
        {
            DayNameProvider = new(),
            CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new()),
        });
    }

    private sealed class Component : IScheduleLoaderComponent
    {
        public string Source { get; set; } = "source v1";

        public int ApplyCount { get; private set; }

        public Action? OnApply { get; set; }

        public ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            global::System.Security.Cryptography.IncrementalHash incrementalHash = hasher;
            byte[] data = Encoding.UTF8.GetBytes(Source);
            incrementalHash.AppendData(data);
            return ValueTask.CompletedTask;
        }

        public ValueTask Apply(ScheduleImportContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCount++;
            OnApply?.Invoke();
            context.Schedule.Course("Imported course");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), CalculatePath2());

        public string FilePath => Path.Combine(DirectoryPath, "schedule.json");

        public TempDirectory() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);

        private static string CalculatePath2()
        {
            string path2 = Guid.NewGuid()
                .ToString("N");
            return path2;
        }
    }
}
