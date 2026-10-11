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
plan; `--apply` reconciles and applies one course/group at a time under a local
lock for the configured registry login and registry base URL. The CLI's preview/apply switch controls
processing flags, while the existing positional derivation, teacher filters,
topics, attendance mapping and extra-lesson policy remain configured in C#.
Extra lessons left alone (or requiring unsupported lossless deletion) appear as
omitted actions. Plans do not submit forms. Application stops on the first failed
submission, reports completed and unattempted actions, and never retries creates.

Apply scans and reconciles each selected course/group, then executes that group's
completed plan before scanning the next group. Local attendance/topic prerequisites
still load first. A later scan or planning failure leaves earlier writes applied
and returns a partial result (exit 6). Submission failures retain the current
group's planned but unattempted actions; groups not yet reconciled have no outcomes.
Preview visits the same groups without submissions. The account lock spans all scans and
writes, and application still stops on the first failed submission.

Lesson POSTs can succeed with an HTTP 302/303 redirect (#206). The transport blocks
automatic redirect navigation to prevent POST replay. The lesson adapter confirms
these redirects with GETs only: the destination must match the form's same-origin
lesson-list backlink, and exactly one saved edit form must match the submitted
date, type, topic and attendance values. Unexpected redirects, ambiguous/mismatched
saved values and failed confirmation reads remain uncertain; no POST is retried.

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

Intake-wide wildcard and multi-parent combined registry destinations cannot
receive labs (#220). The earlier widening in #214 incorrectly treated separate
parent labs as a shared audience. Matching now excludes them, and planning emits an
`unsafe-registry-lab-destination` warning when the selected teacher has matching
labs. It leaves the entire destination untouched and continues with other
destinations. Checking lecturer ownership after broad matching was insufficient:
the teacher filter kept their labs while removing
another lecturer's shared lecture. Skipping the destination also prevents the
positional reconciler from rewriting existing columns after those labs are removed.
Explicit parent/partition destinations and shared lectures remain eligible.

`registry cleanup-intake-labs` (#221) is a separate preview/apply path for the
guard's affected destinations. It uses current teacher/semester matching without
loading topic or attendance sources, selects only remote `Lab` columns, and counts
nonblank attendance marks in its outcomes. Other lesson types and explicit group
destinations are preserved. Every deletion rereads the selected column and refuses
changes to its date, type, topic or attendance; deletion is confirmed by absence
from a fresh lesson list. The normal synchronization guard remains unchanged.
Deletion redirects are confirmed at the delete form's known same-origin list
backlink, using its `lessonId`, through GETs only. A fresh read confirming an
unchanged column classifies the deletion as refused: the CLI reports an omitted
outcome and a warning, then continues. If an unconfirmed submission response is
followed by a read proving the column absent, the deletion is completed. Changed
columns and failed confirmation reads still stop cleanup. Account locking,
cancellation and partial outcomes reuse the sync CLI path. No deletion is
automatically retried.

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

Identical topic sequences share one course CSV and manifest scopes covering all
matching groups. When a shorter sequence is included in a longer one in the same
order, reuse the longest sequence. Different content stays separate; repeated
topics within a sequence remain intact. A course CSV can contain lectures and
labs together using `lessonType`. Manifest lesson type and language values filter
rows when those CSV columns exist and supply defaults when they do not. Use
`series` only to select distinct variants that type and language alone cannot
distinguish.

For a registry destination with separate parent-group sessions, regular topic
indexes advance independently for each scheduled audience and lesson type.
Topic lookup uses the actual lesson audience, so a shared source can serve each
parent from its first topic. Shared sessions retain one sequence, regardless of
group ordering. Assessment occurrences do not consume regular topics; fallback
to existing registry topics retains destination order. Attendance still uses
the registry roster and original lesson dates. See #215.

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

### CLI streaming output (--jsonl)

All CLI commands share `CliOutput` (`src/MainCli/CliOutput.cs`) for the
`--json`/`--jsonl` contract. `--json` keeps its single complete versioned
envelope with the original status strings; `--jsonl` streams `start`, `item`
and `final` records on stdout plus JSONL `log` records on stderr, with
independent per-stream `seq` counters and shared `command`/`runId`.
`--json` and `--jsonl` together exit 2. Explicit `--help` stays text;
`CliHost` uses a silent CommandDotNet console for `--jsonl` startup failures
so no human text mixes into JSONL, while `--json` keeps the existing human
stderr. Human result lines stream as produced and the final summary does not
repeat them. Export artifacts publish as produced; registry sync/grades stream
plan-order indexed outcomes (`planned`/`not-attempted` then terminal states
under the same id, recorded before rendering so a rendering failure cannot be
misclassified as provider uncertainty); calendar/drive stream their stable
plan-order actions the same way plus read-selection items (calendar
`deleted-N` identities and `desired-N` payloads, drive planned actions) while
finals keep counts only; `config profiles`, auth accounts and history
years/candidates stream as items with scalar-count finals, and the remaining
config/auth/history shapes stay atomic start/final batches. `CliAmbient`
carries the active `CliOutput` into `ConfiguredRegistryProvider.Open` so
`Plan` information logs render as stderr JSONL under `--jsonl`; history
providers keep `NullLoggerFactory`. `AddLessonsToOnlineRegistryHandler` logs
per-command entries whenever configured `HasLog`/`HasDryRun` holds, including
under explicit apply. `CalendarStreamingException`/`DriveStreamingException`
carry the partial result so a throwing render callback stops mutations without
retrying or reclassifying completed outcomes. Synthetic `CliJsonlTests` cover
blocked-operation early output, schema/sequence/run correlation, concurrent
per-stream sequencing, structured log/scopes/exception preservation and
filtering, stderr JSONL isolation, preview/apply log parity, argument
rejection (every command rejects `--json` with `--jsonl`), early failures,
cancellation/uncertain transitions with accurate streamed counts, throwing
render callbacks, final-without-duplicates and `--json` compatibility.

### Registry synchronization gaps (#209)

Sync diagnostics compare the selected teacher's dated schedule with registry
course and partition coverage. Duplicate links for one course are accumulated
before reporting missing destinations; a link for one parent does not cover the
other parents of a shared lecture. Missing date ranges, attendance sheets,
unmatched student counts, and assignment/partition mismatches are reported with
course/group context, without student names or credentials.

Ordinary whole-group registry links include parent-language lessons while other
language subgroups retain separate destinations. An English marker for a group
whose primary language is English resolves to its normalized whole-group lessons.
Attendance lookup and day indexes use each lesson's actual partition; explicit
lab merges project only their labs to whole-group attendance. Missing attendance
is left unchanged rather than inferred. Topic text is copied as configured.

- `RegistryConfig.GroupMatchRules` provides opt-in language matching across parent
  numbers. Global defaults enable DJ `eng` with whole English-language parents.
  Relaxation requires one registry subgroup on the course page, one eligible
  schedule audience for the course, and no repeated parent language in the intake.
  Faculty, grade, qualification, and attendance mode remain strict. Ambiguity
  falls back to the original matching path. Layered rules merge by faculty/subgroup.

- Attendance spreadsheets with text dates (`dd.MM`, optionally with a year) or
  native Excel dates in the header are recognized automatically. Date headers
  bound the attendance columns and are skipped before reading student names;
  attendance still follows lesson order. Empty worksheets produce an empty roster
  instead of failing the entire import.

- A language audience in its own parent can attend a lecture shared with a
  different parent-language group. Coverage recognizes that existing registry
  destination under the configured language rule only when course, teacher,
  room, lesson type, date/period, and intake match exactly. Ambiguous intakes
  retain separate missing-target diagnostics. The shared event is synchronized
  once; this coverage rule does not combine attendance worksheets.

- The attendance loader exposes whether any sources are configured. Registry
  planning suppresses missing-sheet and missing-student reports when attendance
  is disabled, while an enabled source with missing rows still reports its gaps.

### Numbered attendance workbooks and registry spacing

Attendance sources expose `studentNameColumn`, `headerFormat`, `dateMode`, and
`worksheetNames`; see [attendance-import.md](attendance-import.md). Date binding
is opt-in (`ValidateAndBind`) to preserve legacy column-order imports. With
headers enabled, the name column is inferred from recognized name headers,
skipping number headers and fully empty columns; without a recognized header,
the first populated column is used. `studentNameColumn` remains an override.
Attendance columns retain physical column identifiers so empty gaps do not
shift marks or header dates. Mixed
headers such as `07.09 curs` retain both the date and lesson type; unannotated
columns use the worksheet's matched type. Date arrays must be partitioned with
attendance when splitting lecture/lab lists. Missing or ambiguous dates leave
registry attendance unchanged.

ClosedXML can populate blank cells in the first row of an Excel table with
`ColumnN`, even when the table's header row is hidden. Header parsing treats
matching generated table-field names as blank, without treating arbitrary
`ColumnN` text outside a table as a header boundary.

`RegistryConfig.httpCallDelay` is a nullable `TimeSpan` override, defaulting to
five seconds. History uses its guarded transport as the sole spacing mechanism;
its generic scraping delay must be zero to avoid an additional five-second gate
when experimenting with smaller values. Short read-only discovery probes at
2, 1, and 0.5 seconds succeeded in October 2026; this does not establish sustained
batch limits or guarantee the absence of later access blocks.

### Saved lesson topic line endings (#216)

The registry can return CRLF for a multiline topic submitted with LF. Saved-form
confirmation and topic reconciliation normalize line endings before comparison;
other topic content remains significant. Confirmation still checks the date,
lesson mode, attendance, and unique saved record without replaying the POST.
Reconciliation uses the same normalization to avoid repeatedly updating an
otherwise unchanged topic.

### Global numeric lab subgroup merging (#219)

`ScheduleDerivationConfig.autoMergeLabSubGroups` defaults to false and accepts
only global defaults, with ordinary user/project precedence. The singleton
schedule provider projects raw history after fresh or cached loading and before
teacher/output filtering. Numeric lab partitions that converge to one surviving
subgroup or an explicit whole-group timetable in a newer applicable period use
the latest timetable for the entire semester. Other partition dimensions stay
intact; inconclusive period evidence produces a diagnostic.

Raw lookup IDs remain stable. The derived schedule excludes obsolete lab IDs
from lesson enumeration, common date/event iteration, and registry matching;
retained labs target `SubGroup.All` with an unspecified period so the semester
interval applies. Raw cache serialization precedes projection. Conclusive
automatic evidence supersedes a registry `labMerges` rule; legacy manual rules
remain available otherwise.

The shared dated-directory loader now reads website PDFs as well as Word
sources, using PR #68's existing entrepreneurship cohort adaptation. The
2026-09-21 snapshot from PR #68's original PDF fixtures and the 2026-10-11
capture follow the 2026-09-01 Word period; their folder dates record capture
time rather than claiming official upload dates. I2501's documented
Tuesday 15:00 pedagogy/Web conflict is retained under an exact overlap allowance.
