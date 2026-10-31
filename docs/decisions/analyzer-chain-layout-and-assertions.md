# Fluent-chain layout and assertion messages (CR0805)

- **Status:** accepted 2026-10-26; implemented in ScheduleLib `b1c9aed`.
- **Scope:** CR0805 and `Debug.Assert`-style checks.

## Decision

- A chain of at most two method calls may stay on one line when the full line is at most 90
  characters. Longer chains wrap. A wrapped argument starts below the enclosing call's opening
  parenthesis, with chain continuations indented inside that argument.
- Single-member object initializers expand.
- Reviewed assertions have explicit meaningful messages, so they no longer depend on the source
  text captured by `CallerArgumentExpression`. Converting public API assertions to configurable
  safe-by-default checks is separate work ([#249][249]).
- Readable runtime-type assertions such as
  `Debug.Assert(merger.GetType().IsAssignableTo(typeof(IMerger<T>)), "...")` remain as written,
  even though CR0300 counts `IsAssignableTo` with a type argument as a nested computation
  (`CallHelper.cs`, `OpenHierarchy.cs`; 10 findings).

## Rationale

Thirteen CR0805 findings sat inside `Debug.Assert`, where changing expression text changes the
implicit caller-expression message; seven more are inside interpolated strings, where the fixer
conservatively avoids layout edits.

[249]: https://github.com/AntonC9/ScheduleLib/issues/249
