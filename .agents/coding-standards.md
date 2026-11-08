# Coding Standards

Single source of truth for prose code-style rules. (Machine-enforced formatting lives in
`.editorconfig` and `All.sln.DotSettings`; it complements — not duplicates — this doc.)

- **Data contracts:** use meaningful record structs for returned, stored, or forwarded data
  aggregates. Keep separate domain concepts separate. Any remaining transient tuple must name every
  element at declaration and use those names, never `Item1`/`Item2`/etc.
- **Typed exceptions:** use meaningful application-specific exception types for application failures,
  including infrastructure operations and unreachable internal invariants.
  Expose failure context through typed read-only properties instead of only embedding it in messages.
  Define exceptions in the owning layer; infrastructure exceptions do not require a Core dependency.
- **Chained Try-patterns:** move a chained `TryX(...) || TryY(..., out sameVar)` disjunction into
  a named helper instead of repeating it at call sites.
- **Review examples:** link each source example to its exact line and show a
  short relevant excerpt.
- **LINQ syntax:** use method syntax; query expressions (`from`, `where`, `select`) are prohibited.
- **Coordinate comparisons:** name alignment, tolerance, intersection and containment operations.
  Keep their strict or inclusive boundaries and floating-point unordered behavior explicit.
- **Generated conditions:** remove negated relational-expression scaffolding in favor of meaningful
  positive predicates. Do not invert floating-point comparisons unless equivalence is proved,
  including `NaN` behavior.
- **Short chains:** keep fluent chains on one line when they fit within 90 columns.
