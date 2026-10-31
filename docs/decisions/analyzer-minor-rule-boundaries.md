# Smaller rule boundaries: Equals, casts, braces, calls

- **Status:** accepted 2026-10-24 to 2026-10-26.
- **Scope:** CR0500, CR0300, CR0803, CR0804 and comparer overloads.

## Decision

- Boolean `Equals`/`EqualsN` methods allow positional arguments, so symmetric comparisons such as
  `IgnoreDiacriticsAndCaseComparer.Instance.Equals(name, label)` need no named roles. Exact
  external methods can be registered by assembly attribute or the MSBuild property
  `CodingRulesAllowPositionalArguments`; the existing method marker remains. Role-sensitive
  calls (from/to, path/content) stay named.
- A simple numeric cast such as `Process.GetProcessById((int) pid)` is accepted for CR0300.
  CR0804 separately requires `(int) pid` spacing, and the reviewed HTML-lookup cast is accepted.
- CR0803 requires braces even for a single-line condition and statement.
- `WaitForExitBounded(..., TimeSpan.FromSeconds(2))` remains a CR0300 finding; the accepted
  simple-factory examples cover initializers, not call arguments.
- Computation names prefer the bound destination parameter, then operation or type, with
  collision handling; redundant same-type generated copies are removed. The fixer still captures
  some operands conservatively to preserve evaluation order, and numeric suffixes resolve
  collisions rather than introduce domain concepts.
- Record `with` member assignments are construction (computations in values are still checked).
  Comparer `Equals` representation overloads are accepted. Subgroup aliases use a readonly
  receiver carrying the key, leaving one effective `To` input.
- Trailing framework `CancellationToken` is excluded from CR1000 counts.
- The CR0301 fixer validates `nameof` constants directly (CodingRules `dfd239d`) so
  `from == null ? nameof(from) : nameof(to)` keeps the saved exception parameter name.
