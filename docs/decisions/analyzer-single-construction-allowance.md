# One simple constructed argument per call is accepted (CR0300)

- **Status:** accepted 2026-10-28; implemented in CodingRules candidate `97de786`.
- **Scope:** CR0300 on arguments that construct objects.

## Decision

One simple directly constructed argument per call is accepted, in explicit and target-typed
`new(...)` forms and alongside other simple arguments. Multiple directly constructed arguments,
nested constructors, producer calls, conditional values and numeric calculations inside
construction inputs keep CR0300. Simple collection inputs remain accepted.

Example: `fixture.Attendance(new Fixture.AttendanceSource(key, ["Synthetic Ana"]));`

## Rationale

The allowance removed 27 test findings and all 9 simple production-construction findings. The
remaining multi-roster calls should name their inputs or use a purposeful fixture builder where
that clarifies setup; the rule does not force an arbitrary builder.

## Links

- [Remaining-case evidence](../evidence/analyzer-remaining-clusters.md).
- [CodingRules #36](https://github.com/AntonC9/CodingRules/issues/36) (review cases) and
  [CodingRules #37](https://github.com/AntonC9/CodingRules/issues/37) (deferred scope/name
  proposal).
- CLI design is recorded for later in [#250](https://github.com/AntonC9/ScheduleLib/issues/250).
