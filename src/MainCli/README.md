# Local CLI operations

See the [main user guide](../../README.md) for the complete command matrix,
settings, authorization, preview/apply, exit codes and publishing. This page
records local schedule/query/export details.

Run from Windows or Linux with the installed .NET 11 preview SDK:

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
unexpected failures, 2 for invalid arguments, 3 for missing/invalid input, 4 for missing/failed authentication,
5 for I/O or remote operation failures, 6 for partial outputs, 7 for a busy explicit output
destination, 8 for unsupported capabilities and 130 for cancellation.
Ctrl+C cancels initialization and query enumeration.

`--jsonl` streams newline-delimited JSON on stdout and is mutually exclusive
with `--json` (both together exit 2). Human output remains the default; result
items and action outcomes stream as they become available, followed by a final
summary that does not repeat them. `--json` keeps its single complete envelope.
`--jsonl` stdout records each carry `schemaVersion: 1`, `command`, `runId`,
per-stdout-stream `seq` (starting at 0) and `event`:

```sh
schedulelib query lessons --jsonl | head -n 3
```

```json
{"schemaVersion":1,"command":"query lessons","runId":"synthetic-run","seq":0,"event":"start"}
{"schemaVersion":1,"command":"query lessons","runId":"synthetic-run","seq":1,"event":"item","id":"0","status":"completed","item":{"day":"Tuesday"}}
{"schemaVersion":1,"command":"query lessons","runId":"synthetic-run","seq":2,"event":"final","status":"succeeded","exitCode":0,"warnings":[],"errors":[],"data":{"count":1}}
```

- `start` is always first when `--jsonl` is recognized, including early
  argument/configuration failures.
- `item` carries a stable string `id` (plan-order index for registry,
  calendar, drive and query results; artifact path for exports) and its current
  `status`, so `planned`/`not-attempted` to
  `completed`/`failed`/`uncertain`/`cancelled` transitions correlate by `id`.
- `final` is last on success, failure and normal cancellation (`cancelled`,
  exit 130 when possible). A missing `final` means the process crashed or was
  killed and the run is incomplete. It carries `status`, `exitCode`,
  `warnings`, `errors` and scalar command data (target, counts, directories)
  without repeating the full array of previously emitted items. Collection
  commands (`config profiles`, auth accounts, history years/candidates,
  query sections, export artifacts, registry/calendar/drive plans and
  outcomes) stream one `item` per element; scalar commands (`config
  show`/`get`/`validate`/`set`/`unset`, empty auth/history results) emit
  `start` then `final` with the full data, since there is nothing to stream
  incrementally.
- Calendar and drive stream their read selection as items too: deleted remote
  identities (`deleted-N`) and desired payloads (`desired-N`) for calendar,
  and every planned drive action. Planned action ids are the same plan-order
  indexes in preview and apply, so each id transitions from
  `planned`/`not-attempted` to its terminal state.
- `seq` orders records within each stream only: stdout `seq` and stderr `seq`
  are independent counters starting at 0. Both streams share `command` and
  `runId`.
- When `--jsonl` is selected, every stderr warning/error/log is also JSONL
  (`event: "log"` with `level` and `message`, same `command`/`runId`, stderr
  `seq`), including errors before the command starts. Plain `--json` keeps the
  existing stderr text. Explicit `--help` stays ordinary text in all modes.
- Existing Serilog information logs keep their content and levels; registry
  per-action logs now appear under both preview and `--apply` when the
  configured processing flags select log/dry-run. History readers keep their
  `NullLoggerFactory` silence for privacy. No new progress logging or
  verbosity flag was added.
- Registry sync applies each completed course/group plan before scanning the
  next group; later planning failures can leave partial writes. Other commands
  retain their complete-plan behavior. Failed submissions are never retried.

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
behavior for every group: both parities, both occupancy modes, and all weekly
periods rather than only the latest one. The two modes are the two branches the
desktop task has always computed, and they differ only in how lessons addressed to
a subgroup, a specialization or an alternative are counted:

- `EveryLessonOccupies`: every lesson the group attends occupies its slot, whichever
  subgroup, specialization or alternative it addresses. A slot is free only when
  the group has no lesson in it at all.
- `WholeGroupAndUnspecializedOptionalLessonsOccupy`: lessons targeting the whole
  group (subgroup, specialization and alternative all unset) occupy their slot,
  plus lessons carrying the legacy `opțional` subgroup marker without a
  specialization, regardless of alternative. Other partitioned lessons leave
  their slot free.

