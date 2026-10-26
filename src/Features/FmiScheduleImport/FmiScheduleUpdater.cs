using System.Security.Cryptography;
using System.Text.Json;
using FmiWebsiteInterop;

namespace FmiScheduleImport;

public sealed record ScheduleSourceHash(
    int Year,
    string Url,
    string Sha256,
    DateOnly? UploadDate);

public sealed record ScheduleUpdateState(int Version, DateOnly PeriodStart, IReadOnlyList<ScheduleSourceHash> Sources);

public sealed record ScheduleUpdate(
    DateOnly PeriodStart,
    IReadOnlyList<FmiScheduleDownload> Downloads,
    IReadOnlyList<ScheduleSourceHash> Sources);

public sealed record ScheduleUpdateResult(bool Updated, DateOnly PeriodStart);

/// <summary>Commits the hash checkpoint only after the consumer successfully imports and exports the complete snapshot.</summary>
public sealed class FmiScheduleUpdater(
    Func<CancellationToken, Task<IReadOnlyList<FmiScheduleDownload>>> download,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
    };

    public async Task<ScheduleUpdateResult> RunAsync(
        string stateFile,
        Func<ScheduleUpdate, CancellationToken, Task> importAndExport,
        CancellationToken cancellationToken = default)
    {
        stateFile = Path.GetFullPath(stateFile);

        string CalculatePath()
        {
            string path = Path.GetDirectoryName(stateFile)!;
            return path;
        }

        Directory.CreateDirectory(CalculatePath());

        // FileShare.None rejects concurrent writers before either starts downloading.
        string CalculatePath2()
        {
            string text = $"{stateFile}.lock";
            string path2 = text;
            return path2;
        }

        await using var gate = new FileStream(CalculatePath2(),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        ScheduleUpdateState? previous = null;
        if (File.Exists(stateFile))
        {
            await using var input = File.OpenRead(stateFile);
            previous = await JsonSerializer.DeserializeAsync<ScheduleUpdateState>(input,
                    Json,
                    cancellationToken)
                ?? throw new FormatException("Empty schedule hash checkpoint.");

            bool CheckCondition()
            {
                if (previous.Version != 1)
                {
                    return true;
                }

                if (previous.Sources is null)
                {
                    return true;
                }

                if (!previous.Sources.Select(s => s.Year)
                        .Order()
                        .SequenceEqual(new[]
                        {
                            1,
                            2,
                            3,
                        }))
                {
                    return true;
                }

                if (previous.Sources.Any(s => s.Sha256 is not { Length: 64 }
                        || s.Sha256.Any(c => !char.IsAsciiHexDigit(c))))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
            {
                throw new FormatException("Invalid or unsupported schedule hash checkpoint.");
            }
        }
        var downloads = await download(cancellationToken);
        if (!downloads.Select(d => d.Link.Year)
                .Order()
                .SequenceEqual(new[]
                {
                    1,
                    2,
                    3,
                }))
        {
            throw new FormatException("An update requires all three bachelor schedules.");
        }

        ScheduleSourceHash ProjectValue(FmiScheduleDownload d)
        {
            int operationValue = d.Link.Year;
            string operationValue2 = d.Link.Url.AbsoluteUri;
            byte[] operationValue3 = SHA256.HashData(d.Content);
            byte[] operationValue4 = operationValue3;
            string operationValue5 = Convert.ToHexStringLower(operationValue4);
            string operationValue6 = operationValue5;
            var operationValue7 = d.Link.UploadDate;
            var operationValue8 = new ScheduleSourceHash(operationValue,
                Url: operationValue2,
                Sha256: operationValue6,
                operationValue7);
            return operationValue8;
        }

        var sources = downloads.Select(ProjectValue)
            .ToArray();
        var changed = sources.Where(s => !string.Equals(
                previous?.Sources.SingleOrDefault(p => p.Year == s.Year)?.Sha256,
                s.Sha256,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (changed.Length == 0 && previous?.Sources.Count == sources.Length)
        {
            return new(false, previous.PeriodStart);
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        // A single period represents this complete snapshot. Each changed source
        // contributes its upload date, or today when the site supplies none.
        var period = changed.Select(s => s.UploadDate ?? today)
            .DefaultIfEmpty(today)
            .Max();
        if (period > today)
        {
            throw new FutureScheduleUploadDateException(period);
        }

        await importAndExport(new(period,
                downloads,
                sources),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = $"{stateFile}.{Guid.NewGuid().ToString("N")}.tmp";
        try
        {
            await using (var output = new FileStream(temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(output,
                    new ScheduleUpdateState(1,
                        period,
                        sources),
                    Json,
                    cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(sourceFileName: temporary,
                destFileName: stateFile,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        return new(true, period);
    }
}
