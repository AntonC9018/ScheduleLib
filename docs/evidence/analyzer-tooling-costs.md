# Runner, formatter and tooling measurements

## Formatter cost

Measured on the 2-vCPU / 8-GiB worker; individual measurements, not controlled comparisons.

- Initial changed-file layout/style apply: 301.3 s for 112 solution files and 87.1 s for five CLI
  test files. After the final corrections, unchanged checks took 100.5 s and 46.4 s with zero
  source changes; named-argument passes took 62.5 s and 18.8 s.
- Unchanged full-target invocations after broad convergence: solution 3m57s / CLI 1m44s; after
  whitespace normalization 3m31s / 1m44s; the CR0006 completion pass 3m16s / 1m20s. Real-project
  formatter passes in the readability rollout took 2m44s (solution) and 2m10s (CLI).
- Routine scoped example (`build/format.sh --project
  src/Features/ConvertDocToDocxLibrary/ConvertDocToDocxLibrary.csproj --path
  src/Features/ConvertDocToDocxLibrary/OwnedConversionDirectory.cs --style --check`): the initial
  check found a line wrap and took 44.5 s, applying took 39.3 s and the **clean check took
  19.5 s**, stabilizing in one pass without changing source bytes.
- Statement-operation Fix All now reanalyzes only the edited document after each edit; the
  256-document fixture fell from 16.23 s to 0.72 s with identical saved-source hashes (see
  CodingRules' `docs/fix-all-performance.md`). It does not remove workspace loading, compilation,
  initial diagnostic discovery or safe-edit checks, and there is no persistent semantic cache.
- The `dotnet format analyzers` command includes semantic refactorings (condition and computation
  extraction) and is broader than routine formatting.

Policy for using the formatter is in [formatting](../formatting.md) and the
[formatter decision](../decisions/formatter-retains-cleanupcode.md).
