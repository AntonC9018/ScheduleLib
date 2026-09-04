# Subgroup and specialization model update

## Outcome

Replace flat, single-value PDF generation with a core model that distinguishes lesson subgroup restrictions from specialization restrictions and enumerates every valid student combination for each Group.

This change must preserve the whole-Group PDF, migrate every downstream consumer, update committed caches, and add direct tests for combination enumeration and filtering.

The durable semantics are in [domain-model.md](./domain-model.md). The project glossary is in [CONTEXT.md](../CONTEXT.md).

## Current problems

- `LessonData` has one `SubGroup`, so specialization values such as `GA2D` and `React` are mixed with numeric, language, and proficiency values.
- `WordScheduleParser` throws when a lesson contains more than one subgroup-like value.
- `SubGroupsByGroup()` returns a flat observed set.
- group PDF generation emits one file per observed value instead of a Cartesian product.
- filtering accepts one selected subgroup plus `All`, so `începători-I` cannot include both lesson sets.
- `SpecialSubGroups` hard-codes several values that are specializations.
- sanity checking rejects real historical labels that low-level parsing and committed data already accept.
- no tests cover combination enumeration, PDF naming, or multi-value filtering.

## Core model changes

### Add `Specialization`

Add a readonly, null-backed string record struct beside `SubGroup`.

Requirements:

- `Specialization.All` represents no specialization restriction.
- Equality follows the normalized stored value.
- Expose static constants for `AG`, `Algoritmica Grafurilor`, `CV`, `DJ`, `GA2D`, `GA3D`, `Logica`, `React`, `Spring`, `SSI`, and `UI`.
- Remove specialization constants from `SpecialSubGroups`.
- Do not add constants for unresolved legacy labels.

### Extend lesson data

Store all three fields directly on lesson data and builder models:

```csharp
SubGroup SubGroup;
Specialization Specialization;
Alternative Alternative;
```

Add a readonly `GroupPartitionKey` that combines all three. Expose it as a computed extension property on lesson data and equivalent lesson references. Use the key for grouping, diffing, matching, color selection, and other helper operations that must consider all three restrictions.

Do not serialize `GroupPartitionKey`.

### Add a combination abstraction

Add a core abstraction for one generated student selection. It must support:

- deterministic Cartesian-product enumeration for a Group;
- canonical display and filename ordering;
- matching a lesson's Group partition in the context of one Group;
- several selected subgroup values plus an optional specialization.

The exact internal representation is an implementation choice. Do not reuse `GroupPartitionKey`; a generated combination may contain more data than one lesson split.

## Parser changes

### Preserve raw labels

Change parser-layer subgroup modifier storage, including `ParsedLesson.SubGroup` and related modifier keys, from domain `SubGroup` values to `ReadOnlyMemory<char>` slices. Default or empty memory means no raw label.

The low-level parser identifies syntax. The schedule-aware parser and builder classify values after lesson Groups are known.

### Classification

After internal parsing:

1. Apply schedule-builder remapping.
2. Promote registered specialization labels into `Specialization`.
3. Convert remaining recognized labels into `SubGroup`.
4. Leave structurally valid unknown values for builder sanity checking, which reports the Group and registry selector context.

At most one subgroup and one specialization may be assigned to a lesson. Reject two subgroup values and reject two specialization values.

Reject an explicitly written `nuîncepători` in Word or internal lesson source parsing. Direct builder APIs and normalized JSON deserialization accept it.

### Exact dashed legacy values

Add narrow, configured parsing for `UI-1` and `UI-2`. The current lexer otherwise throws `WrongFormatException: Expected name token` because it treats the hyphen as a separator. Do not generalize arbitrary hyphenated strings into subgroup syntax.

### Legacy allowlist

Add a configuration list, with a comment that the meanings are unresolved:

```text
S1, S11, S12, S21, S22, S23
GA, GA1, GA2
WR, WR1, WR2
SF
UI-1, UI-2
```

Keep these as ordinary `SubGroup` values. Do not split their digits and do not add constants.

Keep `optional` as a parseable legacy marker outside combination discovery and cardinality checks. Its source comment already records that it represented an unspecified specialization subgroup.

## Registry and remapping

### Registry DSL

Add a specialization registry builder with set reuse and wildcard matching:

```csharp
var set = builder.Set([
    Specialization.GA2D,
    Specialization.UI,
]);

set.ApplyTo(x =>
{
    x.Grade = new(2);
    x.Faculty = new("IA");
    x.AttendanceMode = AttendanceMode.Zi;
});
```