`--help` states those defaults and the result repeats them as a warning. Days are
the Monday-Friday slots of the configured lesson times, and consecutive free
slots are merged into one interval. Text output prints one block per
group/parity/mode; `--json` returns the same structure under `data.sections`, with
the mode in `mode`. Repeating a group reports it once with a warning. A group
with no lessons succeeds with a fully free week. Cancellation is observed while
the lessons are enumerated and again before the result is reported, so an
interrupted query exits 130 with no `data` instead of a partial success.

The desktop task keeps its original Romanian rendering, including its
`optional?: True/False` marker, which is printed for the
`WholeGroupAndUnspecializedOptionalLessonsOccupy` mode exactly as before.
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

Default exports use a unique `output/RUN_ID` directory beneath the resolved project
root, or the invocation root when no project is selected. The result reports the
workbook and `schedulelib-manifest.json` paths.
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

`registry sync --profile TEACHER` previews reconciliation without writes. With
`--apply`, it scans and reconciles each course/group, then submits that group's
actions before scanning the next. The account lock covers the entire run. A later
scan, planning or submission failure leaves earlier writes applied and returns a
partial result (exit 6). Outcomes cover scanned groups only; unscanned groups are
not counted as planned actions. A normal lesson 302/303 redirect is confirmed by
reading the saved lesson values, without replaying the POST. An `uncertain`
outcome means the write may have succeeded: inspect the registry before retrying.

Sync reports missing registry courses/partitions, unmapped registry labels,
assignment mismatches, missing lesson topics, and missing attendance rows as
they are detected and retains them in the final report. JSON and JSONL include
these in `data.diagnostics`; human output gives the course/group and the
correction needed. Assignment mismatches name the teachers assigned in the
schedule and skip that destination before fetching its roster or lessons;
other matched groups continue. Streamed warnings are not printed again at the
end. Whole-group destinations include lessons restricted
to the parent group's teaching language, while other language subgroups retain
separate destinations. Attendance is looked up by each lesson's actual partition;
explicit lab merges retain their configured whole-group attendance projection.
Topic text is copied as configured, so Romanian topics can be explicitly scoped
to an English subgroup without translation.

Topic CSVs can combine lecture, lab, and language sequences in one file using
`name,lessonType,language,series` columns. Manifest `lessonType` and `language`
select matching rows when the CSV contains those columns; otherwise they supply
defaults. Set a manifest document's `series` to select a distinct variant while
retaining its group and partition scope. Rows can be interleaved; each selected
sequence keeps its own row order. Documents without `series` read all rows
matching their manifest type and language.

`registry import-grades --quiz-id ID --profile TEACHER` previews mapped Moodle
quiz grades. `--apply` rereads under the registry account lock and submits each
planned form once. See [grade-import behavior and exits](REGISTRY-GRADES.md).

## Teacher deadlines and pre-defense

```sh
schedulelib export lab-deadlines --profile "Curmanschii Anton" --output ./deadlines --json
schedulelib export pre-defense --output ./commissions --json
```

Deadlines require a selected teacher, laboratory lessons, coded academic date
ranges and resolved `DeadlinesExcelConfig`/task settings. They inspect every
period and generate `deadlines.xlsx`. Missing lessons/settings and invalid row
limits fail with exit 3. Shared/all-group labs mixed with other partition/course
combinations remain unsupported (exit 8). The command accepts the shared
schedule/cache arguments; the profile actually selects the teacher here.

Pre-defense uses the coded thesis source and year-thesis (`An`) commissions,
producing `comisia_NUMBER.xlsx` workbooks. It accepts output/settings/result
arguments, without schedule/cache arguments. The default commissions are empty:
configure nonempty commissions/members in C# before use (otherwise exit 3).
Duplicate/malformed commission/member data is rejected. A selected profile
supplies settings but does not filter commissions. Both commands use the same
owned-output/manifests and cancellation contracts as the other exports.

Synthetic teacher/commission tests inspect workbook contents and preserve real
source data; they do not establish that operational commission data is present.

Registry group aliases can be configured globally in `RegistryConfig.groupMatchRules`:

```json
{
  "faculty": "DJ",
  "subGroup": "eng",
  "language": "En",
  "matchByLanguage": true,
  "allowParentGroup": true
}
```

The default DJ English rule matches across parent numbers within the same intake,
attendance mode, and qualification. It requires one matching registry subgroup,
one course audience, and at most one schedule parent per language in that intake.
`allowParentGroup` includes whole-group lessons in an English-language parent.
If these checks fail, matching uses the strict parent identity. Other faculties and
partitions retain strict matching. Set `matchByLanguage` to `false` in a higher
configuration layer to disable a rule; rules merge by faculty and subgroup.
Topic text is copied from configured sources without translation.

Attendance workbooks accept lesson-type headers and date headers (text day/month
or native Excel dates). Date columns retain their workbook order. Empty sheets
are reported as missing attendance without stopping other worksheet imports.
