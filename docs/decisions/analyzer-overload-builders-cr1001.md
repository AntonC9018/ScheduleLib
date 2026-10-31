# Overload pairs required by external interfaces (CR1001, CR1500)

- **Status:** accepted and implemented; Desktop suppression is temporary.
- **Date:** 2026-10-24 (maintainer decisions, [#246][246]).
- **Scope:** `ManualObservableList<T>` and ScheduleLib builder-style APIs.

## Decision

- `ManualObservableList<T>` keeps its generic operations public. Object-based `IList` members and
  non-generic-only `ICollection` members are explicit interface implementations.
  `Insert(int, T)` / `Insert(int, object?)` and `CopyTo(T[], int)` / `CopyTo(Array, int)` are
  externally required overload pairs, so a configuration builder cannot replace them.
- `LessonTopics/Builder.cs` `FallbackProvider` overloads and `MarkerConfigHelper.CreateMarkerScope`
  keep their shape rather than gaining an inner builder merely to silence CR1001.
- Desktop `SelectionHelper` keeps an explicitly approved temporary CR1001 suppression until its
  builder redesign.

## Rationale

Creating a builder to remove a warning would obscure the scope lifetime, and an external
interface contract fixes the signature.

[246]: https://github.com/AntonC9018/ScheduleLib/issues/246
