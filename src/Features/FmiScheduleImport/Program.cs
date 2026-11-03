using FmiScheduleImport;
using FmiWebsiteInterop;

bool IsInvalidUsage()
{
    if (args.Length is < 2 or > 3)
    {
        return true;
    }

    if (args.Length == 3)
    {
        if (args[2] != "--strict")
        {
            return true;
        }
    }
    return false;
}

if (IsInvalidUsage())
{
    Console.Error.WriteLine("Usage: FmiScheduleImport <hashes.json> <output-root> [--strict]");
    return 1;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
using var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60),
};
http.DefaultRequestHeaders.UserAgent.ParseAdd("ScheduleLib/1.0");
var client = new FmiScheduleClient(http);
var updater = new FmiScheduleUpdater(client.DownloadAsync);
string? generated = null;
var result = await updater.RunAsync(
    args[0],
    async (update, token) =>
    {
        var root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        var stagingName = $".import-{Guid.NewGuid():N}";
        var staging = Path.Combine(root, stagingName);

        await FmiPdfImport.GenerateAsync(
            update,
            staging,
            args.Contains("--strict"),
            token);
        var snapshotName = $"fmi-{update.PeriodStart:yyyy-MM-dd}-{Guid.NewGuid():N}";
        generated = Path.Combine(root, snapshotName);
        Directory.Move(sourceDirName: staging, destDirName: generated);
    },
    cancellation.Token);
string value;
if (result.Updated)
{
    string text2 = $"Imported period {result.PeriodStart:yyyy-MM-dd} into {generated}. Hash checkpoint saved.";
    value = text2;
}
else
{
    string text3 = $"Schedules unchanged. Skipped import; previous period is {result.PeriodStart:yyyy-MM-dd}.";
    value = text3;
}

Console.WriteLine(value);
return 0;
