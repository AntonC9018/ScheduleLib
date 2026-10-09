# Calendar replacement

```sh
schedulelib calendar sync --profile "Nartea Nichita" --json
schedulelib calendar sync --profile "Nartea Nichita" --apply --json
```

`calendar sync` composes source/cache, project/profile, result and shared apply
arguments. It requires a configured teacher present in the schedule and Google
Calendar credentials provisioned by `auth login google --profile TEACHER`.
Ordinary synchronization never initiates consent. Academic settings remain in
C#; the existing latest-period teacher filter, dates, recurrence, descriptions,
colors, Europe/Chisinau timezone and location are retained. No general output
option or calendar selector is added; the destination is the resolved
`GoogleCalendarConfig.CalendarName`.

Preview reads the actual account (the primary calendar ID), lists calendars,
selects the first matching summary and lists that calendar's existing event
IDs. Text and JSON report deletion of that calendar and its events, creation
of a calendar with a new ID, and all desired recurring event payloads. Multiple
matching summaries are disclosed. An empty desired set is disclosed and still
means replacement with an empty calendar. The primary literal and a matching
actual primary calendar are prohibited.

Apply obtains a local account/name lock and reads current calendars and events
inside it. It does not replay a saved preview. Actions execute once in order:
delete the first match, create the calendar, create each desired event. There
is no incremental reconciliation, retry of uncertain creates or rollback.
Completed actions expose calendar/event IDs. Each event outcome includes a
zero-based `desiredEventIndex` into the desired payloads. Cancellation returns
130 with completed and any uncertain in-flight action; an interrupted or lost
response may mean that Google performed the mutation. Inspect the destination
before another apply. A definite rejected first action returns 5; completed or
uncertain mutations followed by failure return 6. Lock conflicts return 7 and
missing or failed authorization returns 4 with the login command. Known
pre-send credential failures mark the action failed without sending a mutation;
authentication failures after completed actions return 6 with the same hint.
Pre-send refresh timeouts and connection failures mark the action failed and
return 5 before any completed mutation or 6 after earlier completed actions;
they do not request a new login. Only caller cancellation returns 130. Read timeouts/connection failures return
5; mutation timeouts remain uncertain with exit 6. Create completion requires a
nonblank returned ID; a missing identity stops application as uncertain.

`ICalendarSyncProvider` is the fakeable read/write boundary; `CalendarSync.Run`
is the replacement engine. `GoogleCalendarSyncProvider` uses provisioned
credentials through `CliRuntime`'s `GoogleAuthentication.Register` hook and
performs paginated reads without swallowing Google failures. Its SDK transport
disables retries and redirects, and wraps credentials to preserve pre-send
refresh without response-triggered refresh/replay. No live calendar
mutation or browser consent was used for verification. Windows execution is
unverified.
