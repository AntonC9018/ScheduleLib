# Coding Standards

Single source of truth for prose code-style rules. (Machine-enforced formatting lives in `.editorconfig` and `All.sln.DotSettings`; it complements — not duplicates — this doc.)

- **Named tuple elements:** every tuple type must name its elements at declaration (e.g. `(Partition: GroupPartitionKey, Course: CourseId)`), and all accesses must use those names — never `Item1`/`Item2`/etc.
- **Typed exceptions:** domain errors in `ScheduleLib.Core` (+ Dates ranges/parity) throw a specific `ScheduleBuildException` subtype (e.g. `ConflictingImplicitAssignmentException`), never a bare `InvalidOperationException` — that one is only for unreachable internal invariants (with `Debug.Assert`). New types derive from `ScheduleBuildException` so existing `InvalidOperationException` catches keep working. Scope: Core/Dates only — infra-layer assemblies (`Helper`, `Helper.Excel`, `Helper.DependencyInjection`, `Scraping.*`) and test helpers must NOT reference Core (layering: Core depends on Helper), so their bare `InvalidOperationException` throws stay as-is.
- **Chained Try-patterns:** a chained `TryX(...) || TryY(..., out sameVar)` disjunction must live in a named helper (e.g. `Specializations.TryResolveSpecialization(value, registry, out spec)` for built-in `||` registry specialization lookup) — never inline the double disjunction at call sites.
