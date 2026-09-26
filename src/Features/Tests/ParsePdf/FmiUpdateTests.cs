using System.Text;
using FmiScheduleImport;
using FmiWebsiteInterop;

public sealed class FmiUpdateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "schedule-update-tests-" + Guid.NewGuid().ToString("N"));
    private string State => Path.Combine(_directory, "hashes.json");

    [Fact]
    public async Task FirstRunImportsUnchangedSkipsAndChangedReimportsWholeSnapshot()
    {
        var downloads = Downloads();
        var updater = Create(() => downloads);
        var calls = 0;
        Task Consume(ScheduleUpdate update, CancellationToken token)
        {
            calls++;
            Assert.Equal(3, update.Downloads.Count);
            Assert.Equal(new DateOnly(2026, 9, 25), update.PeriodStart);
            return Task.CompletedTask;
        }
        Assert.True((await updater.RunAsync(State, Consume)).Updated);
        var checkpoint = await File.ReadAllTextAsync(State);
        Assert.False((await updater.RunAsync(State, Consume)).Updated);
        Assert.Equal(checkpoint, await File.ReadAllTextAsync(State));
        downloads[1] = downloads[1] with { Content = Encoding.UTF8.GetBytes("changed bytes") };
        Assert.True((await updater.RunAsync(State, Consume)).Updated);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedConsumerLeavesHashesUnchangedAndRetries()
    {
        var downloads = Downloads();
        var updater = Create(() => downloads);
        await updater.RunAsync(State, (_, _) => Task.CompletedTask);
        var checkpoint = await File.ReadAllTextAsync(State);
        downloads[0] = downloads[0] with { Content = [42] };
        await Assert.ThrowsAsync<IOException>(() => updater.RunAsync(State, (_, _) => throw new IOException("generation failed")));
        Assert.Equal(checkpoint, await File.ReadAllTextAsync(State));
        Assert.True((await updater.RunAsync(State, (_, _) => Task.CompletedTask)).Updated);
    }

    [Fact]
    public async Task FirstFailureAndIncompleteDownloadDoNotCreateCheckpoint()
    {
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads()).RunAsync(State, (_, _) => throw new FormatException("bad PDF")));
        Assert.False(File.Exists(State));
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads()[..2]).RunAsync(State, (_, _) => Task.CompletedTask));
        Assert.False(File.Exists(State));
    }

    [Fact]
    public async Task UsesLatestChangedUploadDateAndTodayForMissingDates()
    {
        var downloads = Downloads(new(2026, 9, 17));
        var updater = Create(() => downloads);
        Assert.Equal(new DateOnly(2026, 9, 17), (await updater.RunAsync(State, (_, _) => Task.CompletedTask)).PeriodStart);
        downloads[0] = downloads[0] with { Content = [42], Link = downloads[0].Link with { UploadDate = new(2026, 9, 23) } };
        Assert.Equal(new DateOnly(2026, 9, 23), (await updater.RunAsync(State, (_, _) => Task.CompletedTask)).PeriodStart);
        downloads[1] = downloads[1] with { Content = [43], Link = downloads[1].Link with { UploadDate = null } };
        Assert.Equal(new DateOnly(2026, 9, 25), (await updater.RunAsync(State, (_, _) => Task.CompletedTask)).PeriodStart);
    }

    [Fact]
    public async Task CancellationDoesNotCommitHashes()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(() => Downloads()).RunAsync(State,
            (_, _) => { cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        Assert.False(File.Exists(State));
    }

    [Fact]
    public async Task InvalidCheckpointDoesNotCallConsumer()
    {
        Directory.CreateDirectory(_directory);
        const string invalid = "{\"Version\":1,\"PeriodStart\":\"2026-09-17\",\"Sources\":[]}";
        await File.WriteAllTextAsync(State, invalid);
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads()).RunAsync(State,
            (_, _) => throw new Exception("Consumer must not be called")));
        Assert.Equal(invalid, await File.ReadAllTextAsync(State));
    }

    [Fact]
    public async Task FutureUploadDateDoesNotCreateCheckpoint()
    {
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads(new(2026, 9, 26))).RunAsync(State,
            (_, _) => throw new Exception("Consumer must not be called")));
        Assert.False(File.Exists(State));
    }

    [Fact]
    public void DiscoversRelativeLinksAndExplicitUploadDatesButNotSemesterDates()
    {
        var links = FmiScheduleClient.Discover("""
            <p>Anul I Licenţă: <a href="/one.pdf" data-upload-date="2026-09-17">01.09.2026 - 14.12.2026</a></p>
            <p>Anul II Licență: <a href="/two.pdf"><time datetime="2026-09-18T10:00:00">upload</time></a></p>
            <p>Anul III Licenţă: <a href="/three.pdf">01.09.2026 - 14.12.2026</a></p>
            Examene: <a href="/exams.pdf">exams</a>
            """, FmiScheduleClient.SchedulePage);
        Assert.Equal(3, links.Count);
        Assert.Equal(new Uri("https://fmi.usm.md/one.pdf"), links[0].Url);
        Assert.Equal(new DateOnly(2026, 9, 17), links[0].UploadDate);
        Assert.Equal(new DateOnly(2026, 9, 18), links[1].UploadDate);
        Assert.Null(links[2].UploadDate);
    }

    [Fact]
    public void RejectsAmbiguousOrMissingSchedules()
    {
        Assert.Throws<FormatException>(() => FmiScheduleClient.Discover("Anul I Licenţă: <a href='/one.pdf'>one</a>", FmiScheduleClient.SchedulePage));
        Assert.Throws<FormatException>(() => FmiScheduleClient.Discover("Anul I Licenţă: <a href='/one.pdf'>one</a> Anul I Licenţă: <a href='/two.pdf'>two</a>", FmiScheduleClient.SchedulePage));
    }

    private static FmiScheduleUpdater Create(Func<FmiScheduleDownload[]> get) => new(_ => Task.FromResult<IReadOnlyList<FmiScheduleDownload>>(get()), new FixedClock());
    private static FmiScheduleDownload[] Downloads(DateOnly? date = null) => Enumerable.Range(1, 3)
        .Select(year => new FmiScheduleDownload(new(year, new Uri($"https://fmi.usm.md/orar{year}.pdf"), date), [(byte)year])).ToArray();

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
