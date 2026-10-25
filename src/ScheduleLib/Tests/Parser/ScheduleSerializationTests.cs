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
        global::ScheduleLib.ScheduleSerializationBuilder serialize = ScheduleSerializer.Serialize();
        global::ScheduleLib.Schedule schedule = CreateSchedule("Course");
        var builder = serialize.Input(schedule);
        if (usePath)
        {
            builder.Output(temp.FilePath);
            bool condition = File.Exists(temp.FilePath);
            Assert.False(condition);
        }
        else
        {
            builder.Output(stream);
        }
        await builder.ExecuteAsync();
        var bytes = usePath ? await File.ReadAllBytesAsync(temp.FilePath) : stream.ToArray();
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(expected: new[]
            {
                "Hash",
                "OneTimeLessons",
                "RegularLessons",
                "Groups",
                "Teachers",
                "Courses",
                "Periods",
            }.Order(),
            actual: json.RootElement.EnumerateObject()
                .Select(x => x.Name)
                .Order());
        using var input = new MemoryStream(bytes);
        var model = await ScheduleSerializer.Deserialize(input, CancellationToken.None);
        Assert.Equal(expected: "", actual: model.Hash);
        var restored = new ScheduleBuilder();
        ScheduleSerializer.AddToBuilder(restored, model);
        var rebuilt = restored.Build();
        Assert.Equal(expected: "Course", actual: Assert.Single(rebuilt.Courses).FullName);
        using var second = new MemoryStream();
        await ScheduleSerializer.Serialize()
            .Input(rebuilt)
            .Output(second)
            .ExecuteAsync();
        Assert.Equal(expected: bytes, actual: second.ToArray());
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
        Assert.Equal(expected: missing, actual: error.Configuration);
        Assert.Equal(expected: 0, actual: stream.Length);
        // The same builder remains usable after correcting its missing configuration.
        await builder.Input(Schedule.Empty)
            .Output(stream)
            .ExecuteAsync();
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task LastSuccessfulSetterWinsAndExecutionRetainsConfiguration()
    {
        using var temp = new TempDirectory();
        using var replaced = new MemoryStream();
        using var final = new MemoryStream();
        global::ScheduleLib.ScheduleSerializationBuilder serialize = ScheduleSerializer.Serialize();
        global::ScheduleLib.Schedule schedule = CreateSchedule("Old");
        global::ScheduleLib.ScheduleSerializationBuilder input = serialize.Input(schedule);
        global::ScheduleLib.Schedule schedule2 = CreateSchedule("New");
        global::ScheduleLib.ScheduleSerializationBuilder input2 = input.Input(schedule2);
        global::System.IO.Stream stream = replaced;
        global::ScheduleLib.ScheduleSerializationBuilder output = input2.Output(stream);
        string path2 = temp.FilePath;
        global::ScheduleLib.ScheduleSerializationBuilder output2 = output.Output(path2);
        string hash = "old";
        global::ScheduleLib.ScheduleSerializationBuilder hash2 = output2.Hash(hash);
        string hash3 = "cache identity";
        var builder = hash2.Hash(hash3);
        await builder.ExecuteAsync();
        var first = await File.ReadAllBytesAsync(temp.FilePath);
        // A shorter second result must replace rather than leave trailing bytes.
        await builder.Input(Schedule.Empty)
            .Hash("")
            .ExecuteAsync();
        global::System.IO.FileInfo fileInfo = new FileInfo(temp.FilePath);
        bool condition = fileInfo.Length < first.Length;
        Assert.True(condition);
        global::ScheduleLib.Schedule CalculateSchedule()
        {
            global::ScheduleLib.Schedule schedule3 = CreateSchedule("New");
            return schedule3;
        }

        await builder.Input(CalculateSchedule())
            .Hash("cache identity")
            .Output(final)
            .ExecuteAsync();
        Assert.Equal(expected: first, actual: final.ToArray());
        Assert.Equal(expected: 0, actual: replaced.Length);
        Assert.True(replaced.CanWrite);
        final.Position = 0;
        var model = await ScheduleSerializer.Deserialize(final, CancellationToken.None);
        Assert.Equal(expected: "cache identity", actual: model.Hash);
        Assert.Equal(expected: "New", actual: Assert.Single(model.Courses).Names[0]);
        global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(path: temp.DirectoryPath, searchPattern: "*.tmp");
        global::System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
    }

    [Fact]
    public async Task InvalidSettersLeavePriorConfigurationIntact()
    {
        using var stream = new MemoryStream();
        using var readOnly = new MemoryStream([], writable: false);
        var builder = ScheduleSerializer.Serialize()
            .Input(Schedule.Empty)
            .Output(stream)
            .Hash("identity");
        Assert.Throws<ArgumentNullException>(() => builder.Input(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Output((Stream) null!));
        Assert.Throws<ArgumentException>(() => builder.Output(readOnly));
        Assert.Throws<ArgumentNullException>(() => builder.Output((string) null!));
        Assert.Throws<ArgumentException>(() => builder.Output(" "));
        Assert.Throws<ArgumentException>(() => builder.Output("\0"));
        Assert.Throws<ArgumentNullException>(() => builder.Hash(null!));
        await builder.ExecuteAsync();
        stream.Position = 0;
        Assert.Equal(expected: "identity", actual: (await ScheduleSerializer.Deserialize(stream, CancellationToken.None)).Hash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReexecutionRetainsInputOutputAndHash(bool usePath)
    {
        using var temp = new TempDirectory();
        using var stream = new MemoryStream();
        global::ScheduleLib.ScheduleSerializationBuilder serialize = ScheduleSerializer.Serialize();
        global::ScheduleLib.Schedule schedule = CreateSchedule("Course");
        global::ScheduleLib.ScheduleSerializationBuilder input = serialize.Input(schedule);
        string hash = "identity";
        var builder = input.Hash(hash);
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
        Assert.Equal(expected: first, actual: second);
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
        var builder = ScheduleSerializer.Serialize()
            .Input(Schedule.Empty);
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
        Assert.Equal(expected: previous, actual: await File.ReadAllBytesAsync(temp.FilePath));
        Assert.Equal(expected: previous, actual: stream.ToArray());
        global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(path: temp.DirectoryPath, searchPattern: "*.tmp");
        global::System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
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
        var builder = ScheduleSerializer.Serialize()
            .Input(Schedule.Empty)
            .Output(stream);
        if (cancel)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                builder.ExecuteAsync(cancellation.Token));
            Assert.Equal(expected: cancellation.Token, actual: error.CancellationToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<IOException>(() => builder.ExecuteAsync());
            Assert.Same(expected: stream.Error, actual: error);
        }
        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);
        using var retry = new MemoryStream();
        await builder.Output(retry)
            .ExecuteAsync();
    }

    [Fact]
    public async Task StreamOutputPreservesPositionAndDoesNotTruncate()
    {
        using var expected = new MemoryStream();
        await ScheduleSerializer.Serialize()
            .Input(Schedule.Empty)
            .Output(expected)
            .ExecuteAsync();
        var prefix = Encoding.UTF8.GetBytes("prefix");
        var suffix = Encoding.UTF8.GetBytes("suffix");
        using var stream = new MemoryStream();
        stream.Write(prefix);
        stream.Write(new byte[expected.Length]);
        stream.Write(suffix);
        stream.Position = prefix.Length;
        var builder = ScheduleSerializer.Serialize()
            .Input(Schedule.Empty)
            .Output(stream);
        await builder.ExecuteAsync();
        Assert.Equal(expected: prefix.Concat(expected.ToArray())
            .Concat(suffix)
            .ToArray(), actual: stream.ToArray());
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
        await File.WriteAllTextAsync(path: sentinel, contents: "previous complete output");
        var builder = ScheduleSerializer.Serialize()
            .Input(Schedule.Empty)
            .Output(temp.FilePath);
        await Assert.ThrowsAnyAsync<IOException>(() => builder.ExecuteAsync());
        Assert.Equal(expected: "previous complete output", actual: await File.ReadAllTextAsync(sentinel));
        global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(path: temp.DirectoryPath, searchPattern: "*.tmp");
        global::System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
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
        public IOException Error { get; } = new("write interrupted");

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
