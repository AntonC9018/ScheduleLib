# Development Notes

Keep durable technical findings here or in the document for the affected domain.
Tasks, decisions awaiting resolution, and implementation follow-ups belong in
GitHub Issues; see [task-management.md](task-management.md).

## .NET 11 preview definite assignment

SDK `11.0.100-preview.4` ignores `[DoesNotReturn]` for definite assignment. For
example, `if (b) x = 1; else Boom(); return x;` can fail with CS0165 even when
`Boom` has that attribute. Prefer exception factories returning an exception
instance and an explicit `throw` at the call site over void throwing helpers.

This finding was preserved from the former task tracker's shared memory during
the [GitHub Issues migration](https://github.com/AntonC9018/ScheduleLib/issues/69).

## Non-interactive snapshot tests

Set `DiffEngine_Disabled=true` when running Verify snapshot tests from an agent
or other non-interactive shell. Otherwise a mismatch can launch an editor and
stall the test process. This flag disables the diff viewer while retaining
snapshot assertions. See the [DiffEngine documentation](https://github.com/VerifyTests/DiffEngine#disable-for-a-machineprocess).

## CLI Google authorization (#195)

CLI Google services use `IGoogleCredentialAuthorization` through
`GoogleCredentialResolver`; `CliRuntime.CreateServices` registers the
noninteractive `GoogleAuthentication`. Independently assembled remote-command
services must register it too and map `AuthenticationRequiredException` to exit
4. Only `auth login google` invokes consent. Status/logout need no secrets or
schedule initialization. User-state tokens are teacher/OAuth-client partitions,
written atomically under local locks; user-secrets client sources are preserved.
See [CLI authorization](../src/MainCli/AUTH.md) for storage, successor APIs, and
unverified real-consent/Windows behavior.

## Registry CLI synchronization (#197)

`registry sync --profile "Curmanschii Anton"` builds a read-only reconciliation
plan; `--apply` builds a fresh plan while holding a local lock for the configured
registry login and registry base URL. The CLI's preview/apply switch controls
processing flags, while the existing positional derivation, teacher filters,
topics, attendance mapping and extra-lesson policy remain configured in C#.
Extra lessons left alone (or requiring unsupported lossless deletion) appear as
omitted actions. Plans do not submit forms. Application stops on the first failed
submission, reports completed and unattempted actions, and never retries creates.

`IRegistrySyncNavigator` separates reads from submissions. The configured CLI
session owns its settings, initialized service scope and authenticated registry
context. `IConfiguredRegistrySession` exposes that scoped service provider and
navigator for the grade-import slice without reauthenticating. Both registry
commands can share `IRegistryAccountLock`; aliases selecting the same configured
login/base destination use the same local lock. Locks coordinate this machine,
not different hosts.

AngleSharp form submission does not accept a cancellation token. The adapter
cancels its wait and disposes the session on cancellation; an in-flight submission
can already have reached the server, so its outcome is explicitly uncertain.

### Calendar CLI replacement (#199)

`calendar sync` previews by default; `--apply` uses the shared `ApplyArguments`
from the registry slice. The extracted `BuildDesiredEvents` in the existing
Google Calendar handler is the common recurring payload builder. The CLI
checks the selected teacher exists before using the legacy teacher lookup.
`ICalendarSyncProvider` isolates paginated reads and single-attempt mutations;
`CalendarSync.Run` locks actual primary-calendar account ID plus configured
calendar name before rereading state and replacing the first matching summary.
Do not lock the soon-to-be-replaced ID alone: aliases must conflict even when
replacement changes that ID. Primary destinations are prohibited by both
configured literal and actual remote identity. Outcomes preserve completed
calendar/event IDs and desired-event indexes; lost or cancelled responses are
uncertain and never retried. See `src/MainCli/CALENDAR.md` for result semantics.
## CLI Microsoft authorization and curricula (#196)

`MicrosoftAuthentication` uses supported MSAL cache callbacks/serialization;
only `auth login microsoft` invokes interactive acquisition. Downloads resolve
with `AcquireTokenSilent` under the same teacher operation lease as local
login/status/logout. State belongs to OS user state, separated by teacher,
tenant/client and actual MSAL account. The old working-directory cache is not
imported; explicit login reprovisions it. The Graph adapter follows program and
document pagination and performs reads only. Curricula retain their coded
2024–2025 owner/path and disclose them in results. Legacy DOC conversion uses
staged copies with separately published originals. See [CLI authorization](../src/MainCli/AUTH.md)
for storage, capability prerequisites and unverified Windows/real-consent paths.

### Registry Moodle grade import (#198)

`registry import-grades` uses the existing configured registry session and
account lock from #197. `IRegistryGradePlanner` adapts the existing Moodle
scraper and `CopyGradesFromMoodleForTestTaskHandler.Plan`; the latter only reads
forms and captures mapped/rounded changes. `RegistryGradeAction.Execute` fills
inputs and crosses the submission boundary explicitly. Preview neither changes
form inputs nor submits grade forms. Apply fetches a new plan under the actual
registry login/base destination lock. The legacy name parser can interpret
`exmatr` after a two-part name as a patronymic; planning checks that suffix first
to omit expelled students. See [grade-import contracts](../src/MainCli/REGISTRY-GRADES.md)
for outcomes, omissions, page-size and cancellation limitations.

Grade submission uses a scoped requester policy that suppresses authentication
replay; legacy requester retries remain unchanged outside that scope. Completion
requires a fresh grade-form read with matching named fields. HTTP rejection is
failed, while server errors and failed confirmation remain uncertain. Moodle's
form login validates a logout link and cancels its wait on uncancellable
AngleSharp submission; failed context construction disposes both browser and
transport before the CLI releases the account lock. Tests inject in-memory
transports through the production builder and Moodle/planner paths.

### Read-only registry history (#204)

`registry history` uses the configured credential resolver without initializing
the current schedule. The offline course fixture demonstrates a GET `AnUniv`
year selector; discovery derives navigation from that form and requires the
requested year to be selected in the returned page. Raw historical course/group
names do not pass through current-schedule matching. A destination-wide local
lease and shared transport gate cover login and reads across history accounts;
403/429/authentication rejection stop without replay. Private summaries require
an explicitly selected ignored `.registry-history` directory; stdout contains
only counts. Live year/group discovery has been validated sequentially for all seven configured
analysis accounts across ten offered years. Historical courses with no assigned
groups can render a `form[name=lesson]` containing only the hidden MVC
verification field; the history reader recognizes that exact empty shape and
still rejects unknown layouts or displaced lesson links. Fixed structural
diagnostics report the failing contract without raw page or exception content. See [history command details](../src/MainCli/REGISTRY-HISTORY.md).

### Registry profile spelling and topic candidates (#204)

`TeacherProfileScheduleLookup` retains profile/credential identity while resolving
the known `Visnevschi Vlada` profile against schedule spelling `Vișnevschii Vlada`.
Direct matches take precedence; no general fuzzy identity lookup is introduced.
Registry, calendar and teacher-export paths share this resolver.

Historical topic acquisition stops older matching reads after the latest usable
year for each normalized title and semester. Duplicate title links remain
ambiguous, but their latest snapshots can be exported as private CSV candidates
marked `ambiguous-course`; candidate export does not activate an import manifest.
Unconfirmed historical `(D)` and `(S)` partitions are excluded before lesson
navigation. The topic manifest cannot target group numbers or subgroup partitions;
those limitations must be checked against the schedule before activating candidates.

### Attendance workbook reconciliation (#204)

The attendance reader starts at the first row containing cell contents, so styled
blank leading rows do not hide a later lesson-type header. Repeated header lesson
types produce one output key per type while preserving all attendance columns.
Synthetic tests exercise complete workbook parsing and building with and without
leading blank rows.

Combined registry group matching resolves existing parent groups from the actual
course and exact partition. Missing language requires a single language among
eligible parents. Matching accepts lessons wholly within those parents and disables
the ordinary elective fallback. This resolves registry labels; it does not merge
attendance sheets or collapse distinct scheduled lessons into one event. A physical
merge needs a confirmed destination and explicit alignment of old lesson columns;
positional concatenation must not be treated as a semantic merge.

`RegistryConfig.labMerges` provides an explicit registry-only lab projection,
keyed by canonical course name and parent group, with `sourceSubGroup` selecting
the surviving scheduled lab partition. Historical schedule lessons remain intact.
The registry planner replaces subgroup labs with that partition's lab times and
keeps ordinary whole-group lectures. The attendance reader resolves a merged
whole-group lab sheet against the surviving scheduled partition, then stores its
attendance under the whole-group key. Missing surviving labs stop planning.
No merge is inferred for other courses, groups, or combined registry labels.

Merged worksheet columns must be aligned by confirmed lesson occurrence before
loading; this projection does not reconcile differing source dates automatically.
For typed headers, attendance cell boundaries use the current cell's column and
the declared lesson count, so values outside the table cannot extend attendance.

### Scoped historical topic activation (#204)

Topic manifest documents may specify exact `groups`, `semester` (1 or 2), and
partition dimensions (`subGroup`, `specialization`, `alternative`).
`wholeGroupOnly` explicitly selects the unpartitioned target. Existing documents
without these fields retain their previous matching behavior. The teacher topic
loader passes the selected semester; scoped documents are excluded when it is
different or unavailable. Language-independent fallback topics preserve the
document's group and partition restrictions.

`allowMissingLessonTypes` permits a partial source to leave missing scheduled
lesson types unnamed after configured fallbacks are considered. A lookup for an
unavailable lesson type returns null. It does not copy lecture topics into labs.

Private user settings select the reconciled sources per teacher, removing the
inherited manifest-directory source to prevent duplicate sequence loading.
Historical sources are reconciled against teacher/course/parent/partition/type
evidence; conflicting sequences remain inactive. Newer supplied topics and
authoritative current registry topics retain precedence. Topic files and local
settings remain outside the repository.

### Registry assessments (#204)

`RegistryConfig.assessments` is keyed by course, group, lesson type, and optional
subgroup, specialization, or alternative. Each rule has multiple ISO `dates`. The planner reserves assessment
occurrences before assigning regular topic indexes; attendance indexes are
unchanged. Existing assessment text and dates are authoritative unless the
matching rule supplies replacement dates. Dates and existing assessments are
paired chronologically. Additional existing assessments are preserved, and
additional configured dates receive numbered assessment labels. Invalid or
ambiguous target occurrences stop planning before submissions. An assessment
outside the selected timetable is protected from deletion; moving it requires
its source lesson to be present. A move changes topics on the timetable's rows,
keeping attendance on its dates, rather than shifting attendance with the topic.

The CSV serializer and history exporter exclude assessment labels from regular
topic sequences. Private activated sources also store only regular topics;
original inventories and backups retain source evidence. Historical conflicts
are resolved by the latest source lesson timestamp, never by CSV file mtime.

The same-day equation derivation clears its matching buffer for every date.
Retaining prior days reprocessed old matches and generated duplicate commands;
the assessment relocation/rerun regression test checks one update per changed
occurrence and an empty plan after those topics are applied.

When a configured scope has no imported regular topics, a date override flows
the existing registry regular topics around the new assessment dates. If an
imported sequence runs out at an occurrence freed by a moved assessment, planning
stops rather than retaining a duplicate assessment there.
