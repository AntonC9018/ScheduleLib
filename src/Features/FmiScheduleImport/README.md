# FMI schedule update runner

This C# module downloads the three regular bachelor schedules from
https://fmi.usm.md/orar/, detects changes, imports PDFs and generates the existing
group, partition and teacher documents plus `schedule.json`.

Run from the repository root:

```bash
dotnet run --project src/Features/FmiScheduleImport -- \
  work/fmi-hashes.json output/fmi
```

Append `--strict` to reject overlapping lessons. The default retains and reports
source overlaps, then reruns all other model validation. Parsing errors always
fail the update. The runner does not publish to Drive.

## Update behavior

`FmiScheduleClient` in FmiWebsiteInterop discovers links with AngleSharp and
fetches PDF bytes with the supplied `HttpClient`. Missing or ambiguous bachelor
links, HTTP failures and non-PDF responses fail the run.

`FmiScheduleUpdater` computes SHA-256 hashes and compares them by bachelor year
with the previous successful snapshot in the supplied JSON file. The first run
imports everything. If all hashes match, it returns without parsing, generating
documents, changing the period or rewriting the checkpoint. If any hash changes,
it imports all three PDFs to produce a complete schedule snapshot.

The period is today's local date by default. An explicit upload/update date
attached to a changed schedule link can replace today. Supported website metadata
is `data-upload-date`, a nested `time[datetime]`, or labelled uploaded/încărcat/
actualizat text in the link or its title. Semester date ranges, page-wide dates,
URL year/month folders, HTTP modification times and PDF creation metadata are not
used as upload dates. For multiple changed PDFs, the latest of their upload dates
or today fallbacks becomes the common period. Future dates are rejected.

The updater accepts a download delegate, an import/export callback and an optional
`TimeProvider`, so it can be used by another C# host and tested without the network.

```csharp
var client = new FmiScheduleClient(httpClient);
var updater = new FmiScheduleUpdater(client.DownloadAsync);
await updater.RunAsync(hashFile, (update, token) =>
    FmiPdfImport.GenerateAsync(update, freshOutputDirectory, cancellationToken: token));
```

A file lock prevents concurrent updates using the same checkpoint. New hashes
are written to a temporary file, flushed and atomically moved into place only
after the callback succeeds. Failed downloads, parsing, generation or cancellation
leave the prior checkpoint intact, so the next run retries the change. An invalid
checkpoint fails explicitly rather than being treated as a first run.

`Program.cs` generates in a unique staging directory and renames it after success.
Successful directories are named `fmi-yyyy-MM-dd-<id>` under the output root, allowing
multiple revisions on the same day. Failed output may remain in `.import-<id>` for
diagnosis. Each successful directory contains the source PDFs, normalized cells,
source hashes, an import report, schedule JSON and generated PDFs.

## Verification

The September 25 live run produced 494 cells, 31 groups, 84 teachers, 541 lessons
and 194 PDFs across 209 pages. FMI changed the third-year PDF since September 21:
IA2402's Cloud lab now occupies an odd-week Monday slot and an even-week Tuesday
slot. A second run detected identical hashes and skipped generation.

One source conflict remains: I2501 has Web development and optional pedagogy
at 15:00 on odd Tuesdays. It is preserved and reported by default.
