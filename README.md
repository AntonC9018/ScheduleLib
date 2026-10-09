# ScheduleLib

ScheduleLib parses university schedule Word/Excel sources, generates timetable
artifacts, and supports teacher registry, Calendar, Drive and curricula workflows.
The `schedulelib` CLI runs one operation per invocation on Linux and Windows.
Academic year, semester, teacher identities and source defaults remain in the
existing C# `AppConfiguration`/`DefaultConfig`; optional JSON settings overlay
those defaults. Proper elective cohorts and the public API remain deferred.

## Run and publish

Use the repository's **.NET 11 preview** SDK (`11.0.100-preview.4` was verified).
The target is `net11.0`; published framework-dependent executables need a matching
.NET 11 preview **.NET and ASP.NET Core runtimes** on the destination machine
(`Microsoft.NETCore.App` and `Microsoft.AspNetCore.App`; the verified packages
request `11.0.0-preview.4.26230.115`). The matching SDK includes both.
From the repository root:

```sh
dotnet run --project src/MainCli -- --help
dotnet run --project src/MainCli -- config profiles --json
dotnet run --project src/MainCli -- query lessons --help
```

Build platform-specific packages (the publish directory contains the executable,
assemblies, runtime configuration and bundled `data`; keep them together):

```sh
dotnet publish src/MainCli/MainCli.csproj -c Release -r linux-x64 --self-contained false -o /tmp/schedulelib-publish/linux-x64
dotnet publish src/MainCli/MainCli.csproj -c Release -r win-x64 --self-contained false -o /tmp/schedulelib-publish/win-x64
/tmp/schedulelib-publish/linux-x64/schedulelib --help
```

On Windows choose a Windows output path and run `schedulelib.exe` from that
package. Publishing for Windows on Linux builds a package; it does not verify
native Windows execution. See [verification evidence and limits](docs/cli-verification.md).

The examples below use `schedulelib` from the package/on `PATH`. During development
replace it with `dotnet run --project src/MainCli --`. Empty arguments and every
command's `--help` show help without loading schedules or contacting providers.
Place options after the selected operation. There is no interactive menu or
command chaining.

## Commands

All operations accept `--project DIRECTORY`, `--profile TEACHER` and `--json`;
a profile supplies settings rather than filtering global exports. Options are
accepted only by applicable commands: inspect the operation's `--help`.

| Command | Result and preserved defaults | Required input |
| --- | --- | --- |
| `export pdf` | All group/partition/teacher PDFs, latest period | Schedule sources |
| `export ics` | All group/partition/teacher ICS calendars, latest period; missing semester dates are reported skips | Schedule sources and coded academic dates |
| `export teachers-excel` | All-teacher workbook, latest period | Schedule sources |
| `export free-rooms` | Free-room workbook, every weekly period | Schedule sources |
| `export lab-deadlines` | Teacher laboratory deadlines, all periods | `--profile`, teacher deadlines/tasks settings and academic dates |
| `export website-schedules` | Per-teacher schedule JSON and ZIP locally, latest period | Schedule sources and existing website slug mapping |
| `export website-theses` | Per-teacher thesis JSON and ZIP locally | Coded thesis source and slug mapping |
| `export pre-defense` | Year-thesis (`An`) commission workbooks | Nonempty valid commissions/members and thesis source in C# |
| `query lessons` | Latest-period weekly lessons; optional `--room`, `--day`, `--parity`, inclusive `--before-slot` (1–7) | Schedule sources |
| `query free-hours` | Both parities and legacy partition occupancy modes, every weekly period | One or more `--group NAME` |
| `drive publish` | Generate XLSX/PDF/ICS bundle and preview Drive creates/updates/deletes | `--profile`, configured folder, provisioned Google authorization |
| `calendar sync` | Preview named calendar replacement and event/ID changes | `--profile`, configured calendar and Google authorization |
| `registry sync` | Preview positional lesson/topic/attendance reconciliation | `--profile` and configured registry credentials |
| `registry import-grades` | Preview mapped/rounded Moodle quiz grades | `--profile`, positive `--quiz-id ID`, configured Moodle/registry credentials |
| `curricula download` | Download from the coded 2024–2025 Microsoft source | `--profile` and Microsoft authorization |

```sh
schedulelib query lessons --room 423/4 --day Tuesday --parity OddWeek --before-slot 6 --json
schedulelib query free-hours --group IA2301 --group M2301
schedulelib export teachers-excel --output ./teacher-export
schedulelib export lab-deadlines --profile "Curmanschii Anton" --output ./deadlines
schedulelib export pre-defense --output ./commissions
```

