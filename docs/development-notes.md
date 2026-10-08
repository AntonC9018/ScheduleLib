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