Only fields assigned inside `ApplyTo` constrain the match. Omitted grade, faculty, attendance, or qualification fields match every value in that category. If several selectors match, union their specialization sets.

Configure the initial sets listed in the domain model.

The registry is an allowlist. For PDF and combination generation, a specialization counts only when it occurs on a lesson for the specific Group.

### Remapping

Keep aliases in `ScheduleBuilder.Remappings`, not in the registry DSL:

```text
AG -> Algoritmica Grafurilor
GR -> GA2D
Node -> UI
```

Remap before counting distinct specialization values. Alias and canonical spelling must count as one value.

## Builder processing and sanity

Run processing in this conceptual order:

1. Validate required builder data.
2. Finish internal parsing and raw-label remapping.
3. Classify subgroup and specialization values for each lesson and Group.
4. Compute observed values per Group.
5. Compute effective specializations per Group.
6. Normalize language proficiency.
7. Run sanity checks.
8. Build immutable model data.

Preserve `ValidationSettings.SubGroup = None`. Historical raw snapshot fixtures use this mode; the dedicated current-source sanity test remains strict.

### Numeric subgroups

For each Group, observed Roman numeric subgroups must form a contiguous prefix starting at `I`. If the maximum is `X`, require every value from `I` through `X` somewhere in that Group's lessons.

### Language subgroups

Recognize `ro`, `ru`, and `eng` globally. A Group has either zero observed language subgroup values or at least two. `Group.Language` does not participate.

### Specializations

For each Group:

- zero observed values means inactive;
- one observed value means inactive and behaves as shared for that Group;
- two or more observed values means active.

Do not physically erase a singleton specialization from a lesson shared with other Groups. Matching must determine effective specialization using the Group being filtered. Cover the shared Spring example from the domain model in tests.

### Language proficiency

For every `(Group, CourseId)` with an `începători` lesson:

- require at least one lesson for the same pair with `SubGroup.All`;
- change every such `All` value to `nuîncepători`;
- keep each lesson's specialization unchanged;
- leave lessons with another subgroup value unchanged;
- reject multi-Group lessons whose participating Groups disagree about whether this course has the proficiency split.

The builder constructs normalized proficiency state. Do not add redundant post-normalization checks for states that construction cannot produce.

## Combination enumeration and filtering

Replace `SubGroupsByGroup()` as the PDF-generation source with the new core combination enumeration.

For every Group:

- discover active values only from its lessons;
- take the full Cartesian product of active numeric, language, proficiency, and specialization partitions;
- retain configured opaque subgroup values as ordinary `SubGroup` data without a PDF-specific legacy branch;
- emit every combination even when two filtered lesson sets are identical;
- use deterministic ordering.

Lesson inclusion for one Group must use the combination abstraction:

- effective specialization is `All` or equals the selected specialization;
- subgroup is `All` or equals one of the selected subgroup values.

Keep the unsuffixed whole-Group PDF and include every lesson for that Group. If no split is active, generate only this file.

## PDF naming

Use `ListStringBuilder` and this fixed dimension order:

```text
alternative-specialization-proficiency-language-numeric
```

Separate the Group name from the combination with `_`, and dimensions with `-`:

```text
IA2403_GA2D-începători-ru-I.pdf
```

With an active alternative, it comes first:

```text
IA2401_A1-CV-I.pdf
```

Omit inactive dimensions. Use Romanian values, including `nuîncepători`. Do not normalize away distinct combinations based on file contents.

## Display and downstream migration

Update every consumer that assumes `SubGroup` alone describes lesson targeting:

- `LessonTextDisplayHandler` and `SubGroupNumberDisplayHandler`
- `FilteredSchedule` and `GroupFilter`
- `GeneratePdfsForGroupsAndTeachers`
- `GenerateAllTeachersExcel`
- deadlines grouping and display
- Google Calendar color keys
- free-hours logic
- FMI website schedule merging
- layered lesson configuration keys
- online-registry search, attendance, matching, and command models
- lesson diffing, lookup, and builder helper code

Print alternative first, then specialization, then subgroup:

```text
GA2D, I: Grafică și animație 2D
A1, GA2D, I: Grafică și animație 2D
```

Use the computed `GroupPartitionKey` wherever equality or grouping must include all three fields (subgroup, specialization, and alternative).

## Serialization and cache migration

Serialize flat sibling lesson fields:

```json
"SubGroup": "I",
"Specialization": "GA2D",
"Alternative": "A1"
```

Use the existing null/default convention for `All`. `Alternative` is non-required: caches written before alternatives existed deserialize a missing value as `All`. Do not serialize `GroupPartitionKey` or generated combinations.

