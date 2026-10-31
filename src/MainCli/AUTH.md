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
fixture tests. Google and Microsoft share the typed `AuthProviderArguments` and
`SettingsArguments` models and route explicitly to their provider adapters.
Calendar synchronization and Drive publication are separate CLI slices. Real Google browser consent requires user participation and
has not been smoke-tested. Windows execution remains unverified.


## Microsoft authorization

```sh
schedulelib auth login microsoft --profile "Curmanschii Anton"
schedulelib auth status microsoft --profile "Curmanschii Anton" --json
schedulelib auth logout microsoft --profile "Curmanschii Anton"
```

The lowercase provider literal is `microsoft`. Microsoft `TenantId` and `ClientId`
remain in the existing `Microsoft` user-secrets section. The registered public
client must permit a desktop public-client flow with `http://localhost` as its
redirect URI. Only explicit login opens the system browser. Status/logout read
local state without resolving client configuration or initializing schedules.
Ordinary token resolution uses MSAL `AcquireTokenSilent` and may refresh provisioned
credentials; missing/unusable authorization exits 4 with the exact login command.
There is no interactive fallback. Native browser consent has not been exercised.

The supported [MSAL token-cache callbacks and serialization APIs](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization)
replace private Azure Identity reflection. Tokens live in OS LocalApplicationData
under `ScheduleLib/auth/microsoft`, partitioned by teacher, tenant/client and actual
MSAL account ID. Complete-file replacement and a profile operation lock coordinate
login, refresh, status and local logout. Token and binding files are owner-only on
Unix; Windows user-state ACLs apply. SDK exceptions and token/cache contents do not
appear in CLI results. Old invocation-directory `tokencache.bin` is not imported;
provision Microsoft authorization through explicit login once.
