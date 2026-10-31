# Direct values in object initializers are accepted (CR0302)

- **Status:** accepted; analyzer-side regression coverage requested.
- **Date:** 2026-10 ([ScheduleLib #227][227], [CodingRules #19][cr19]).
- **Scope:** CR0302 on object-initializer member assignments.

## Decision

Direct values and factory calls in object initializers are accepted, including
`TimeSpan.FromMinutes(90)` and empty collection expressions. Extracting initializer assignments
into named steps reduces clarity, so those sites stay unchanged.

Examples (user-authored before June 2026):

- `Schedule.Empty` in `src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs`:
  `WeeklyLessons = []`, `Groups = []` and the other empty collections (`d8b113e4`, 2026-03-25).
- `LessonTimeConfig.CreateDefault` in
  `src/ScheduleLib/ScheduleLib.Core/Model/ImmutableModels/TimeSlot.cs`:
  `LessonDuration = TimeSpan.FromMinutes(90)` and `TimeSlotStarts = CreateDefaultTimeSlots()`
  (`1dba62e1`, 2025-09-22).

## Rationale

CR0302 classified these as consumed mutations, but the assigned value is not forwarded into a
surrounding computation: initializing a member is the assignment's purpose.

## Constraints

- Do not disable CR0302 globally.
- Do not add an EditorConfig key the analyzer cannot read; 0.1.0 has no initializer-specific
  option.
- A corrected package should exclude initializer assignments from consumed-mutation
  classification while still checking genuine right-hand-side complexity.

[227]: https://github.com/AntonC9018/ScheduleLib/issues/227
[cr19]: https://github.com/AntonC9018/CodingRules/issues/19
