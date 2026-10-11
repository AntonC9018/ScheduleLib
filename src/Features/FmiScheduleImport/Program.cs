using FmiScheduleImport;
using FmiWebsiteInterop;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--strict"))
{
    Console.Error.WriteLine("Usage: FmiScheduleImport <hashes.json> <output-root> [--strict]");
    return 1;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("ScheduleLib/1.0");
var client = new FmiScheduleClient(http);
var updater = new FmiScheduleUpdater(client.DownloadAsync);
string? generated = null;
var result = await updater.RunAsync(args[0], async (update, token) =>
{
    var root = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(root);
    var staging = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
    await FmiPdfImport.GenerateAsync(update, staging, args.Contains("--strict"), token);
    generated = Path.Combine(root, $"fmi-{update.PeriodStart:yyyy-MM-dd}-{Guid.NewGuid():N}");
    Directory.Move(staging, generated);
}, cancellation.Token);
Console.WriteLine(result.Updated
    ? $"Imported period {result.PeriodStart:yyyy-MM-dd} into {generated}. Hash checkpoint saved."
    : $"Schedules unchanged. Skipped import; previous period is {result.PeriodStart:yyyy-MM-dd}.");
return 0;