Queries succeed when a valid selection is empty; unknown group/room references
report an argument error. Laboratory deadlines report the existing unsupported
shared/all-group lab combinations with other partitions/courses. Pre-defense
has no configured commissions by default and reports a prerequisite error
(exit 3), rather than claiming an empty export. Neither workflow discovers new
teacher/commission data. Website exports create local files, without publishing.
See [local exports, source/cache selection and query behavior](src/MainCli/README.md).

## Settings and teacher profiles

`config profiles` lists code-defined teacher identities; one logical profile
per teacher is supported. `--profile` selects that teacher's settings overlay.
A settings file is optional. Project discovery searches upward for the nearest
`schedulelib.json`; `--project` selects an existing directory explicitly.
User settings live in the OS user configuration directory under `ScheduleLib`.

Precedence is code defaults → user defaults → project defaults → selected
teacher's coded settings → user teacher overlay → project teacher overlay →
explicit command arguments. JSON paths resolve relative to the defining file;
argument paths resolve relative to the invocation directory.

| Configuration operation | Behavior |
| --- | --- |
| `config show` / `config get KEY` | Inspect resolved values and contributing sources, with secrets redacted |
| `config set KEY VALUE --scope user\|project` | Persist a typed JSON override |
| `config unset KEY --scope user\|project` | Remove the local override/suppression, restoring inheritance |
| `config remove KEY --scope user\|project [--item JSON]` | Suppress an inherited block or keyed collection item |
| `config clear KEY --scope user\|project` | Explicitly empty an inherited collection |
| `config validate` | Validate both files and all teacher overlays |
| `config profiles` | List existing teacher identities |

```sh
schedulelib config show --profile "Curmanschii Anton" --json
schedulelib config set GoogleCalendarConfig.calendarName '"lessons"' --scope project --project .
schedulelib config get GoogleCalendarConfig.calendarName --project . --json
schedulelib config unset GoogleCalendarConfig.calendarName --scope project --project .
schedulelib config validate --project .
```

The string example uses POSIX/PowerShell shell quoting; preserve the literal
JSON quotes when adapting to another shell. Null inherits; an ordinary empty
list does not clear inherited entries. Only registered typed keys are supported.
Passwords, API keys and tokens cannot be stored in ordinary settings files;
existing user-secrets and credential references remain supported.
[Settings reference](src/MainCli/SETTINGS.md) explains the schema, collection
keys, removals and complete-file coordinated writes.

## Authorization and remote changes

Only `auth login` initiates browser consent. Provision each needed provider once
using the existing client/user-secrets configuration:

```sh
schedulelib auth login google --profile "Curmanschii Anton"
schedulelib auth status google --profile "Curmanschii Anton" --json
schedulelib auth logout google --profile "Curmanschii Anton"
schedulelib auth login microsoft --profile "Curmanschii Anton"
schedulelib auth status microsoft --profile "Curmanschii Anton" --json
```

All three verbs accept `google` or `microsoft`. Status reads local state; logout
removes local authorization without cloud revocation. Ordinary commands use
provisioned/refreshable credentials and fail with exit 4 and a login instruction
when authorization is missing or unusable. Registry/Moodle use their configured
credentials without consent. Tokens belong to OS user state, separated by
teacher/client/account, and never appear in results. See [authorization and
curricula prerequisites](src/MainCli/AUTH.md).

`drive publish`, `calendar sync`, `registry sync` and `registry import-grades`
**preview by default**. Preview may read remote state and generate local files,
but writes no published Drive content, calendar events or registry lessons/grades.
Add `--apply` to the same command to reread current state and execute changes:

```sh
schedulelib calendar sync --profile "Curmanschii Anton" --json
schedulelib calendar sync --profile "Curmanschii Anton" --apply --json
schedulelib drive publish --profile "Curmanschii Anton" --output ./drive-bundle --json
schedulelib registry sync --profile "Curmanschii Anton" --json
schedulelib registry import-grades --profile "Curmanschii Anton" --quiz-id 123 --json
```

