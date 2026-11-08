# Analyzer diagnostics stay visible

- **Status:** accepted, in force.
- **Date:** 2026-10 (CodingRules adoption, [#227][227], [#244][244]).
- **Scope:** how `Anton.CodingRules` findings are treated in ScheduleLib builds.

## Decision

- `Anton.CodingRules` 0.1.0 is referenced centrally with `PrivateAssets="all"`.
- CR warnings stay visible through `WarningsNotAsErrors`. They are neither a diagnostic baseline
  nor a suppression list.
- The formatter does not apply CR fixes. Only the explicit `--style` allowlist runs
  (see [formatting](../formatting.md)).
- No CR family is disabled to make a case pass. A declined automatic fix is not an accepted
  exemption.
- Findings in legacy (pre-June 2026) user-authored code are not presumed to justify a rewrite.
  Clear improvements are applied; contested findings and the maintainer's decision are recorded
  in GitHub.
- Bulk CR0300–CR0302 fixes are held. A saved-source probe of `CliSettings.Removals.cs` showed
  the expansion mode creates redundant `operationValue` copies and the default extraction adds
  many `CalculateValue` helpers. The warnings stay visible until naming and capture quality
  improve ([CodingRules #26][cr26]).

## Rationale

A clean build is not evidence that manual findings or unresolved policies are complete, and a
fixer that satisfies a rule while hurting readability defeats the rule's purpose.

## Links

- [Rollout evidence](../evidence/analyzer-rollout.md).
- [Candidate adoption](codingrules-candidate-adoption.md).

[227]: https://github.com/AntonC9018/ScheduleLib/issues/227
[244]: https://github.com/AntonC9018/ScheduleLib/issues/244
[cr26]: https://github.com/AntonC9018/CodingRules/issues/26
