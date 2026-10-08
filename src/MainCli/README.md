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
unchanged. Query and export share a cache in the operating system user state directory
under `ScheduleLib/cache`. `--cache-dir` selects another shared directory;
`--no-cache` bypasses both reads and writes. Production teacher-name enrichment
retains its read-only website request. Query requires no profile or cloud login.

`--json` emits a single schema-version-1 envelope with `command`, `runId`,
`status`, `exitCode`, `outputs`, `actions`, `warnings`, `errors` and `data`.
Diagnostics and progress go to stderr. Exit codes are 0 for success, 1 for
unexpected failures, 2 for invalid arguments, 3 for missing/invalid input,
5 for I/O failures, 6 for partial outputs, 7 for a busy explicit output
destination, 8 for unsupported capabilities and 130 for cancellation.
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


```sh
dotnet run --project src/MainCli -- export teachers-excel --json
dotnet run --project src/MainCli -- export teachers-excel --output ./teacher-export --cache-dir ./cache
```

Teacher export retains the existing latest-period selection and all-teacher
breadth. It reports omitted lessons from other periods. The workbook uses the
existing `main` worksheet, timetable layout and seminar behavior. No teacher
identity or interactive consent is required. Code-defined website enrichment
still reads the existing teacher provider on a cache rebuild; remote website
changes alone cannot invalidate a local cache. `--no-cache` obtains fresh input
without replacing the shared cache.

Default exports use a unique `output/RUN_ID` directory beneath the invocation
root. The result reports the workbook and `schedulelib-manifest.json` paths.
`--output` selects a directory; it is never recursively cleared. Replacement
requires a manifest for the same operation and an unchanged SHA-256 fingerprint
of the old artifact. Unrelated files remain, and filename/manifest collisions
fail with exit 5. Modified generated files also count as collisions. Symlinks
in output paths are rejected. Cancellation after artifact publication reports
that artifact and attempts a partial manifest; failure to write that manifest
is reported as a warning.

`RunOutput.Create(..., projectDirectory)` lets settings-aware commands place
default outputs beneath their resolved project root. `CliRuntime.CreateServices`
centralizes source/cache setup; resolved settings can register their layer tree
on that collection before the provider is built. `ExportCommands` is partial
and its `ConfigureServices` hook remains overridable for other export slices.

`ScheduleLoader.ConfigurationIdentity` participates in its SHA-256 content
hash, together with component types, source paths/bytes and implicit split
configuration. Initializer identity includes code module versions, academic
selection, enrichment/consultation options, lesson times and remappings. Teacher
profile aliases and output paths do not change this global schedule identity.
The source-root/academic cache filename keeps unrelated source trees apart.
`LocalFileLock` serializes cache rebuilds and reports explicit output contention;
its exclusive OS handle releases on process termination. Persistent `.lock`
files are coordination markers, not evidence of an active operation.
`AtomicFile.Publish` stages in the destination filesystem, flushes complete
contents, checks cancellation, and renames over the live file. Failed or
cancelled writes leave the previous live cache untouched. A killed process can
leave an orphan `.tmp` file; it is never read as a cache. These locks coordinate
cooperating local processes, not separate machines.

The storage tests run two real .NET processes for cache/export reuse, output
locks and killed staging writes; they inspect workbook content with ClosedXML
and deserialize the retained cache. Existing query fixtures explicitly bypass
caches. Windows execution remains unverified in this Linux environment.

```sh
dotnet run --project src/MainCli -- export pdf --json
dotnet run --project src/MainCli -- export ics --output ./calendars --no-cache
```

PDF and ICS exports retain the latest-period weekly lessons and generate the
existing full set of group, partition-combination and teacher files. A profile
can supply settings, but does not filter the export. ICS expands lessons into
concrete semester dates with the existing parity and holiday exclusions; missing
semester date ranges skip affected calendars and appear in result warnings.
Empty filtered schedules produce no file. These commands publish each completed
artifact through the same owned-output contract and never launch Explorer or an
editor. Ctrl+C cancels between PDFs and during ICS date expansion; an individual
synchronous PDF render finishes before cancellation is observed.
