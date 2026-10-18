using System.Text;
using System.Text.Json;
using ScheduleLib.Builders;

namespace ScheduleLib.ParserTests;

public sealed class ScheduleSerializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputFormsRoundTripTheSameSchemaWithEmptyDefaultHash(bool usePath)
    {
        using var temp = new TempDirectory();
        using var stream = new MemoryStream();
        var builder = ScheduleSerializer.Serialize().Input(CreateSchedule("Course"));
        if (usePath)
        {
            builder.Output(temp.FilePath);
            Assert.False(File.Exists(temp.FilePath));
        }
        else
        {
            builder.Output(stream);
        }
        await builder.ExecuteAsync();
        var bytes = usePath ? await File.ReadAllBytesAsync(temp.FilePath) : stream.ToArray();
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(new[]
            {
                "Hash",
                "OneTimeLessons",
                "RegularLessons",
                "Groups",
                "Teachers",
                "Courses",
                "Periods",
            }.Order(),
            json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        using var input = new MemoryStream(bytes);
        var model = await ScheduleSerializer.Deserialize(input, CancellationToken.None);
        Assert.Equal("", model.Hash);
        var restored = new ScheduleBuilder();
        ScheduleSerializer.AddToBuilder(restored, model);
        var rebuilt = restored.Build();
        Assert.Equal("Course", Assert.Single(rebuilt.Courses).FullName);
        using var second = new MemoryStream();
        await ScheduleSerializer.Serialize().Input(rebuilt).Output(second).ExecuteAsync();
        Assert.Equal(bytes, second.ToArray());
        Assert.True(stream.CanWrite);
        if (usePath)
        {
            using var exclusive = File.Open(temp.FilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
        }
    }

    [Theory]
    [InlineData(false,
        false,
        "Input")]
    [InlineData(false,
        true,
        "Input")]
    [InlineData(true,
        false,
        "Output")]
    public async Task MissingConfigurationFailsBeforeAnyWrite(
        bool input,
        bool output,
        string missing)
    {
        using var stream = new MemoryStream();
        var builder = ScheduleSerializer.Serialize();
        if (input)
        {
            builder.Input(Schedule.Empty);
        }
        if (output)
        {
            builder.Output(stream);
        }
        var error = await Assert.ThrowsAsync<MissingSerializationConfigurationException>(() => builder.ExecuteAsync());
        Assert.Equal(missing, error.Configuration);
        Assert.Equal(0, stream.Length);
        // The same builder remains usable after correcting its missing configuration.
        await builder.Input(Schedule.Empty).Output(stream).ExecuteAsync();
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task LastSuccessfulSetterWinsAndExecutionRetainsConfiguration()
    {
        using var temp = new TempDirectory();
        using var replaced = new MemoryStream();
        using var final = new MemoryStream();
        var builder = ScheduleSerializer.Serialize()
            .Input(CreateSchedule("Old"))
            .Input(CreateSchedule("New"))
            .Output(replaced)
            .Output(temp.FilePath)
            .Hash("old")
            .Hash("cache identity");
        await builder.ExecuteAsync();
        var first = await File.ReadAllBytesAsync(temp.FilePath);
        // A shorter second result must replace rather than leave trailing bytes.
        await builder.Input(Schedule.Empty).Hash("").ExecuteAsync();
        Assert.True(new FileInfo(temp.FilePath).Length < first.Length);
        await builder.Input(CreateSchedule("New")).Hash("cache identity").Output(final).ExecuteAsync();
        Assert.Equal(first, final.ToArray());
        Assert.Equal(0, replaced.Length);
        Assert.True(replaced.CanWrite);
        final.Position = 0;
        var model = await ScheduleSerializer.Deserialize(final, CancellationToken.None);
        Assert.Equal("cache identity", model.Hash);
        Assert.Equal("New", Assert.Single(model.Courses).Names[0]);
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public async Task InvalidSettersLeavePriorConfigurationIntact()
    {
        using var stream = new MemoryStream();
        using var readOnly = new MemoryStream([], writable: false);
        var builder = ScheduleSerializer.Serialize().Input(Schedule.Empty).Output(stream).Hash("identity");
        Assert.Throws<ArgumentNullException>(() => builder.Input(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Output((Stream) null!));
        Assert.Throws<ArgumentException>(() => builder.Output(readOnly));
        Assert.Throws<ArgumentNullException>(() => builder.Output((string) null!));
        Assert.Throws<ArgumentException>(() => builder.Output(" "));
        Assert.Throws<ArgumentException>(() => builder.Output("\0"));
        Assert.Throws<ArgumentNullException>(() => builder.Hash(null!));
        await builder.ExecuteAsync();
        stream.Position = 0;
        Assert.Equal("identity", (await ScheduleSerializer.Deserialize(stream, CancellationToken.None)).Hash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReexecutionRetainsInputOutputAndHash(bool usePath)
    {
        using var temp = new TempDirectory();
        using var stream = new MemoryStream();
        var builder = ScheduleSerializer.Serialize().Input(CreateSchedule("Course")).Hash("identity");
        if (usePath)
        {
            builder.Output(temp.FilePath);
        }
        else
        {
            builder.Output(stream);
        }
        await builder.ExecuteAsync();
        var first = usePath ? await File.ReadAllBytesAsync(temp.FilePath) : stream.ToArray();
        stream.Position = 0;
        stream.SetLength(0);
        await builder.ExecuteAsync();
        var second = usePath ? await File.ReadAllBytesAsync(temp.FilePath) : stream.ToArray();
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrecancelledExecutionDoesNotTouchOutputAndCanBeRetried(bool usePath)
    {
        using var temp = new TempDirectory();
        var previous = Encoding.UTF8.GetBytes("previous complete output");
        await File.WriteAllBytesAsync(temp.FilePath, previous);
        using var stream = new MemoryStream();
        stream.Write(previous);
        var builder = ScheduleSerializer.Serialize().Input(Schedule.Empty);
        if (usePath)
        {
            builder.Output(temp.FilePath);
        }
        else
        {
            builder.Output(stream);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.ExecuteAsync(cancellation.Token));
        Assert.Equal(previous, await File.ReadAllBytesAsync(temp.FilePath));
        Assert.Equal(previous, stream.ToArray());
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.tmp"));
        stream.Position = 0;
        stream.SetLength(0);
        await builder.ExecuteAsync();
        Assert.True(stream.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteFailureOrCancellationPropagatesAndLeavesCallerStreamOpen(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new InterruptedStream(cancellation, cancel);
        var builder = ScheduleSerializer.Serialize().Input(Schedule.Empty).Output(stream);
        if (cancel)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                builder.ExecuteAsync(cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<IOException>(() => builder.ExecuteAsync());
            Assert.Same(stream.Error, error);
        }
        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);
        using var retry = new MemoryStream();
        await builder.Output(retry).ExecuteAsync();
    }

    [Fact]
    public async Task StreamOutputPreservesPositionAndDoesNotTruncate()
    {
        using var expected = new MemoryStream();
        await ScheduleSerializer.Serialize().Input(Schedule.Empty).Output(expected).ExecuteAsync();
        var prefix = Encoding.UTF8.GetBytes("prefix");
        var suffix = Encoding.UTF8.GetBytes("suffix");
        using var stream = new MemoryStream();
        stream.Write(prefix);
        stream.Write(new byte[expected.Length]);
        stream.Write(suffix);
        stream.Position = prefix.Length;
        var builder = ScheduleSerializer.Serialize().Input(Schedule.Empty).Output(stream);
        await builder.ExecuteAsync();
        Assert.Equal(prefix.Concat(expected.ToArray()).Concat(suffix).ToArray(), stream.ToArray());
        // Disposing a previously configured stream still fails at execution.
        stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => builder.ExecuteAsync());
    }

    [Fact]
    public async Task FailedPublicationCleansStagingFileAndAllowsRetry()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.FilePath);
        var sentinel = Path.Combine(temp.FilePath, "previous");
        await File.WriteAllTextAsync(sentinel, "previous complete output");
        var builder = ScheduleSerializer.Serialize().Input(Schedule.Empty).Output(temp.FilePath);
        await Assert.ThrowsAnyAsync<IOException>(() => builder.ExecuteAsync());
        Assert.Equal("previous complete output", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(Directory.EnumerateFiles(temp.DirectoryPath, "*.tmp"));
        File.Delete(sentinel);
        Directory.Delete(temp.FilePath);
        await builder.ExecuteAsync();
        using var exclusive = File.Open(temp.FilePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.NotNull(await ScheduleSerializer.Deserialize(exclusive, CancellationToken.None));
    }

    private static Schedule CreateSchedule(string course)
    {
        var builder = new ScheduleBuilder();
        builder.Course(course);
        return builder.Build();
    }

    private sealed class InterruptedStream(CancellationTokenSource cancellation, bool cancel) : MemoryStream
    {
        public IOException Error
        {
            get;
        } = new("write interrupted");

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer[..1], cancellationToken);
            if (cancel)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw Error;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string DirectoryPath
        {
            get;
        } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public string FilePath => Path.Combine(DirectoryPath, "schedule.json");

        public TempDirectory() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
