# Coding Standards

- **Named tuple elements:** every tuple type must name its elements at declaration (e.g. `(Partition: GroupPartitionKey, Course: CourseId)`), and all accesses must use those names — never `Item1`/`Item2`/etc.
- **Typed exceptions:** domain errors throw a specific `ScheduleBuildException` subtype (e.g. `ConflictingImplicitAssignmentException`), never a bare `InvalidOperationException` — that one is only for unreachable internal invariants (with `Debug.Assert`). New types derive from `ScheduleBuildException` so existing `InvalidOperationException` catches keep working.
