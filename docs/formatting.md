# Formatting and analyzer review

The [fresh local candidate](codingrules-candidate.md), [coverage ledger](codingrules-coverage.md)
and [validation record](codingrules-validation.md) distinguish automatic layout
from manual architectural review. CleanupCode remains necessary for capabilities
the package-only workflow does not replace.

Run the formatter from any directory using Bash (or Git Bash on Windows):

```sh
# Apply EditorConfig layout and required braces to the CLI.
build/format.sh --project src/MainCli/MainCli.csproj

# Verify without writing; suitable for a CI step.
build/format.sh --check --project src/MainCli/MainCli.csproj

# Also apply/check the package's A1/A2/A3/A12 formatting-style fixes.
build/format.sh --style --project src/MainCli/MainCli.csproj --path src/MainCli/Commands.Auth.cs
build/format.sh --style --check --project src/MainCli/MainCli.csproj --path src/MainCli/Commands.Auth.cs

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
there is no custom syntax rewriter. The default layout command does not run
Anton.CodingRules fixes or general code cleanup.

`--style` explicitly adds only CR0800, CR0801, CR0802, RCS0042 and RCS1016:
leading existing Boolean operators, multi-member initializer expansion, sole
constructor-argument adjacency, compact auto-accessors and multiline-expression
block bodies. It never selects operation extraction, argument naming, tuples,
builders, interpolation, duplication or sealing fixes. The two upstream IDs
have explicit per-file severities in `.editorconfig`; compact properties require
`csharp_preserve_single_line_blocks=true`.

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

This syntax-only host is retained deliberately. The executable package-only
probe in `build/verify-formatting.py` leaves an unbraced control statement,
missing trailing comma, same-line statements in a compact body and an unwrapped
three-argument call. The combined workflow repairs them. Therefore neither
CleanupCode nor its solution host has demonstrated a complete replacement.

For `--style`, after CleanupCode runs, its synthetic host is removed and the
wrapper stages the real consumer project, dependency sources and MSBuild/
EditorConfig inputs. It copies no application data or credentials. NuGet
configuration and the actual project references provide the fixers' binding
context; package fixes do not run against the syntax-only project. The copied
consumer is restored, the five fixes run with an explicit diagnostic/file
allowlist, then Roslyn whitespace formatting runs. Only selected source files
can be copied back. No other workspace edits are applied to the repository.
Complete layout/style rounds repeat until the selected bytes stabilize, with
a four-round limit before any source write. This handles newly introduced
blocks whose argument indentation CleanupCode must adjust. Nonconvergence
fails explicitly and leaves source untouched; stable input needs one round.

The temporary analyzer process sets `ScheduleLibFormatting=true`. This removes
the legacy `WarningsNotAsErrors` list from its design-time compiler options:
the tested CLI filters those IDs out instead of fixing them. Ordinary builds
retain their existing warning policy, and no analyzer family is disabled.

`--check` compares the temporary output and never rewrites source. Apply mode
copies only changed source back, after checking that it has not changed during
formatting. Tool caches and ignored build outputs may be created. Empty selections
fail explicitly. The wrapper supplies file selection and checking; CleanupCode
owns all formatting and EditorConfig interpretation.

The same check/apply boundary surrounds the optional package pass. A successful
`--check` means a fixing pass would leave the selected bytes unchanged; it does
not mean every diagnostic has a fix. Caller-information-sensitive layouts,
arrow comments, custom task-like return contracts and architectural findings
remain visible for manual review. A stable second pass must be checked after
applying. The source-preservation fixture also verifies comments/literals,
runtime output and that an unrelated CR1100 concatenation remains unchanged.

Run the wrapper's source-preservation regression check with
`python3 -m unittest discover -s build/tests`.
Run the actual tools/package regression with `python3 build/verify-formatting.py`.
It compares package-only and combined golden files, verifies scoped check/apply,
executes before/after source and checks byte-identical second passes. Use the
command-scoped audit warning exception described in the candidate document if
the existing application dependency advisories block restore.

Anton.CodingRules 0.1.0 is referenced centrally with `PrivateAssets="all"`.
CR warnings remain visible through `WarningsNotAsErrors` while legacy findings
are reviewed individually. Other warnings retain the existing build policy.
This is not a diagnostic baseline or suppression: builds continue to report the
actual findings. Do not bulk-fix user-authored pre-June code or disable a rule to
hide a questionable finding. Apply clear improvements; record contested findings
and the maintainer's decision in GitHub. See [analyzer review](analyzer-review.md).
