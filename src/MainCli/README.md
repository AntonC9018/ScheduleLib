Run from Windows or Linux with the installed .NET SDK:

```sh
dotnet run --project src/MainCli -- query lessons
dotnet run --project src/MainCli -- query lessons --room 423/4 --day Tuesday --parity OddWeek --before-slot 6 --json
```

Empty arguments and `--help` show help without loading schedules. Query filters
are optional. The query lists latest-period weekly lessons; `--before-slot` is
an inclusive cutoff using slots numbered from 1 to 7. A parity filter includes
lessons held every week. An unknown room is an argument error; a valid empty
query succeeds.

Academic year, semester and supported configuration stay in `AppConfiguration`
and `DefaultConfig`. Packaged `data` is resolved relative to the executable;
`--data-dir` selects another source root relative to the invocation directory,
with the same `YEAR_semN/zi|fr` structure. The process working directory is
unchanged. The first query slice always bypasses the cache (`--no-cache` is
accepted); shared caching follows in #187. Production teacher-name enrichment
retains its read-only website request. Query requires no profile or cloud login.

`--json` emits a single schema-version-1 envelope with `command`, `runId`,
`status`, `exitCode`, `outputs`, `actions`, `warnings`, `errors` and `data`.
Diagnostics and progress go to stderr. Exit codes are 0 for success, 1 for
unexpected failures, 2 for invalid arguments, 3 for missing/invalid input,
5 for I/O failures, 8 for unsupported capabilities and 130 for cancellation.
Ctrl+C cancels initialization and query enumeration.

DOCX/XLSX parsing is portable. Legacy `.doc` conversion requires Windows and
Microsoft Word; Linux returns the capability error. Conversion operates on
staged copies and preserves original sources. Windows Word conversion is not
validated by the Linux fixture suite.

Focused checks:

```sh
dotnet test src/MainCli/Tests
```

The fixtures use existing third-year DOCX and master XLSX resources, disable
website enrichment in the test service configuration, and do not write caches.
The first-year 2025 semester-2 source has an existing invalid IA2502 subgroup
prefix, so it is not used as this query's valid fixture.
