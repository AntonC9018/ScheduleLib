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
