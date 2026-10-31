# Overload builders and explicit collection interfaces (CR1001, CR1500)

- **Status:** accepted and implemented; Desktop suppression is temporary.
- **Date:** 2026-10-24 (maintainer decisions, [#246][246]).
- **Scope:** CR1001, CR1500, `ManualObservableList<T>` and builder-style APIs.

## Decision

- `ManualObservableList<T>` keeps its generic operations public. Object-based `IList` members and
  non-generic-only `ICollection` members are explicit interface implementations.
  `Insert(int, T)` / `Insert(int, object?)` and `CopyTo(T[], int)` / `CopyTo(Array, int)` are
  externally required overload pairs, so a configuration builder cannot replace them.
- CR1500 generalizes that policy without hiding shared generic members such as `Count` and
  `Clear`.
- CR1001 recognizes builder declaring types and extension receivers.
  - Default suffixes are `Builder`, `Descriptor` and `Configurator`.
  - `CodingRulesBuilderSuffixes` replaces the list; suffixes use commas because semicolons are
    analyzer-config comment delimiters.
  - `ConfigurationBuilder` marks types directly.
  - Extension receivers do not count as configuration inputs; one effective parameter is allowed.
  - Explicit interface implementations are excluded.
- `LessonTopics/Builder.cs` `FallbackProvider` overloads and `MarkerConfigHelper.CreateMarkerScope`
  were judged against this rule rather than given an inner builder merely to silence it.
- Desktop `SelectionHelper` keeps an explicitly approved temporary CR1001 suppression until its
  builder redesign.

## Rationale

Creating a builder to remove a warning would obscure the scope lifetime, and an external
interface contract fixes the signature.

## Links

- Result: CR1001 has no remaining findings in the 2026-10-25 rollout
  ([evidence](../evidence/analyzer-rollout.md)).
- [CodingRules #26](https://github.com/AntonC9/CodingRules/issues/26) tracks fixer quality.

[246]: https://github.com/AntonC9/ScheduleLib/issues/246
