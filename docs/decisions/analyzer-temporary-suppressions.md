# Temporary suppressions pending design audits

- **Status:** accepted as temporary; each is tied to an open issue.
- **Scope:** CR1400 (inheritance) and CR1001 suppressions.

## Decision

- `CalendarCommands` has a class-scoped CR1400 suppression pending the
  [inheritance audit #247][247]. It has a tested virtual seam, so deleting or sealing it to
  satisfy the warning is rejected. The six related command classes (`Query`, `Auth`, `Drive`,
  `Curricula`, `Export`, `Registry`) and the `FakeGraphCommands` test seam (two subclasses) are
  reviewed in the same audit; see the audit's
  [comment](https://github.com/AntonC9018/ScheduleLib/issues/247#issuecomment-6395472858).
- Desktop `SelectionHelper` keeps its CR1001 suppression until the builder redesign
  (see [overload builders](analyzer-overload-builders-cr1001.md)).
- Baseline test failures, including the registry privacy regression, are tracked in
  [#248][248] and [#228](https://github.com/AntonC9018/ScheduleLib/issues/228). Warning cleanup
  must not claim to fix or weaken them.

[247]: https://github.com/AntonC9018/ScheduleLib/issues/247
[248]: https://github.com/AntonC9018/ScheduleLib/issues/248