Do not implement backward-compatible upgrade logic. Regenerate all committed caches and snapshots, including:

- `src/ScheduleLib/Tests/ScheduleFromDoc/schedule_Default.json`
- `src/ScheduleLib/Tests/ScheduleFromDoc/schedule_New.json`
- `src/Tests/data/schedule_2025_1.json`
- `Default_verify_schedule_json.verified.json`
- `New_verify_schedule_json.verified.json`
- the corresponding model verification snapshots once their skipped tests are re-enabled

Assume no external caches need migration.

## Tests

### Parser tests

- Raw modifier values remain `ReadOnlyMemory<char>` until schedule-aware classification.
- `Spring`, `CV`, and other specialization-looking raw labels parse structurally.
- Schedule-aware parsing promotes registered values into `Specialization`.
- `WR1`, `GA1`, and `S21` remain configured ordinary subgroups.
- `UI-1` and `UI-2` parse through narrow exact support.
- Explicit source `nuîncepători` fails.
- One subgroup plus one specialization succeeds.
- Two subgroups fail.
- Two specializations fail.

### Builder and registry tests

- Wildcard selectors match every omitted category value.
- Overlapping selectors union sets.
- Missing registry entries allow no specialization values.
- Unknown labels fail builder sanity with useful Group context.
- `AG`, `GR`, and `Node` remap before specialization counting.
- Numeric values require a contiguous prefix from `I`.
- One language subgroup fails; zero and two or more pass.
- Singleton specializations are inactive per Group.
- The shared Spring lesson is shared for Faculty I and restricted for Faculty IA.
- Beginner normalization derives `nuîncepători` from `All` lessons of the same `(Group, CourseId)`.
- Numeric or language lessons for the beginner course remain unchanged.
- Missing beginner counterpart and inconsistent shared Group status fail.
- `ValidationSettings.SubGroup = None` bypasses subgroup sanity for historical raw fixtures.

### Combination and PDF tests

- Enumerate every product value for several simultaneous dimensions.
- Include shared lessons plus every selected dimension's lessons.
- Exclude conflicting numeric, language, proficiency, and specialization values.
- Generate distinct beginner and non-beginner combinations for each numeric subgroup.
- Generate the whole-Group PDF unconditionally.
- Skip an empty suffixed combination when no split is active.
- Do not deduplicate identical filtered contents.
- Assert deterministic filenames and `ListStringBuilder` formatting.

### Existing test corrections

- Replace `AcceptsAllNumericAndSpecialSubGroups` because specializations no longer count as `SubGroup` constants.
- Retain the unknown `IA2504` subgroup rejection test.
- Keep `Sxx` and numbered legacy parsing tests. Their semantics remain unresolved.
- Regenerate JSON and Verify snapshots after the model migration.
- The `IntegrationTest.AllThingsWork` Verify snapshot was intentionally removed (machine-specific path baked into the expectation made it brittle).
- The existing unrelated parser skips remain out of scope.

## Current preparation already completed

- Added a clarifying comment to `SpecialSubGroups.Optional`.
- Made `ScheduleBuilder.SanityChecks()` respect `SubGroupValidationMode.None`.
- Added a regression test for disabled subgroup validation.
- Disabled subgroup sanity only for historical raw snapshot builders. The current-source sanity test remains strict.
- Accepted current snapshot drift that corrected `IASD2401` to `MIASD2401` and removed duplicate Group entries.

## Acceptance criteria

- The full relevant test suite passes after snapshot regeneration.
- No generated PDF omits a valid subgroup combination.
- `începători-I`, `nuîncepători-I`, `începători-II`, and `nuîncepători-II` schedules contain the correct union of lessons.
- Specialization, language, proficiency, and numeric values remain independent in generated combinations.
- A lesson never stores two subgroup values or two specialization values.
- Every downstream equality or grouping operation uses specialization, subgroup, and alternative where lesson targeting matters.
- The whole-Group PDF remains available.
- Generated filenames are deterministic.
- Committed JSON contains normalized `nuîncepători` and a separate `Specialization` field.

## Known unrelated failure

At the time this spec was written, the full `ScheduleFromDoc.Tests` run had one unrelated failure:

```text
DateProviderTests.AcademicCalendar2026_2027MatchesPublishedDates
```

The test expects the week beginning 2026-08-31 to be odd, but `Config.StudyWeeks` currently marks it even. Do not weaken or update that test as part of this subgroup migration. Resolve it through the academic-calendar configuration workflow.
