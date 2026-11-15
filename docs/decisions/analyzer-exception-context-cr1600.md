# Typed exception hierarchies (CR1600)

- **Status:** accepted and implemented (2026-10-25/26).
- **Scope:** existing ScheduleLib exception hierarchies. The general structured-exception rule
  belongs to CodingRules.

## Decision

- `WrongFormatException` and `CliOutputStreamingException` are abstract. Their direct
  constructions use sealed `GeneralWrongFormatException` and `GeneralCliOutputStreamingException`
  subclasses, preserving base catches. `RegistrySubmissionRejectedException` remains a base for
  the typed operation-rejection subtype pending the inheritance review ([#247][247]).
- `InvalidDataException` is sealed. ZIP-content and pre-defense member-name failures keep that
  exact framework type and attach a named immutable context value to `Exception.Data`, keyed by
  its context type. The two factory constructions carry narrowly scoped CR1600 suppressions.

## Rationale

Blindly sealing or abstracting the existing bases breaks either their subclasses or their own
factories.

[247]: https://github.com/AntonC9018/ScheduleLib/issues/247
