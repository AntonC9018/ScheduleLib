# Formatting and analyzer review

Ticket 18 has delivered a [fresh local candidate](codingrules-candidate.md) and
[coverage ledger](codingrules-coverage.md), but formatter replacement stopped at
a [CodingRules consumer-gate failure](codingrules-validation.md). The workflow
below is retained pending that gate and demonstrated replacement equivalence.

Run the formatter from any directory using Bash (or Git Bash on Windows):

```sh
# Apply EditorConfig layout and required braces to the CLI.
build/format.sh --project src/MainCli/MainCli.csproj

# Verify without writing; suitable for a CI step.
build/format.sh --check --project src/MainCli/MainCli.csproj

# Include tests in the CLI slice, or narrow a solution run to selected paths.
build/format.sh --project src/MainCli/Tests/MainCli.Tests.csproj
build/format.sh --path src/MainCli
```

`--path` is repeatable. Directory paths include their descendants. A full-solution
check uses `build/format.sh --check`; a deliberate full-solution rewrite uses
`build/format.sh --yes`. Prefer the project/path scope for routine edits so
formatting changes stay reviewable.

The utility needs Bash, Python 3.9+ and the repository's .NET SDK. It restores
JetBrains.ReSharper.GlobalTools 2026.2.3.1 from `dotnet-tools.json` automatically.
The `ScheduleLib Formatting` cleanup profile in `All.sln.DotSettings` enables
only C# reformatting, control-statement braces, and trailing commas. CleanupCode
implements those operations using standard and ReSharper EditorConfig settings;
there is no custom syntax rewriter. It does not run Anton.CodingRules fixes,
reorder members, change declarations, or perform general code cleanup.

The newer MainCli slice also enforces Roslyn code style in builds. IDE0005
requires XML documentation output; the CLI generates it without imposing a new
API-documentation requirement (CS1591). Redundant usings are checked by builds;
the formatting profile leaves them unchanged.

The wrapper selects compiled C# files using MSBuild and copies them and their
EditorConfig files to a temporary formatting-only solution. CleanupCode requires
a solution to apply syntax styles; file-only mode applies layout alone. The
temporary project targets .NET Standard 2.1 with preview language syntax and no
application dependencies or analyzers. Its role is formatting, not compilation.
See the [CleanupCode documentation](https://www.jetbrains.com/help/resharper/CleanupCode.html).

`--check` compares the temporary output and never rewrites source. Apply mode
copies only changed source back, after checking that it has not changed during
formatting. Tool caches and ignored build outputs may be created. Empty selections
fail explicitly. The wrapper supplies file selection and checking; CleanupCode
owns all formatting and EditorConfig interpretation.

Run the wrapper's source-preservation regression check with
`python3 -m unittest discover -s build/tests`.

Anton.CodingRules 0.1.0 is referenced centrally with `PrivateAssets="all"`.
CR warnings remain visible through `WarningsNotAsErrors` while legacy findings
are reviewed individually. Other warnings retain the existing build policy.
This is not a diagnostic baseline or suppression: builds continue to report the
actual findings. Do not bulk-fix user-authored pre-June code or disable a rule to
hide a questionable finding. Apply clear improvements; record contested findings
and the maintainer's decision in GitHub. See [analyzer review](analyzer-review.md).
