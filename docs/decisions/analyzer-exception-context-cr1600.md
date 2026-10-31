# Typed exception context instead of string extraction (CR1600)

- **Status:** accepted and implemented (2026-10-25/26).
- **Scope:** exception messages and exception hierarchies in ScheduleLib.

## Decision

- Exception messages use CR1600 instead of CR03 string extraction. Naming an eagerly formatted
  string, or extracting a helper, does not implement the structured-exception design.
- The opt-in action generates an internal sealed framework-exception subtype with typed read-only
  context properties and preserves eager message rendering. There is no Fix All because the exact
  runtime type changes. Side effects, mutable formatting and custom exception hierarchies remain
  manual.
- Named concrete subtypes expose failure data through typed get-only properties. Templates render
  eagerly inside constructors, preserving text and culture. Mutable lists, span-backed parser
  names and PDF-cell groups are snapshotted. Tests assert the concrete subtype.
- `WrongFormatException` and `CliOutputStreamingException` are abstract. Their direct
  constructions use sealed `GeneralWrongFormatException` and `GeneralCliOutputStreamingException`
  subclasses, preserving base catches. `RegistrySubmissionRejectedException` is a base for the
  typed operation-rejection subtype; its abstract-base/general-subclass design is still part of
  the deferred inheritance review.
- `InvalidDataException` is sealed. ZIP-content and pre-defense member-name failures keep that
  exact framework type and attach a named immutable context value to `Exception.Data`, keyed by
  its context type. The two factory constructions carry narrowly scoped CR1600 suppressions that
  document this alternative; no family is disabled.

## Rationale

Short messages alone lose the data callers need, and blindly sealing or abstracting the existing
bases breaks either their subclasses or their own factories.

## Links

- [Rollout evidence](../evidence/analyzer-rollout.md).
- Exception-text policy for arbitrary short messages remains open (coverage ledger A6, Q7/Q9:
  [ledger](../evidence/codingrules-coverage-ledger.md)).
