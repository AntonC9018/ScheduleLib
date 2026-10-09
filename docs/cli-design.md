# ScheduleLib CLI design

Design for [issue #71](https://github.com/AntonC9018/ScheduleLib/issues/71).
This document records the approved, implemented command contract. See the [user guide](../README.md)
and [verification evidence](cli-verification.md) for usage and platform limits.

Baseline: `feat/cli`, created from published `master` at `4caf3c1` after
integrating `merge` at `5511cc8` and completing the GitHub Issues migration.

## Scope

Expose all fifteen existing MainCli tasks through command-line arguments,
with one operation per invocation. Support Windows and Linux. Keep the
existing C# configuration path and its academic/teacher/source settings in
place. JSON is an additional persisted settings path using the existing
LayeredData/LayeredConfig engine, not a replacement engine.

The public API, proper elective cohorts, Avalonia integration, semantic lesson
matching, package extraction and external C# configuration authoring are later
work. The scalar elective workaround remains. PDF PR #68 was excluded from
this baseline; CLI work does not implicitly merge it.

Future C# configuration is recorded in
[#184](https://github.com/AntonC9018/ScheduleLib/issues/184); Layered Data
extraction already has [#18](https://github.com/AntonC9018/ScheduleLib/issues/18).

## Commands

Use CommandDotNet as the command-line framework. Define typed `IArgumentModel`
classes for operation inputs and reusable option groups where applicable;
compose them to keep handlers small and argument contracts consistent. Do not
attach irrelevant options to every command or replace CommandDotNet with a
custom parser. See the [official argument-model documentation](https://commanddotnet.bilal-fazlani.com/arguments/argument-models/).

The command name is `schedulelib`. During development, the same arguments can
be passed after `dotnet run --project src/MainCli --`. Release publishing can
produce platform-specific executable builds; public distribution and Google
production setup remain separate work.

| Command | Existing task | Contract |
| --- | --- | --- |
| `export pdf` | `PerGroupAndPerTeacherPdfs` | Generate existing group/partition/teacher PDFs. |
| `export ics` | `GenerateIcsCalendars` | Generate existing group/partition/teacher calendars. |
| `export teachers-excel` | `AllTeachersExcel` | Generate the all-teacher workbook. |
| `export free-rooms` | `FreeRooms` | Generate the free-room workbook. |
| `export lab-deadlines` | `TableOfAllLabLessons` | Generate the selected teacher's deadlines workbook. |
| `export website-schedules` | `JsonSchedulesForWebsite` | Generate per-teacher JSON files and ZIP locally. |
| `export website-theses` | `ListOfThesesPerTeacherForWebsite` | Generate per-teacher thesis files and ZIP locally. |
| `export pre-defense` | `CreatePredzashitaExcels` | Generate commission workbooks when existing configuration supplies the required commission data. |
| `query free-hours --group NAME ...` | `FreeHoursOfGroup` | Use supplied groups rather than the two groups embedded in dispatch. |
| `query lessons` | `Query` | Expose the existing weekly-lesson query predicates as arguments. |
| `drive publish` | `UploadDocsToDrive` | Generate the existing Excel/PDF/ICS bundle and preview or apply its Drive synchronization. |
| `calendar sync` | `UpdateCalendar` | Preview or apply synchronization to the configured calendar. |
| `registry sync` | `CreateLessonsInRegistry` | Preview or apply the existing lesson/topic/attendance reconciliation. |
| `registry import-grades --quiz-id ID` | `CopyGradesFromMoodleToRegistry` | Preview or apply grades from the specified Moodle quiz. |
| `curricula download` | `PullCurriculaFromOneDrive` | Download using the existing configured source and explicit Microsoft authentication. |

`drive publish` remains one operation even though generating its bundle has
several internal steps. It does not introduce arbitrary command chaining.
Website exports do not publish to the website.

Add `config show`, `config get`, `config set`, `config unset`, `config remove`,
`config clear`, `config validate`, and `config profiles`. Add `auth login`,
`auth status`, and `auth logout` with a provider argument where applicable.

Every command has help. No command prints an interactive menu or opens
Explorer/an editor after completing. Empty arguments show help. Invalid or
missing arguments fail before loading schedules or connecting to services.

## Arguments and existing behavior

Common options are `--project DIRECTORY`, `--profile TEACHER`, `--output PATH`,
`--cache-dir DIRECTORY`, `--json`, and `--help`. Options are accepted only where
they make sense; irrelevant options are errors rather than silently ignored.
Remote mutation commands also accept `--apply`. `--no-cache` bypasses the
schedule cache for operations that load a schedule.

`--profile` selects the existing teacher configuration. Identity-dependent
commands require it; global exports and queries do not require an unrelated
teacher identity. Profile listing includes code-defined teachers. For now,
there is one logical profile per teacher, with JSON overlays on that teacher;
independent profiles for different semesters/accounts of the same teacher are
deferred; storage choices must preserve the possibility of that extension.

Keep `AppConfiguration`/`DefaultConfig` as supported C# configuration. Do not
move their current study-year, semester, source or teacher data into JSON as
part of this issue. Operation selection, group query arguments, quiz ID and
output destinations stop requiring edits to MainCli dispatch. Coded settings
continue to work when no JSON settings file exists.

Preserve each existing task's period/filter defaults and explain them in help:
PDF, ICS, teacher Excel and website schedules use the latest period, while
free-room/free-hour handlers currently inspect all weekly periods. Do not
silently harmonize those behaviors or add a general query language. The
lesson query accepts the existing room/day/parity/time-slot cutoff predicates;
with no predicates it lists latest-period weekly lessons rather than using
the old embedded room/day/time example. Group names and other references must
resolve before execution and produce useful errors when unknown.

Exports initially retain their existing all-group/all-teacher breadth; a
selected execution profile must not be advertised as an export filter.
Broader export selectors can be a later extension.

## Settings and profiles

Lowest-to-highest precedence is:

1. Base code-defined defaults.
2. User defaults.
3. Project settings.
4. Selected teacher profile.
5. Explicit command-line arguments.

Within the selected profile, retain its code-defined settings, then merge
user profile overrides, then project profile overrides. CLI arguments remain
last. Current non-layered academic initialization options stay configured in
code; unsupported JSON keys fail validation rather than pretending to affect
those options.

Project discovery searches upward from the invocation directory for the
nearest `schedulelib.json`. `--project` selects an explicit directory and
requires that directory to exist. A settings file is optional; explicit
project selection still establishes the project root. User settings live in
the operating system's user configuration directory under `ScheduleLib`.
Project-free commands can run with user settings alone.

Use a versioned, readable JSON envelope containing defaults and profiles keyed
by teacher identity. Retain the existing typed configuration blocks and their
registered merge behavior. Validate keys, types and operations before doing
work; report unknown schema versions. Preserve the desktop serializer's
existing contract instead of silently changing its files.

Missing/null properties inherit; later provided scalar values override; nested
blocks merge; keyed collections retain unmatched inherited entries. JSON null
does not mean removal. Explicit remove/reset/clear operations are persisted
alongside values and applied through the existing update mechanism. Per-item
removal uses the collection's registered key. Unknown keys and unsupported
operations report errors rather than succeeding without effect.

`config set KEY VALUE --scope user|project` edits only the chosen scope, using
typed JSON values. Profile edits additionally select `--profile TEACHER`.
`unset` removes a local override and restores inheritance; `remove` suppresses
an inherited block/item; `clear` explicitly empties an inherited collection.
Show/get report resolved values and their contributing sources. Configuration
commands do not load the schedule or access remote providers.

Config-file paths resolve relative to the file that defines them. Paths passed
as arguments resolve relative to the invocation directory. Current code-based
resource discovery keeps its supported base paths explicitly. Stop changing
the process's working directory as a way to select configuration/resources.

Do not introduce an environment-variable settings layer in this version.
Existing credential providers remain supported. Ordinary project settings do
not store passwords, API keys or OAuth tokens; profile settings reference
credentials. Config inspection, plans and results redact secret values.

## Authentication

Only `auth login` may initiate browser consent. Support Google and Microsoft
consent where their existing workflows require it. Registry/Moodle commands
use their configured credentials without interactive prompting. Existing
user-secrets configuration remains compatible.

Ordinary commands may authenticate using already provisioned credentials or
refresh valid cached tokens, but missing interactive authorization fails with
the provider and the exact login command needed. Authenticate only providers
required by the selected operation. Login/logout intentionally manage local
credentials and are not schedule-publication previews.

Token stores belong to user state, with teacher/account separation. Logout is
explicit and local; no cloud revocation is assumed unless implemented and
documented. Do not print credentials or tokens in output.

## Preview and application

`drive publish`, `calendar sync`, `registry sync`, and `registry import-grades`
preview by default. They may read the remote state, authenticate with existing
credentials, and generate local artifacts. They do not mutate schedule files,
calendar events, registry lessons/grades or published Drive content remotely.

The plan identifies the destination/account, planned creates/updates/deletes,
affected lessons/files/grades/events and relevant omissions or warnings.
Calendar preview explicitly reports calendar replacement and resulting ID
changes, because that is the existing synchronization behavior. Drive preview
includes remote deletions. Registry preview preserves its existing matching
and extra-lesson policy; it does not claim semantic minimum-patch matching.

Repeat the same command with `--apply` to recompute against current remote
state and execute. There is no saved transaction or exact replay guarantee.
Application reports individual action outcomes and partial failures; a logged
or swallowed submission error must not result in an unconditional success
exit. Avoid blindly repeating non-idempotent creates after an uncertain result.
No automatic rollback or new incremental calendar algorithm is promised.

## Files, concurrency and cancellation

Default outputs use a unique run directory beneath the project output root
(or invocation-directory output root without a project). Report the actual
output paths in text and JSON. `--output` selects an explicit destination.
Never recursively clear a caller-supplied directory. Replace only files owned
by the operation, coordinate use of explicit shared destinations, and report
collisions with unrelated files.

Shared schedule caches use keys that distinguish source inputs and relevant
configuration. Coordinate writes and publish complete cache files atomically;
readers must not observe a partially written cache. A failed/cancelled rebuild
leaves the previous cache usable. Config and token writes also coordinate and
use complete-file replacement.

Remote application acquires a local lock for the actual destination/account,
not merely the profile alias. Concurrent conflicting operations report a busy
result instead of racing. This coordinates agents on the same machine; no
distributed locking across servers is promised.

Ctrl+C cancels the selected operation and propagates to its work. Preserve
original source documents during any conversion and operate on staged copies
when a legacy converter otherwise deletes its input. Publish completed local
outputs with a manifest; expose partial remote outcomes if cancellation occurs
after writes have started.

## Output and errors

Text is the default. `--json` emits a single versioned result object on stdout;
progress, warnings and diagnostic logs go to stderr. The object includes the
command, run identifier, preview/applied status, outputs, action outcomes,
warnings and errors, as applicable. Exported website JSON remains an artifact
and is distinct from the command result envelope.

| Exit | Meaning |
| --- | --- |
| 0 | Successful operation or valid preview. |
| 1 | Unexpected failure. |
| 2 | Invalid command/arguments. |
| 3 | Invalid/missing configuration or required input. |
| 4 | Missing/failed authentication. |
| 5 | Input/output or remote operation failure before partial application. |
| 6 | Partial application/output failure. |
| 7 | Conflicting operation is already running. |
| 8 | Unsupported platform/capability. |
| 130 | Cancelled; report completed remote actions if any. |

Legitimate empty queries succeed. Missing required pre-defense configuration
does not masquerade as a successful empty export. Existing deliberate skips
(such as missing calendar date ranges or website slugs) appear in results.

## Platform and known limitations

Build/run the CLI on Windows and Linux. Keep legacy Microsoft Word COM
conversion behind a Windows capability boundary. Unsupported `.doc`
conversion reports a clear prerequisite without modifying the source file;
DOCX/XLSX and the other portable paths must not inherit that requirement.

Initialize only dependencies needed by the command. Help/config/auth commands
must not parse schedules, erase outputs, or initialize unrelated cloud clients.
Preserve current website enrichment where a selected workflow needs it; this
design does not add an offline-mode guarantee.

Pre-defense commission data is currently empty; report that prerequisite until
the existing code configuration supplies it. Curricula source settings remain
code-defined for now and must be disclosed in the command's resolved inputs.
Existing positional registry matching, mixed-partition deadline limitations and
Moodle mapping limitations are surfaced rather than redesigned under #71.

## Verification and implementation boundary

Meaningful checks cover command parsing/routing, zero remote writes during
preview, correct apply routing, no interactive consent outside login, layer
precedence, persistence of explicit removals, source-relative path resolution,
clean machine output and error aggregation. Exercise concurrent cache/config
writes and output isolation. Use local fixtures/fake providers for remote-write
tests; do not test by changing live registries/calendars/Drive content.

Verify portable builds and representative local exports on Linux, and the
Windows build/capability boundary where an environment is available. Report
unverified Windows behavior honestly. Existing NU1510 restore warnings and
DOCX snapshot baseline failures remain tracked observations; do not hide them
by globally disabling checks or accepting new snapshots without review.

The CLI implementation follows this approved contract. This document does not
authorize live remote actions or Git publication; those require their own task
authority. Unavailable verification is recorded separately from completed checks.
