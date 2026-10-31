# Boolean decision functions use early returns and explicit inputs (CR0200–CR0205, CR0303)

- **Status:** accepted 2026-10-26; implemented in ScheduleLib `b1c9aed`.
- **Scope:** mixed-condition lowering, Boolean decision extraction and reviewed call sites.

## Decision

- The old CR0200 mixed-condition lowering produced mutable Boolean trees, which CR0303 then
  extracted without flattening else branches. Return predicates now use explicit early exits,
  keeping the original short-circuit order. CR0205 reports a redundant else after return/throw;
  a lone return ternary is covered by CR0001. Built-in negation and equality simplify safely;
  overloaded negation keeps its operator (a runtime regression caught `False` becoming `True`).
- The `Start` output-format check still tests both `Json` and `Jsonl`: it rejects their
  combination while JSONL alone is valid. Its predicate is a guard followed by
  `return Output.Jsonl`.
- `ShouldSkipMerge` receives its `in LessonGroups groups` parameter explicitly, by `in`, rather
  than capturing it. No `LessonGroups` redesign is included.
- `StringSearchHelper.RemainingMatchText` takes the section-exhausted flag, input memory and
  matched length and returns the remaining memory; it does not capture a ref-like reader or
  mutate the caller's Boolean scratch state.
- Validation chains use separate guards at the original site; matching predicates use block
  lambdas with early returns; catch filters use named local functions evaluated inside the
  filter. RegistryHistory URL checks keep their order, same-origin policy, rejection message and
  diagnostic classification.
- All unnecessary `global::` qualifiers were removed.

## Rationale

A capturing local function cannot close over `in` parameters or ref-like readers, and each shape
needs its own review. Do not emit noncompiling captures or exempt every such decision.

## Links

- Source: `src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryLabMerge.cs`,
  `src/Features/CurriculumShared/StringSearchHelper.cs`.
- [Rollout evidence](../evidence/analyzer-rollout.md).
