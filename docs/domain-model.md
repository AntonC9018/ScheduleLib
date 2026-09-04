# Group partitions and student schedules

## Purpose

A `Group` contains every student registered under one university group identity. Students inside it may be partitioned independently by numeric subgroup, language subgroup, language proficiency, and specialization. A generated student schedule represents one combination of the active partitions.

`Group.Language` is separate metadata. It does not represent a language subgroup.

## Model

```text
Group
  lessons
    GroupPartitionKey
      SubGroup
      Specialization
  active partitions
    numeric subgroup
    language subgroup
    language proficiency
    specialization
  generated schedules
    Cartesian product of active partition values
```

### Lesson targeting

A lesson has at most one `SubGroup` and one `Specialization`. These values form its Group partition.

Examples:

| SubGroup | Specialization | Meaning |
|---|---|---|
| All | All | Shared by the whole Group |
| I | All | Numeric subgroup I |
| eng | All | English language subgroup |
| începători | All | Beginner language-proficiency subgroup |
| All | GA2D | GA2D specialization |
| I | GA2D | Numeric subgroup I within GA2D |

Two subgroup annotations on one lesson are invalid. For example, `începători-I` is invalid because both values occupy the subgroup part of the lesson split. A generated student schedule may still select both `începători` and `I`; it combines the separate lessons for those values.

Two specialization annotations on one lesson are also invalid.

### Specialization

`Specialization` is a readonly, null-backed string value. `All` means that the lesson has no specialization restriction. It is not an enum because schedule sources can add names over time.

Known values have typed constants:

```text
AG
Algoritmica Grafurilor
CV
DJ
GA2D
GA3D
Logica
React
Spring
SSI
UI
```

These are specializations, not special subgroups.

## Discovering active partitions

Only values observed on a Group's lessons count as existing for that Group. Values merely permitted by configuration do not produce schedules.

The active partitions follow these rules:

- Numeric subgroups form a contiguous prefix starting at `I`. If `X` occurs, every value from `I` through `X` must occur somewhere for the Group.
- Language subgroups have either no observed values or at least two observed values.
- Language proficiency becomes active when `începători` occurs. Its two values are `începători` and `nuîncepători`.
- No observed specializations means that specialization is inactive.
- Exactly one observed specialization is also inactive. The annotation behaves as shared for that Group.
- Two or more observed specializations activate the specialization partition.

The canonical Romanian values are `începători` and `nuîncepători`.

### A shared lesson with Group-relative specialization behavior

One real source cell produces a Spring lesson shared by `I2401`, `IA2401`, and `IA2402`.

```text
CourseId: Development of Enterprise Applications
Groups: I2401, IA2401, IA2402
Specialization: Spring
```

The Groups observe these specialization sets elsewhere in the same schedule:

```text
I2401:  Spring
IA2401: Spring, React, GA3D, DJ
IA2402: Spring, React, GA3D, DJ
```

The stored lesson keeps `Specialization.Spring`. Its effective meaning depends on the Group being filtered:

- `I2401` has only one observed specialization, so the specialization partition is inactive. The Spring lesson behaves as shared and appears in every applicable `I2401` student schedule.
- `IA2401` and `IA2402` have active specialization partitions. Their Spring schedules include the lesson, while their React, GA3D, and DJ schedules exclude it.
- The whole-Group schedule always includes it.

The model does not duplicate or mutate the shared lesson per Group.

## Language-proficiency normalization

The source format writes `începători` but leaves the complementary lessons unannotated.

For each `(Group, CourseId)` that contains an `începători` lesson:

1. Find lessons for the same Group and course whose subgroup is `All`.
2. Change those subgroup values to the derived `nuîncepători` value.
3. Preserve any specialization on those lessons.
4. Require at least one unannotated counterpart.

Lessons already restricted to another subgroup, such as `I`, `II`, or `eng`, remain unchanged. They are shared across the two proficiency values when their own subgroup dimension matches.

A source document may not write `nuîncepători` explicitly. Normalized model data and serialized caches may contain it.

A lesson shared by several Groups must not mix Groups with and without the proficiency split for that course. Such input is invalid because one stored subgroup value could not represent both meanings.

## Generated student schedules

For each Group, generate the full Cartesian product of every active partition. Do not deduplicate combinations whose lesson contents happen to match.

A combination includes a lesson when:

- the lesson's effective specialization for the Group is `All` or equals the selected specialization; and
- the lesson's subgroup is `All` or equals one of the subgroup values selected by the combination.

For example, `GA2D-începători-ru-I` includes shared lessons and lessons restricted to `GA2D`, `începători`, `ru`, or `I`. It excludes `II` and other specializations.

The whole-Group schedule is separate. It contains every lesson for the Group and does not represent one student population.

## Specialization registry

Configuration lists the specializations permitted for categories of Groups. Matching uses Group grade, faculty, attendance mode, and qualification type. A selector may omit any field; omission matches every value in that category. Overlapping selectors union their specialization sets.

Registry membership permits a value. It does not activate the specialization for a Group.

Initial permitted sets:

| Grade | Faculty | Attendance | Qualification | Specializations |
|---|---|---|---|---|
| 1 | I | Zi | Licență | Algoritmica Grafurilor, Logica |
| 1 | IA | Zi | Licență | Algoritmica Grafurilor, Logica |
| 1 | IA | Dual | Licență | Algoritmica Grafurilor, Logica |
| 2 | I | Zi | Licență | Spring |
| 2 | IA | Zi | Licență | CV, DJ, GA2D, GA3D, React, Spring, SSI, UI |

Aliases belong to schedule remapping rather than the registry:

```text
AG -> Algoritmica Grafurilor
GR -> GA2D
Node -> UI
```

## Legacy source labels

The meaning of these historical subgroup labels is unresolved:

```text
S1, S11, S12, S21, S22, S23
GA, GA1, GA2
WR, WR1, WR2
SF
UI-1, UI-2
```

Keep them as configured `SubGroup` values. Do not add constants or infer a specialization split from their digits. `UI-1` and `UI-2` need exact parser handling because the normal lexer treats the hyphen as a separator.

`optional` is another legacy marker. It once represented an unspecified specialization subgroup. It remains parseable but does not activate a subgroup partition or enter combination cardinality checks.

## Naming

Generated names use this dimension order:

```text
alternative-specialization-proficiency-language-numeric
```

Example:

```text
IA2403_GA2D-începători-ru-I.pdf
```

With an active alternative, it comes first:

```text
IA2401_A1-CV-I.pdf
```

The actual values use Romanian spelling and diacritics. A Group with no active split has only its unsuffixed whole-Group PDF.
