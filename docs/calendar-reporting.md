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
