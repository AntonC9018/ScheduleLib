using FmiScheduleImport;
using FmiWebsiteInterop;

bool conditionResult;
if (args.Length is < 2 or > 3)
{
    conditionResult = true;
}
else
{
    if (args.Length == 3)
    {
        conditionResult = args[2] != "--strict";
    }
    else
    {
        conditionResult = false;
    }
}

if (conditionResult)
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
var result = await updater.RunAsync(args[0],
    async (update, token) =>
    {
        var root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        string path1 = root;
        string toString = Guid.NewGuid()
            .ToString("N");
        string text = $".import-{toString}";
        string path2 = text;
        var staging = Path.Combine(path1, path2);

        bool CalculateStrict()
        {
            bool strict = args.Contains("--strict");
            return strict;
        }

        await FmiPdfImport.GenerateAsync(update,
            staging,
            CalculateStrict(),
            token);
        string path12 = root;
        string path22 = $"fmi-{update.PeriodStart:yyyy-MM-dd}-{Guid.NewGuid():N}";
        string combine = Path.Combine(path12, path22);
        generated = combine;
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
