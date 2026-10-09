using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;

namespace ScheduleLib.Cli;

public static class DriveBundle
{
    public static async Task<IReadOnlyList<DriveArtifact>> Generate(IServiceProvider services, RunOutput output,
        List<string> warnings, CancellationToken token)
    {
        var schedule = services.LatestPeriodSchedule();
        warnings.Add("Teacher workbook, PDFs and ICS use the latest period and all groups/partitions/teachers; the selected profile is not an export filter. Free rooms inspect every weekly period.");
        await output.Publish("all-teachers.xlsx", async (stream, ct) => await services.GetRequiredService<GenerateAllTeachersExcelTaskHandler>().Run(new()
        { Schedule = schedule, CancellationToken = ct, OutputDirectory = stream }), token);
        await output.Publish("free-rooms.xlsx", async (stream, ct) => await services.GetRequiredService<GenerateFreeRoomsTaskHandler>().Run(new()
        { CancellationToken = ct, OutputStream = stream }), token);
        await services.GetRequiredService<GeneratePdfsForGroupsAndTeachersTaskHandler>().Run(new()
        { CancellationToken = token, PublishArtifact = output.Publish });
        await services.GetRequiredService<GenerateIcsCalendarsTaskHandler>().RunAsync(new()
        { CancellationToken = token, PublishArtifact = output.Publish, ReportOmission = warnings.Add });
        token.ThrowIfCancellationRequested();
        // Only this invocation's successfully published schedule artifacts, never directory enumeration or prior manifest entries.
        return output.PublishedPaths.Where(x => DriveSync.IsScheduleArtifact(Path.GetFileName(x)))
            .Select(x => new DriveArtifact(Path.GetFileName(x), x)).ToArray();
    }
}
