# Structured calendar reporting (#241)

Calendar synchronization carries a `CalendarPlanStep` record struct through
planning, preview and terminal transitions. The plan-order IDs, action names,
calendar IDs and desired-event indexes retain their existing meanings.
`CalendarReport` associates the unchanged machine item with an `ICliMessage`;
the generic `Report<TMessage>` accepts the concrete message record structs.

`ICliMessage.Format(IFormatProvider)` is the formatting contract. It does not
need span formatting, format strings or a localization system. The generic
`CliOutput.WriteHuman<TMessage>` renders using the boundary's current culture
only in human mode. A different presentation can inspect the message type and
associated values without parsing English text. JSONL still serializes the
original machine item and never serializes the human message. JSON still
contains the same complete result and string-valued `actions` array.

The three replacement-summary actions are record structs, carried in
`CliCompletion.ActionMessages` until `CliCommandContext.Finish` renders the
existing JSON action strings. The other commands' existing `Actions` contract
is retained. Human summaries and JSONL finals retain their existing behavior.

## Evaluation and snapshot review

At approved snapshot `4c26748e448fe7ccbdffc3f8f6c4b05ffddce144`, the local
calendar `Report` accepted `string human`. Its interpolations used the ordinary
default string handler, without custom constructor gating, append short
circuiting, logging-level gating or custom `AppendFormatted` overloads. Hole
types were strings, integers, nullable integers and nullable `DateTimeOffset`
values, with default formatting and no alignment. Their built-in formatting
has no application-defined side effects. This change is a reviewed application
refactor, not a general automatic interpolation rewrite.

The anonymous desired-event machine item is still constructed first, including
its invariant round-trip (`o`) timestamps and original recurrence reference.
The human message then reads index, summary, start, end and recurrence in the
original order, before invoking the callback. Scalar strings and nullable
timestamps are immutable values. Recurrence is copied to an `ImmutableArray`
at the former join point; retaining the mutable SDK event/list would observe
later edits. The production desired-event builder supplies ordinary string
recurrence collections, with no user-defined formatting. Deleted identities
and action outcomes are immutable records. Desired payloads passed to the
provider and returned in the final result are intentionally still the original
SDK objects; presentation snapshots do not replace mutation inputs.

Human formatting now occurs at the output boundary, synchronously in the
normal command path, using the same current-culture default integer/date
formats. A retained message can explicitly render with another culture.
Machine modes skip unused human formatting; they still collect the human
values at the same reporting point. No human text or machine schema change is
intended.

Completed outcomes are recorded before reporting. Formatting/output failures
still stop application through `CalendarStreamingException`, preserving the
completed partial result without retry or reclassification. Cancellation,
uncertain outcomes, lock identity, desired-event indexes and recurring payloads
continue to use the existing execution path.

The calendar/reporting regressions cover culture and null fields, recurrence
snapshots after callback mutations, generic output gating, legacy action
strings, selection schemas, stable preview/apply IDs, cancellation and failures
after a completed mutation.

## Verification (2026-10-18)

Run from the repository root. The initial normal restore was blocked by
NU1902/NU1903 advisories on existing transitive Cryptography.Pkcs/Xml 10.0.10
packages. Restore/build validation used a command-scoped warning exception,
retaining the repository's existing CR warning exceptions; no dependency or
project warning policy was changed:

```sh
schedulelib_test_warnings=$(python3 -c 'import xml.etree.ElementTree as E; print((E.parse("Directory.Build.props").find(".//AntonCodingRulesDiagnosticIds").text + ";NU1902;NU1903").replace(";", "%3B"))')
DiffEngine_Disabled=true dotnet test src/MainCli/Tests/MainCli.Tests.csproj --filter 'FullyQualifiedName~Calendar' -m:1 --verbosity quiet "-p:WarningsNotAsErrors=$schedulelib_test_warnings"
DiffEngine_Disabled=true dotnet test src/MainCli/Tests/MainCli.Tests.csproj --no-build --no-restore -m:1 --verbosity quiet
```

After restore/build corrections, the focused run (with `--no-restore`) passed
**82/82** tests. The broader CLI run passed **760/761** tests, with no skips.
The sole failure was
`RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext`.
It also failed in isolation with `--no-build --no-restore --filter
FullyQualifiedName=RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext`.
`RegistryErrorLogger.GroupNotFound` logs the supplied synthetic canary group
label, violating the test's `Assert.DoesNotContain` at line 428. Both the logger
(`src/Features/OnlineRegistry/OnlineRegistry/Services.cs`) and test file have
identical Git blobs at the approved snapshot and the implementation commit;
this calendar change does not modify that registry behavior. Resolving that
separate logging/test contract is outside #241.

The repository formatter completed on the ten changed C# files, and
`git diff --check` passed. No solution-wide, package, publish or live-provider
validation was needed for this reporting slice.
