# PDF reader review corrections (2026-11-07)

Continues [#246][cleanup] and the existing readability clusters in [#265][clusters].
The review covers `PdfTableReader`, not completion of either issue.

## Previously missed evidence

The exact negated floating-comparison artifact was missing from the findings.
The generic condition and alias clusters did not identify this example:

```csharp
if (!(box.Left + 2 < b.Position))
{
    return false;
}
```

[Source before this correction][old-recovery]. The rewrite originated in
[the earlier decision and fluent-chain cleanup][origin].
Its preservation of unordered floating comparisons explains why simply changing
`!(a < b)` to `a >= b` is unsafe. That does not justify leaving the generated
scaffolding in application predicates. Keep positive comparisons inside a named
geometry operation instead.

## Applied corrections

- Remove unchanged `cells2` and `borders2` aliases and generated numbered plumbing.
  Border extraction now names vertical/horizontal geometry and coordinate conversion.
- Introduce `PdfCoordinateTolerance` for strict alignment, absolute separation,
  directional gap and inclusive coverage comparisons. Each retains its original
  arithmetic placement and comparison operator.
- Replace the query expression with method syntax and a named border-crossing
  predicate. Preserve vertical-outer/horizontal-inner enumeration, distinctness
  and coordinate ordering.
- Share the border-coverage policy between intersection discovery and edge checks.
- Move strict recovery containment and overlap checks into named operations.
  Keep margin two exclusive, recovery-row coverage inclusive and overlap greater
  than five. The original source rejects `NaN`; the new predicates retain that.
- Keep short chains together, including `boxes.Select(ReadCell).ToArray()`.
- Record method-only LINQ, meaningful coordinate comparisons, short-chain layout
  and removal of generated condition scaffolding in the coding standards.

The explicit method-only LINQ policy is new. The concrete floating-guard artifact
and the short-chain regression also belong in CodingRules #36 for future analyzer
or fixer work. This consumer correction changes no analyzer binary or suppression.

## Validation

All 47 PDF tests pass: the original 35 and 12 boundary cases covering inclusive
border/corner tolerance, strict recovery margins, minimum overlap and `NaN`.
The tolerance type and geometry tests produce no analyzer findings. The compact
`document.GetPages().Select(ReadPage).ToArray()` chain still produces two CR0805
warnings: the analyzer requests line breaks despite the short-chain layout policy.
Record that mismatch upstream; do not suppress it or restore unwanted formatting.
Other existing unrelated warnings remain visible. `git diff --check` passes.

[cleanup]: https://github.com/AntonC9018/ScheduleLib/issues/246
[clusters]: https://github.com/AntonC9018/ScheduleLib/issues/265
[old-recovery]:
  https://github.com/AntonC9018/ScheduleLib/blob/affb6cd2/src/Features/ParsePdf/PdfTableReader.cs#L271
[origin]: https://github.com/AntonC9018/ScheduleLib/commit/d0b7d55b0e79269f57b4b2088a3108ac1992dea3
