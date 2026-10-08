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
prefix, so it is not used as this query's valid fixture. The free-hour checks
pin the fixture intervals and the free-room checks pin the workbook cells; a
copy of that master workbook with its lesson rows deleted is the
schedule-without-lessons case.


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

```sh
dotnet run --project src/MainCli -- query free-hours --group IA2301 --group M2301
dotnet run --project src/MainCli -- query free-hours --group MIA2501 --json
```

`query free-hours` uses the supplied groups instead of the two embedded in the
desktop dispatch. At least one `--group` is required, and names resolve against
the schedule: an unknown or ambiguous name is an argument error that reports the
name and suggests a case-insensitive match. Results keep the existing handler's
behavior for every group: both parities and both partition modes (with and
without optional lessons), over all weekly periods rather than only the latest
one. `--help` states those defaults and the result repeats them as a warning.
Days are the Monday-Friday slots of the configured lesson times, and consecutive
free slots are merged into one interval. Text output prints one block per
group/parity/mode; `--json` returns the same structure under `data.sections`.
Repeating a group reports it once with a warning. A group with no lessons
succeeds with a fully free week.

The desktop task keeps its original Romanian rendering.
`PrintFreeHoursOfGroupTaskHandler.Sections` exposes that same computation as
`FreeHoursSection` records and `Run` renders those sections, so the existing
text and the CLI results cannot drift apart.

```sh
dotnet run --project src/MainCli -- export free-rooms
dotnet run --project src/MainCli -- export free-rooms --output ./free-rooms-export --cache-dir ./cache
```

`export free-rooms` generates the existing free-room workbook: one worksheet per
parity, a day header row for each day that has lessons, and one row per time
slot listing the rooms free in it. Rooms and occupancy come from every weekly
period, room identifiers without a block suffix are written as `<room>/4`, and
days without lessons are skipped. It reuses `RunOutput`, so it takes the same
isolated default output directory, manifest, and replacement rules as
`export teachers-excel`. A schedule without weekly lessons succeeds with an
empty workbook and a warning. No teacher identity or interactive consent is
required.

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

```sh
dotnet run --project src/MainCli -- export website-schedules --json
dotnet run --project src/MainCli -- export website-theses --output ./website --cache-dir ./cache
```

Website exports generate existing per-teacher JSON files and a ZIP locally;
nothing is published to the website. Schedules keep the latest-period
selection, teacher enrichment and slug mapping; theses keep the configured
thesis input. Missing-slug and intentional omissions (such as teachers with
no schedule entries) appear as warnings and in the JSON result alongside an
artifact manifest; ZIP entries are verified against the published files.
Website JSON remains on disk as artifacts, distinct from the command-result
envelope, and legacy progress messages go to stderr. No Explorer window is
opened. Cancellation, output ownership, locks and exit codes follow the same
contracts as the teacher export. Checks use fake slug/website providers and
synthetic thesis fixtures; the configured real thesis input is preserved.

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

`registry import-grades --quiz-id ID --profile TEACHER` previews mapped Moodle
quiz grades. `--apply` rereads under the registry account lock and submits each
planned form once. See [grade-import behavior and exits](REGISTRY-GRADES.md).
