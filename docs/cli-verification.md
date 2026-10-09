# CLI verification

This records verification for the implemented [#71 CLI contract](cli-design.md)
and [#201 final verification](https://github.com/AntonC9018/ScheduleLib/issues/201).
The [user guide](../README.md) contains the complete command matrix and usage.
Checks use local fixtures and injected providers without live publication.

## Repeatable commands

Use .NET SDK `11.0.100-preview.4.26230.115` (the repository targets `net11.0`).
From the repository root:

```sh
dotnet restore src/MainCli/Tests/MainCli.Tests.csproj --verbosity quiet
dotnet test src/MainCli/Tests/MainCli.Tests.csproj --no-restore -m:1 --verbosity quiet
dotnet build src/MainCli/MainCli.csproj --no-restore -m:1 --verbosity quiet
dotnet publish src/MainCli/MainCli.csproj -c Release -r linux-x64 --self-contained false -o /tmp/schedulelib-201-publish/linux-x64 -m:1 --verbosity quiet
dotnet publish src/MainCli/MainCli.csproj -c Release -r win-x64 --self-contained false -o /tmp/schedulelib-201-publish/win-x64 -m:1 --verbosity quiet
/tmp/schedulelib-201-publish/linux-x64/schedulelib --help
/tmp/schedulelib-201-publish/linux-x64/schedulelib config profiles --json
```

Both framework-dependent packages request `Microsoft.NETCore.App` and
`Microsoft.AspNetCore.App` version `11.0.0-preview.4.26230.115`; install the
matching preview SDK or both runtimes on the target machine. Keep the whole
publish directory together, including assemblies, runtime/dependency JSON and
`data`. The Linux executable is `schedulelib`; Windows is `schedulelib.exe`.
Publishing does not distribute credentials or grant provider authorization.

## Completed checks (2026-10-09)

| Check | Evidence |
| --- | --- |
| Full CLI suite | **529 passed, zero failed/skipped** after PR review corrections, including complete-command-surface and production-adapter regressions |
| CLI build | Zero warnings/errors with repository warning-as-error policy |
| Linux x64 release package | Framework-dependent publish completed; native ELF apphost executed |
| Windows x64 release package | Cross-publish completed; PE apphost, managed assembly, dependency/runtime JSON and all three Word converter dependencies present |
| Package source data | Both packages contained all **44** configured DOCX/XLSX/topic files, compared byte-for-byte by SHA-256 (1,218,612 bytes); existing shared content rules also include `data/.gitignore` |
| Published Linux help | All **26** operation routes show help from a fresh unrelated working directory, with no files created |
| Published Linux config | Profiles/validate/set/get/unset/validate succeeded using isolated OS user configuration/state and project directories; a fresh get observed the persisted string |
| Published Linux auth inspection | Google status succeeded locally for a coded teacher without consent or schedule initialization |
| Published Linux errors | Inapplicable config schedule option returned exit 2 and one JSON object; unconfigured pre-defense returned prerequisite exit 3 |

The command audit maps each of the fifteen `AppTask` values to a documented
operation: eight exports, two queries, Drive, Calendar, registry synchronization,
registry grades and curricula. Eight configuration verbs and three authorization
verbs complete the surface; authorization accepts both provider literals.
Each command receives typed `IArgumentModel` inputs (`SettingsArguments`,
`SourceArguments`, `ResultArguments`, `OutputArguments` and operation-specific
query/quiz/config/provider/apply models where relevant). Help reports preserved
period/filter behavior and applicable options. Irrelevant options are parser
errors, and two operations cannot be chained into one invocation.

`CommandSurfaceTests` runs every help route with a deliberately invalid project
settings file: it verifies help does not read settings or create output/state.
It also checks irrelevant options and operation chaining produce a single
schema-version-1 argument-error envelope with empty outputs/actions and stderr
diagnostics. Those checks exercise the public runner, supplementing the focused
command/provider tests.

## Existing behavior evidence retained

| Contract | Tests and scope |
| --- | --- |
| Portable sources and artifacts | `QueryTests`, `ScheduleExportTests`, `FreeRoomsTests`, `TeacherExportTests`, `WebsiteExportTests`: DOCX/XLSX fixtures, PDF/ICS structure, workbook cells, synthetic teacher deadlines/commissions, per-teacher JSON/ZIP contents and omissions |
| Safe concurrent storage | `StorageTests`, `SettingsWriteTests`, `SettingsRemovalTests`: real process output/cache contention, killed staging writer, atomic preservation of previous files, owned replacement/collision handling, scoped settings/removal roundtrips |
| Explicit authorization | `AuthenticationTests`, `MicrosoftTests`: consent only through login, local status/logout, teacher/client state, silent refresh/failures, Graph pagination and staged source preservation |
| No-write preview and apply outcomes | `CalendarSyncTests`, `DriveSyncTests`, `RegistryTests`, `RegistryGradeTests`: preview does not invoke remote writes; apply recomputes under destination locks, partial/uncertain outcomes and cancellation |
| Production transport failures | `GoogleCalendarProviderTests`, `GoogleDriveProviderTests`, `RegistryGradeAdapterTests`, `RegistryLessonAdapterTests`: SDK/HTTP paths against injected transports, no automatic create replay, authentication/network/deadline classification, HTTP rejection and post-submit confirmation |
| Machine results and errors | Command/query/export/provider tests: single JSON envelope, stderr diagnostics, exits 2/3/4/5/6/7/8/130 where appropriate; completed/uncertain work survives partial failure |

Provider HTTP checks exercise the production adapters with local/in-memory
transports, not live registry/Calendar/Drive accounts. Portable fixture exports
use controlled enrichment/slug providers. Ordinary configured schedule loading
still uses its existing read-only website enrichment; the packaged CLI does not
promise offline exports. No repeated broad test run is needed unless subsequent
implementation/review corrections change behavior.

## Verification limits

Native Windows execution and Microsoft Word COM conversion were unavailable.
Windows cross-publishing proves package construction, not Windows execution or
Word availability. Word cancellation uses cooperative shutdown and verified process ownership; a Word instance that blocks before reporting its identity cannot safely be terminated. Linux tests verify that unsupported legacy DOC conversion
returns a capability error and preserves the original document.

Live Google/Microsoft browser consent, actual organization/account access,
production curricula sources and real registry/Calendar/Drive publication were
not exercised. Fixture checks do not provision operational commissions, fix the
coded 2024–2025 curricula source, or validate every historical schedule document.
Those prerequisites and retained matching/partition limitations appear in help
and the specialized operation documents.

This ticket verifies the CLI and its referenced build graph; it does not claim
that every project in `All.sln` passes. Previously recorded NU1510 restore
warnings and DOCX snapshot observations remain separate repository findings.
The commands above completed without warning suppression, target-framework
migration or snapshot acceptance. For noninteractive Verify snapshot runs, use
`DiffEngine_Disabled=true` as documented in [development notes](development-notes.md).
