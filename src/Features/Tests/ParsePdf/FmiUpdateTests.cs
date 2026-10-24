using System.Text;
using FmiScheduleImport;
using FmiWebsiteInterop;

public sealed class FmiUpdateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
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
            Assert.Equal(expected: 3, actual: update.Downloads.Count);
            global::System.DateOnly expected2 = new DateOnly(year: 2026, month: 9, day: 25);
            global::System.DateOnly actual2 = update.PeriodStart;
            Assert.Equal(expected: expected2, actual: actual2);
            return Task.CompletedTask;
        }
        Assert.True((await updater.RunAsync(State, Consume)).Updated);
        var checkpoint = await File.ReadAllTextAsync(State);
        Assert.False((await updater.RunAsync(State, Consume)).Updated);
        Assert.Equal(expected: checkpoint, actual: await File.ReadAllTextAsync(State));
        downloads[1] = downloads[1] with { Content = Encoding.UTF8.GetBytes("changed bytes") };
        Assert.True((await updater.RunAsync(State, Consume)).Updated);
        Assert.Equal(expected: 2, actual: calls);
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
        Assert.Equal(expected: checkpoint, actual: await File.ReadAllTextAsync(State));
        Assert.True((await updater.RunAsync(State, (_, _) => Task.CompletedTask)).Updated);
    }

    [Fact]
    public async Task FirstFailureAndIncompleteDownloadDoNotCreateCheckpoint()
    {
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads()).RunAsync(State, (_, _) => throw new FormatException("bad PDF")));
        bool condition = File.Exists(State);
        Assert.False(condition);
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads()[..2]).RunAsync(State, (_, _) => Task.CompletedTask));
        bool condition2 = File.Exists(State);
        Assert.False(condition2);
    }

    [Fact]
    public async Task UsesLatestChangedUploadDateAndTodayForMissingDates()
    {
        var downloads = Downloads(new(year: 2026, month: 9, day: 17));
        var updater = Create(() => downloads);
        global::System.DateOnly CalculateExpected()
        {
            global::System.DateOnly expected2 = new DateOnly(year: 2026, month: 9, day: 17);
            return expected2;
        }

        Assert.Equal(expected: CalculateExpected(), actual: (await updater.RunAsync(State, (_, _) => Task.CompletedTask)).PeriodStart);
        downloads[0] = downloads[0] with { Content = [42], Link = downloads[0].Link with { UploadDate = new(year: 2026, month: 9, day: 23) } };
        global::System.DateOnly CalculateExpected2()
        {
            global::System.DateOnly expected3 = new DateOnly(year: 2026, month: 9, day: 23);
            return expected3;
        }

        Assert.Equal(expected: CalculateExpected2(), actual: (await updater.RunAsync(State, (_, _) => Task.CompletedTask)).PeriodStart);
        downloads[1] = downloads[1] with { Content = [43], Link = downloads[1].Link with { UploadDate = null } };
        global::System.DateOnly CalculateExpected3()
        {
            global::System.DateOnly expected4 = new DateOnly(year: 2026, month: 9, day: 25);
            return expected4;
        }

        Assert.Equal(expected: CalculateExpected3(), actual: (await updater.RunAsync(State, (_, _) => Task.CompletedTask)).PeriodStart);
    }

    [Fact]
    public async Task CancellationDoesNotCommitHashes()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(() => Downloads()).RunAsync(State,
            (_, _) => { cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        bool condition = File.Exists(State);
        Assert.False(condition);
    }

    [Fact]
    public async Task InvalidCheckpointDoesNotCallConsumer()
    {
        Directory.CreateDirectory(_directory);
        const string invalid = "{\"Version\":1,\"PeriodStart\":\"2026-09-17\",\"Sources\":[]}";
        await File.WriteAllTextAsync(path: State, contents: invalid);
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads()).RunAsync(State,
            (_, _) => throw new Exception("Consumer must not be called")));
        Assert.Equal(expected: invalid, actual: await File.ReadAllTextAsync(State));
    }

    [Fact]
    public async Task FutureUploadDateDoesNotCreateCheckpoint()
    {
        await Assert.ThrowsAsync<FormatException>(() => Create(() => Downloads(new(year: 2026, month: 9, day: 26))).RunAsync(State,
            (_, _) => throw new Exception("Consumer must not be called")));
        bool condition = File.Exists(State);
        Assert.False(condition);
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
        Assert.Equal(expected: 3, actual: links.Count);
        global::System.Uri expected2 = new Uri("https://fmi.usm.md/one.pdf");
        global::System.Uri actual2 = links[0].Url;
        Assert.Equal(expected: expected2, actual: actual2);
        global::System.DateOnly expected3 = new DateOnly(year: 2026, month: 9, day: 17);
        global::System.DateOnly? expected4 = expected3;
        global::System.DateOnly? actual3 = links[0].UploadDate;
        Assert.Equal(expected: expected4, actual: actual3);
        global::System.DateOnly expected5 = new DateOnly(year: 2026, month: 9, day: 18);
        global::System.DateOnly? expected6 = expected5;
        global::System.DateOnly? actual4 = links[1].UploadDate;
        Assert.Equal(expected: expected6, actual: actual4);
        Assert.Null(links[2].UploadDate);
    }

    [Fact]
    public void RejectsAmbiguousOrMissingSchedules()
    {
        Assert.Throws<FormatException>(() => FmiScheduleClient.Discover("Anul I Licenţă: <a href='/one.pdf'>one</a>", FmiScheduleClient.SchedulePage));
        Assert.Throws<FormatException>(() => FmiScheduleClient.Discover("Anul I Licenţă: <a href='/one.pdf'>one</a> Anul I Licenţă: <a href='/two.pdf'>two</a>", FmiScheduleClient.SchedulePage));
    }

    private static FmiScheduleUpdater Create(Func<FmiScheduleDownload[]> get) => new(_ => Task.FromResult<IReadOnlyList<FmiScheduleDownload>>(get()), new FixedClock());
    private static FmiScheduleDownload[] Downloads(DateOnly? date = null)
    {
        return Enumerable.Range(start: 1, count: 3)
            .Select(year =>
            {
                int year2 = year;
                string uriString = $"https://fmi.usm.md/orar{year}.pdf";
                global::System.Uri url = new Uri(uriString);
                global::System.DateOnly? uploadDate = date;
                global::FmiWebsiteInterop.FmiScheduleLink link = new(year2, url, uploadDate);
                byte[] content = [(byte) year];
                global::FmiWebsiteInterop.FmiScheduleDownload fmiScheduleDownload = new FmiScheduleDownload(link, content);
                return fmiScheduleDownload;
            }).ToArray();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(year: 2026, month: 9, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string CalculatePath2()
    {
        string toString = Guid.NewGuid().ToString("N");
        string text = $"schedule-update-tests-{toString}";
        string path2 = (string) text;
        return path2;
    }
}
