# CLI authorization

Google authorization is explicit:

```sh
schedulelib auth login google --profile "Curmanschii Anton"
schedulelib auth status google --profile "Curmanschii Anton" --json
schedulelib auth logout google --profile "Curmanschii Anton"
```

All three commands accept the shared `--project`, `--profile` and `--json`
argument models. They do not accept schedule/cache/output options. Profile
validation uses `CliSettings` and the existing coded teacher identities. Only
login creates a browser consent receiver. Login requests the existing Calendar
and Drive scopes, preserves any previously granted scopes, and authorizes each
distinct configured OAuth client once. Existing `Google` user-secrets and
`IGoogleApiKeysSource` client configuration continue to work. Calendar and Drive
may reference different OAuth clients.

Status reads local state without network calls or resolving client secrets.
It reports authorized/refreshable/expired accounts; it does not claim Google
has accepted a stored token. Missing local authorization is a successful empty
status. Logout removes the selected teacher's local accounts, is idempotent,
and performs no cloud revocation. Neither command initializes schedules.

CLI tokens live under OS LocalApplicationData/`ScheduleLib/auth/google`, with
SHA256 teacher and OAuth-client identifiers separating logical accounts. There
is one configured Google account per teacher/client; independent aliases for
multiple Google users of one teacher/client remain deferred. Tokens and client
secrets never appear in command results. On Unix token files are created with
owner-only permissions before writing. On Windows the user's state-directory
ACL applies. Teacher operation locks reject conflicting login/logout/resolution;
account locks serialize SDK persistence and atomic complete-file replacement.
SDK persistence after local logout is rejected, rather than restoring deleted
authorization. Legacy configured `credentialsPath` is retained in typed settings
but does not redirect CLI tokens outside user state. Provision legacy CLI
credentials once through explicit login; legacy hosts may refresh their
pre-existing configured FileDataStore without launching consent.

`CliRuntime.CreateServices` registers `IGoogleCredentialAuthorization` to the
CLI `GoogleAuthentication`. Other operation adapters that assemble services
independently must call `GoogleAuthentication.Register(services)` before
building the provider. The existing `GoogleCredentialResolver.Resolve` delegates
to that interface with the selected teacher and required scopes. It never
consents, including when no override is registered. Missing tokens/scopes,
missing client secrets, or failed refresh throw `AuthenticationRequiredException`;
adapters map this to exit 4 and its exact `LoginCommand`. Refresh responses may
omit unchanged refresh tokens/scopes; those are inherited and persisted.

`IGoogleTokenProvider` separates consent, refresh and credential construction for
fixture tests. `AuthProviderArguments` and `SettingsArguments` can be reused by
the Microsoft successor; extend the provider literal and route explicitly.
No Microsoft authorization, Calendar synchronization, or Drive publication is
implemented here. Real Google browser consent requires user participation and
has not been smoke-tested. Windows execution remains unverified.
