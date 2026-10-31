# Coding Standards

Single source of truth for prose code-style rules. (Machine-enforced formatting lives in
`.editorconfig` and `All.sln.DotSettings`; it complements — not duplicates — this doc.)

- **Data contracts:** use meaningful record structs for returned, stored, or forwarded data
  aggregates. Keep separate domain concepts separate. Any remaining transient tuple must name every
  element at declaration and use those names, never `Item1`/`Item2`/etc.
- **Typed exceptions:** throw a specific domain exception type for domain errors.
  Reserve `InvalidOperationException` for unreachable internal invariants.
- **Chained Try-patterns:** move a chained `TryX(...) || TryY(..., out sameVar)` disjunction into
  a named helper instead of repeating it at call sites.
- **Review examples:** link each source example to its exact line and show a
  short relevant excerpt.
