# Google token context, credential request and timestamp helper (CR1000, CR1300)

- **Status:** accepted 2026-10-26; implemented in ScheduleLib `b1c9aed`.
- **Scope:** owned Google credential contracts and calendar timestamp assertions.

## Decision

- Consent, Refresh and CreateCredential share the client, account and scope set through
  `GoogleTokenContext`. Authorization and storage remain distinct inputs for the stages that need
  them; cancellation remains an execution control.
- Resolve uses `GoogleCredentialRequest` for configuration, scopes, teacher and services.
- The test-local `ExpectedCalendarTimestamp(DateTimeOffset? value)` captures the shared invariant
  round-trip representation for both calendar endpoints in `CalendarSyncTests`, because both
  assert the same serialization policy. Unrelated timestamp policies are reviewed separately.

## Links

- `src/MainCli/GoogleAuthentication.cs`,
  `src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleCredentialsConfig.cs`,
  `src/MainCli/Tests/CalendarSyncTests.cs`.
