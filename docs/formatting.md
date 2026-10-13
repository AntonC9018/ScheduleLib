# Formatting and analyzer review

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
It applies the IDE0011 brace fix and IDE0005 redundant-using fix, formats selected
C# files with JetBrains, and arranges multiline trailing commas with a Roslyn
syntax rewriter. Both standard and ReSharper layout settings in EditorConfig are
used, together with the shared `All.sln.DotSettings`. The newer MainCli slice also
enforces Roslyn code style in builds. IDE0005 requires XML documentation output;
the CLI generates it without imposing a new API-documentation requirement
(CS1591).

JetBrains runs in [file formatting mode](https://www.jetbrains.com/help/resharper/CleanupCode.html),
which performs no semantic cleanup. The comma rewriter only changes separators
in multiline initializer, collection, enum, switch and pattern lists; comments
stay attached. Neither pass applies Anton.CodingRules fixes.

Layout checking runs on temporary copies of compiled source files and their
EditorConfig files. `--check` never rewrites source; tool caches and ignored build
outputs may be created. Missing paths and empty selections fail explicitly.
The formatter refuses to overwrite a source file changed during its layout pass.
Layout and comma passes repeat to a stable result, with a bounded failure if the
formatters cannot agree.

Anton.CodingRules 0.1.0 is referenced centrally with `PrivateAssets="all"`.
CR warnings remain visible through `WarningsNotAsErrors` while legacy findings
are reviewed individually. Other warnings retain the existing build policy.
This is not a diagnostic baseline or suppression: builds continue to report the
actual findings. Do not bulk-fix user-authored pre-June code or disable a rule to
hide a questionable finding. Apply clear improvements; record contested findings
and the maintainer's decision in GitHub. See [analyzer review](analyzer-review.md).