Apply is a new reconciliation, not replay of a saved transaction. Calendar keeps
its existing replacement semantics; Drive requires one unambiguous folder name,
updates matching filenames and deletes unmatched remote files. Failures report
completed/unattempted/uncertain actions; there is no automatic rollback or speculative retry of uncertain creates.
Read the operation-specific contracts before applying:
[Calendar](src/MainCli/CALENDAR.md), [Drive](src/MainCli/DRIVE.md),
[registry reconciliation](docs/development-notes.md#registry-cli-synchronization-197)
and [grade import](src/MainCli/REGISTRY-GRADES.md).

## Outputs, concurrent runs and automation

Export/download defaults use a unique `output/RUN_ID` below the resolved project
root, or invocation root without a project. `--output` selects a shared destination;
completed artifacts and a `schedulelib-manifest.json` are reported. Existing
caller directories are never recursively cleared. Replacement requires unchanged
manifest-owned files from the same operation; unrelated/modified files cause a
collision error. Schedule operations accept `--data-dir`, `--cache-dir` and
`--no-cache`; pre-defense, curricula and configuration/auth operations do not
accept irrelevant schedule options. Source layout and academic selection remain
code-defined.

Separate processes can share caches, which publish complete files atomically.
Explicit output/config/token writes and remote apply destinations use local
locks; conflicts report exit 7. Destination locks use the actual account/target,
so profile aliases cannot race. Locks coordinate one machine, not multiple hosts.
Ctrl+C propagates cancellation, preserves original sources and reports completed
outputs/actions. Run separate invocations from scripts for multiple operations.

`--json` emits one schema-version-1 result object on stdout; progress, warnings
and diagnostics use stderr. The envelope contains `command`, `runId`, `status`,
`exitCode`, `outputs`, `actions`, `warnings`, `errors` and command-specific `data`.
Read the exit code and partial/uncertain action outcomes before retrying.

| Exit | Meaning |
| --- | --- |
| 0 | Success or valid preview |
| 1 | Unexpected failure |
| 2 | Invalid command/arguments |
| 3 | Missing/invalid configuration or required input |
| 4 | Missing/failed authentication |
| 5 | I/O or remote operation failure before partial application |
| 6 | Partial application/output failure |
| 7 | Conflicting local operation |
| 8 | Unsupported platform/capability |
| 130 | Cancelled, with completed outputs/actions reported where applicable |

DOCX/XLSX and portable exports run on Linux. Legacy `.doc` conversion requires
Windows with .NET Framework 4.8 and Microsoft Word, and operates on staged copies.
Native Windows/Word and live Google/Microsoft consent checks are explicitly unverified; fake-provider
checks exercise preview/apply/failure behavior without live publication.
[Verification guide](docs/cli-verification.md) records repeatable checks and limits.

## Schedule features

### Models

All interactions with the schedule are done against an immutable schedule model (the `Schedule` type).
It's basically like a flat database of various schedule-related entities, 
linked between each other with strongly-typed identifiers.

You can see all models [here](src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs) (they are only conceptually immutable).

### Source-of-truth format

The schedule is currently made manually (not by me) and provided in the form of a Word document.
It is delivered by email on demand to me personally.
There aren't any public pages that Word document is available at.

All other representations must be derived from said Word document.
This includes the PDF documents on the [official FMI website](https://fmi.usm.md/orar/),
which is not my responsibility.

Parsing Word seemed easier than processing the PDF's generated from Word,
because they would lose the concept of tables and such,
hence parsing Word is the way that the schedule information gets into the program.

> The Word documents provided by the university usually contain a few errors,
> which make the parser trip up, so have to be fixed manually.

### Word parser

The word parser is one of the most complex aspects of this application.
The most complex parts are the following:
- [WordScheduleParser](src/ScheduleLib/ScheduleLib.Core/Parsing/WordScheduleParser.cs), 
  which deals with Word itself, using the Microsoft OpenXML library.
- [LessonParser](src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs), 
  which parses the strings in a singular cell in a schedule table.
- [CourseNameParser](src/ScheduleLib/ScheduleLib.Core/Parsing/CourseNameParser.cs) and
  [CourseNameUnifierModule](src/ScheduleLib/ScheduleLib.Core/Parsing/CourseNameUnifierModule.cs),
  which make sure similar course names are considered the same.

The rules around the document format do not officially exist
and have been derived empirically from the existing schedule Word documents.
Hence, this part of the code is very sensitive to the format
and has tons and heaps of special cases to deal with the inconsistencies.

### The JSON representation

The JSON representation is generated from the source-of-truth schedule in order to:
- Cache the schedule representation, so loading back into memory is faster.
- Implement integration tests that could verify the parsing code doesn't break. 
  This is needed to deal with the aforementioned sensitivity of the parser to the format,
  with the parser having to be adjusted to include new edge cases when new rules are discovered.
- Simplify potential integration with other tools to allow them to load the schedule structure
  without reimplementing the Word parser.

The JSON conversion is done using `System.Text.JSON`. 
You can find the relevant code [here](src/ScheduleLib/ScheduleLib.Core/Model/ImmutableModels/Serializer.cs).

