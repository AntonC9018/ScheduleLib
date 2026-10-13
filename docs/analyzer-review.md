# Anton.CodingRules review decisions

Integration work: [ScheduleLib #227](https://github.com/AntonC9018/ScheduleLib/issues/227).
Diagnostics remain visible; the formatter does not apply CR fixes. Findings in
legacy code are not presumed to justify a rewrite solely because an analyzer
reports them.

## Object-initializer assignments — rejected rewrite

The maintainer accepts direct values and factory calls in object initializers,
including `TimeSpan.FromMinutes(90)` and empty collection expressions.

Examples in user-authored code predating June 2026:

- `src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs`, `Schedule.Empty`:
  `WeeklyLessons = []`, `Groups = []`, and the other empty collections.
  Blame points to `d8b113e4` (2026-03-25).
- `src/ScheduleLib/ScheduleLib.Core/Model/ImmutableModels/TimeSlot.cs`,
  `LessonTimeConfig.CreateDefault`: `LessonDuration = TimeSpan.FromMinutes(90)`
  and `TimeSlotStarts = CreateDefaultTimeSlots()`. Blame points to `1dba62e1`
  (2025-09-22).

CR0302 classifies these initializer assignments as consumed mutations. The
assignment's value is not being forwarded to a surrounding computation;
initializing a member is its purpose. Extracting the member assignments into
named steps reduces clarity here. These examples remain unchanged and their
warnings remain visible.

[Analyzer issue #19](https://github.com/AntonC9018/CodingRules/issues/19) records the
false positives, the maintainer's decision, and requested regression coverage.
The reviewed 0.1.0 implementation has no initializer-specific EditorConfig
option. Do not disable CR0302 globally or add a configuration key the analyzer
cannot read. A corrected package should exclude the initializer assignment from
consumed-mutation classification while still checking genuine RHS complexity.
