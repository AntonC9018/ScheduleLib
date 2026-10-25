# Ticket 18 validation

Latest follow-up: [2026-10-25 rollout and grouped remaining cases](analyzer-review-2026-10-25.md). The dated results below describe earlier candidates.

Status: **consumer adoption complete**. Manual/unresolved policy in the ledger,
known test baselines and package publication remain separate.

## Fresh candidate and resolved gate issue

CodingRules was confirmed clean at
`070dbf9568783497286c953deef89cc8b5dda5d3` before the one pack. No CodingRules
source was edited, no package was published, and no new PR was opened.
The previous `4749125` candidate is superseded.

From CodingRules:

```sh
dotnet pack Package/CodingRules.Package.csproj -c Release --output /tmp/schedulelib-ticket18-070dbf9
```

Exit 0. `Anton.CodingRules.0.1.0.nupkg` SHA-256:
`cd255dca5aaa7cdec9eb28dd99bb70dc6e3d30a039c97f35b3d39f3f3a55e300`.
Its nuspec repository commit matches HEAD. The package uses SDK 1.1.0,
Roslyn 4.10.0, CodeAnalysis.Analyzers 3.3.4 and System.Composition.AttributedModel
8.0.0; the consumer receives 3 CodingRules analyzer/fix/shared DLLs, 2 SDK
helpers and 15 retained Roslynator 5.0.1 DLLs under its analyzer directory.
All 15 upstream DLLs were compared byte for byte with their pinned restored
NuGet assets and matched. The unsafe common Roslynator fixer remains omitted;
the upstream formatting fixer and CodingRules' guarded RCS1016 fixer are present.

From ScheduleLib:

```sh
WarningsNotAsErrors='NU1902;NU1903' python3 build/use-codingrules-candidate.py /tmp/schedulelib-ticket18-070dbf9/Anton.CodingRules.0.1.0.nupkg
```

Exit 0. An initially empty hash-specific cache and exact-ID source mapping
exclude the stale machine/published 0.1.0. Across **24 restored projects**, the
actual nupkg and **26 analyzer/fix/configuration/guidance assets** match the
fresh archive. After the broad solution restore/build, the same byte verifier
confirmed the exact candidate and assets in **all 39 solution projects**.
Explicit guidance export matches the archive; AGENTS.md directs
agents to read the committed export, which was read during this task. Ordinary
build/restore does not export instructions. See [candidate setup](codingrules-candidate.md).

The old contract-consumer failure was a **gate defect**, not an analyzer defect:
csc skips all analyzers on declaration errors. CodingRules `070dbf9` expects
only the three CS0246 errors in that pass. Analyzer-side invalid-signature skips
are covered by `ContractParameterAnalyzerTests.SkipsInvalidSignatures`; the
consumer gate does not claim to execute an analyzer on declaration errors.

## Packaged consumer gates

These commands each ran **once**, sequentially, against that exact archive:

```sh
python3 build/verify-contract-consumer.py /tmp/schedulelib-ticket18-070dbf9/Anton.CodingRules.0.1.0.nupkg
python3 build/verify-formatting-consumer.py /tmp/schedulelib-ticket18-070dbf9/Anton.CodingRules.0.1.0.nupkg
python3 build/verify-computation-consumer.py /tmp/schedulelib-ticket18-070dbf9/Anton.CodingRules.0.1.0.nupkg
python3 build/verify-tuple-consumer.py /tmp/schedulelib-ticket18-070dbf9/Anton.CodingRules.0.1.0.nupkg
```

All four passed (exit 0), using SDK **10.0.113**:

- Contract: **9 exact CR1000 findings** in valid sources; accepted external
  TryFormat signatures/generated sources; unchanged source after a manual-only
  fixing pass; actual warning/error/none transport. The separate failing
  compilation has exactly **3 expected CS0246 errors**, with no analyzer output.
- Formatting: **21 initial warnings** (RCS0042=1, RCS1016=6, CR0800=2, CR0801=8,
  CR0802=4); exported fixes, exact golden files, unchanged runtime and
  byte-identical second passes. Final build retains only **3 deliberate A1
  caller-information warnings**. On SDK `10.0.113`, analyzer verification's
  expected exit 2 has exactly **11 manual diagnostics**: 6 arrow-comment,
  2 custom-task-like and 3 A1. SDK `10.0.4xx`'s `dotnet format` omits the
  manual-only RCS1016 reports that offer no action and returns only the 3 A1
  diagnostics; the gate requires those 3 exactly, allows only the documented
  manual additions, and proves the manual sources stay byte-identical.
- Computation: **7 exact findings**, accepted service receiver chaining,
  empty/direct/simple-factory initializers and lexer custom-handler interpolation
  unreported/unchanged; actual fixes, severity transport, stable second pass and
  runtime `True;6:1;paths;section;defaults/key:profiles/user/key;True:False`.
- Tuple: **6 exact CR0900 findings**, accepted local-function tuple results and
  generated cases, manual-only source stability and severity transport.

The orchestrator ran the final common-package gate afterwards against the same
candidate; it is recorded below. No CR family is disabled in ScheduleLib to make
an accepted case pass.

## Formatter coverage and retained tooling

ScheduleLib uses SDK **11.0.100-preview.4.26230.115** and CleanupCode
**2026.2.3.1** (on runtime **10.0.13**). The supported package-only analyzer +
whitespace workflow proves A1/A2/A3/A12 in
`build/tests/fixtures/Formatting/PackageOnly.cs.txt`. It does **not** add the
fixture's control-statement braces or trailing commas, split statements inside
a compact body, wrap a three-argument call, or wrap a long two-argument call.
The combined `Expected.cs.txt` proves all those retained capabilities. Therefore
CleanupCode and its syntax-only solution host are deliberately retained.
Package fixes use the real consumer project/reference context in a separate
snapshot, not that synthetic project. See [workflow](formatting.md).

The opt-in `--style` pass allows only CR0800/CR0801/CR0802/RCS0042/RCS1016.
The standard CLI filtered out IDs in the legacy `WarningsNotAsErrors` list;
the isolated analyzer process sets `ScheduleLibFormatting=true` to omit that
list. Ordinary builds keep their warning policy. Explicit upstream severities
and single-line-block preservation are committed in `.editorconfig`.

A first production check exposed argument indentation needing another layout
pass after block conversion. The wrapper now repeats complete layout/style
rounds to a byte fixed point **before copying source back**, with a four-round
limit and explicit no-write failure on nonconvergence. Preflight and per-write
concurrent-edit protection remain. The executable fixture includes forwarding,
line/block comments, literal tokens, runtime output, source scoping and an
actual CR1100 warning that the format command must leave unfixed.

## ScheduleLib formatter and focused checks

Commands use `WarningsNotAsErrors='NU1902;NU1903'`; tests additionally use
`DiffEngine_Disabled=true`.

```sh
python3 -m unittest discover -s build/tests -v
python3 build/verify-formatting.py
build/format.sh --style --project src/MainCli/MainCli.csproj --path src/MainCli/Commands.Auth.cs --path src/MainCli/AppTasks.cs
build/format.sh --style --project src/MainCli/MainCli.csproj --path src/MainCli/Commands.Auth.cs --path src/MainCli/AppTasks.cs --check
build/format.sh --style --project src/MainCli/MainCli.csproj --path src/MainCli/Commands.Auth.cs --path src/MainCli/AppTasks.cs
```

All final commands passed. Unit checks: **5 passed**. The executable fixture
builds before/after with **17/10 warnings, 0 errors**; unrelated findings,
including CR1100, remain enabled. Exact package-only/combined golden files,
unchanged six-line runtime output (including trace `12345:1:2:2:3`), comments and
literal tokens pass. Its first check returns expected exit **1** without writing;
apply changes **1 selected file**, then check and second apply each report
**1 checked, 0 changes**. Dirty fixture input stabilizes in **2 internal rounds**;
stable input needs **1**. Byte equality is asserted after the second apply.

Production scope is exactly `Commands.Auth.cs` and `AppTasks.cs`. Accessor lists
are compacted, auth forwarders use blocks with explicit returns, and existing
layout is normalized. No semantic/architectural rule fixes run. Initial conversion
changed both files; its check revealed the argument-indentation ordering issue
and prompted the convergence guard. The final convergent apply reports
**2 checked, 1 remaining change** (auth indentation); final check and second
apply each report **2 checked, 0 changes**, exit 0. Both files' SHA-256 values
are compared across check and second apply; neither changes. Existing NuGet
advisory/workspace-load warnings remain visible.

Focused tests ran first:

```sh
dotnet test src/MainCli/Tests/MainCli.Tests.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~AuthenticationTests|FullyQualifiedName~CalendarReportingTests|FullyQualifiedName~ScheduleLoaderSerializationTests' --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/focused-maincli
dotnet test src/ScheduleLib/Tests/Parser/Parser.Tests.csproj -c Release -m:1 --filter FullyQualifiedName~LexerTest --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/focused-lexer
```

Passed: **27/27 MainCli tests**, **1/1 lexer test**, no failures or skips.
The current structured reporting and serializer-builder behavior is rechecked
independently from the package gates.

## Baselines and completion boundaries

Existing NU1902/NU1903 advisories in the application graph include
System.Security.Cryptography.Pkcs/Xml 10.0.10. Validation uses the command-scoped
`WarningsNotAsErrors='NU1902;NU1903'` exception, preserving warning output.
No permanent audit exception, dependency repair or rule-family suppression
was added. These advisories are unrelated to the old gate defect.

## Broad builds and tests

All heavy checks ran sequentially. The same command-scoped audit exception and
`DiffEngine_Disabled=true` were set for the following commands:

```sh
dotnet build All.sln -c Release -m:1
dotnet test All.sln -c Release --no-build --no-restore -m:1 --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/full
dotnet test src/MainCli/Tests/MainCli.Tests.csproj -c Release --no-build --no-restore -m:1 --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/maincli-full
```

- Solution build: **passed**, exit 0, **5,139 warnings, 0 errors**, 1:43.74.
  Existing/legacy findings remain visible, including the new manual rules.
- Unfiltered solution tests: exit **1**; **696 passed, 6 failed, 7 skipped**,
  **709 total**. Two helper assemblies contain no tests. MainCli.Tests is not
  listed in All.sln and therefore must be tested separately.
- Unfiltered MainCli: exit **1**; **766 passed, 1 failed, 0 skipped**, **767 total**.
  Its only failure is the supplied
  `RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext`
  (`RegistryHistoryTests.cs:428`, the synthetic canary appears in error output).
  It was not fixed or suppressed in source.

The other six solution failures were reproduced from an isolated `git archive
6d271e3` of the **starting ScheduleLib sources**, under ignored
`tmp/ticket18-baseline-6d271e3`. This is a temporary test snapshot, not another
checkout or a branch switch. It uses the same SDK/fresh candidate and a copy of
the generated NuGet.Config. The old props receive the additional current CR/RCS
IDs in the command-scoped WarningsNotAsErrors environment, so warnings remain
visible without breaking the baseline build. No rule family is disabled.

From that snapshot, these commands each reproduced the same failures (exit 1):

```sh
dotnet test src/Features/Tests/Curriculum/Curriculum.Tests.csproj -c Release -m:1 --filter FullyQualifiedName~ThesisNameTests --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/baseline-curriculum
dotnet test src/Features/Tests/WebsiteJsonSchedule/WebsiteJsonSchedule.Tests.csproj -c Release -m:1 --filter FullyQualifiedName~FmiWebsiteInteropTests.CountTest --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/baseline-website
dotnet test src/Gui/Tests/Desktop/Desktop.Tests.csproj -c Release -m:1 --filter FullyQualifiedName~Desktop.Tests.EventTests --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/baseline-desktop
```

- `Curriculum.Tests.ThesisNameTests.FileNameParsedCorrectly` and
  `CurriculumParsedCorrectly`: **2/2 failed**. The Windows-style fixture path
  yields a null curriculum key on this Linux host.
- `FmiWebsiteInterop.Tests.FmiWebsiteInteropTests.CountTest`: **1/1 failed**.
  The snapshot expects `DU2501`; starting/current code emits `DU-IA2501`.
- `Desktop.Tests.EventTests.LayerChangedTest`, `NodeChangedTest`, `ReloadWorks`:
  **3/3 failed**. The unchanged unkeyed Registry registration cannot resolve its
  required option-key string constructor parameter. Its explicit factory/default
  key belongs in a separate DI fix, not this formatter adoption.

These files and their implementation paths are unchanged from the starting
commit. No fixture/snapshot was accepted or implementation repaired to obtain
passing results.

Final regression checks exclude **only these seven exact baseline names**:

```sh
BASELINE_FILTER='FullyQualifiedName!=Curriculum.Tests.ThesisNameTests.FileNameParsedCorrectly'
BASELINE_FILTER="$BASELINE_FILTER&FullyQualifiedName!=Curriculum.Tests.ThesisNameTests.CurriculumParsedCorrectly"
BASELINE_FILTER="$BASELINE_FILTER&FullyQualifiedName!=FmiWebsiteInterop.Tests.FmiWebsiteInteropTests.CountTest"
BASELINE_FILTER="$BASELINE_FILTER&FullyQualifiedName!=Desktop.Tests.EventTests.LayerChangedTest"
BASELINE_FILTER="$BASELINE_FILTER&FullyQualifiedName!=Desktop.Tests.EventTests.NodeChangedTest"
BASELINE_FILTER="$BASELINE_FILTER&FullyQualifiedName!=Desktop.Tests.EventTests.ReloadWorks"
BASELINE_FILTER="$BASELINE_FILTER&FullyQualifiedName!=RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext"
dotnet test All.sln -c Release --no-build --no-restore -m:1 --filter "$BASELINE_FILTER" --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/solution-filtered
dotnet test src/MainCli/Tests/MainCli.Tests.csproj -c Release --no-build --no-restore -m:1 --filter "$BASELINE_FILTER" --logger trx --results-directory /tmp/schedulelib-ticket18-070dbf9/results/maincli-filtered
```

Both pass (exit **0**): solution **696 passed, 7 skipped, 0 failed** (**703 total**);
MainCli **766 passed, 0 skipped, 0 failed** (**766 total**). Combined:
**1,462 passed, 7 skipped, 0 failures** after explicit baseline exclusions.
The unfiltered suites remain red for the seven proven baselines above; these
filtered runs do not claim those failures have been fixed.

TRX results and complete logs are retained under the local
`/tmp/schedulelib-ticket18-070dbf9` artifact directory. Final source hashes match
the formatter's post-apply hashes; `git diff --check` passes. CodingRules remains
clean at `070dbf9`. The consumer changes are committed/pushed on the existing
`feat/registry-history-analysis` branch/PR #226.

## Final review follow-up and orchestrator gate

An independent final review covered CodingRules `387b3f2..070dbf9` (issue #19,
ticket 07 and the contract-gate correction) and ScheduleLib `6d271e3..28b6f66`
(ticket 18). It approved CodingRules and approved ScheduleLib with one **MAJOR**
finding: failed candidate adoption could leave the replacement `NuGet.Config`
active and could leave replacement guidance behind, because both were written
before restore/verification/export. Two follow-up commits address it:

- `0351355` restores the exact previous bytes or absence of `NuGet.Config` and
  `.codingrules/agent-coding-rules.md` on any failure after the first mutation,
  keeps the original failure (cleanup problems are attached to it), and adds
  synthetic-archive regression tests. The ignored feed/cache, the candidate
  record and partially restored `obj/` assets are explicitly **not** rolled
  back; a retry must use a forced restore.
- `16f3439` makes rollback reporting work on the documented Python 3.9+ rather
  than requiring `BaseException.add_note` (3.11+), with a test for that path.

Both were pushed on the existing branch. `python3 -m unittest discover -s
build/tests -v` passes **16 tests**; `git diff --check` is clean.

The orchestrator then ran the final common-package gate once, sequentially,
against the unchanged candidate (`cd255dca...e300`, CodingRules `070dbf9`).
CodingRules source did not change after `070dbf9` before this gate and nothing
was published. Later CodingRules commits are test/docs/gate only and leave the
archive unchanged: `faac1d4` pins the test-host `System.Security.Cryptography.Pkcs`
dependency at 9.0.21 to clear NU1902/NU1903, and `7100461` makes the formatting
consumer gate portable across SDK feature bands (SDK `10.0.4xx` omits the
manual-only RCS1016 reports; the 3 A1 diagnostics and golden bytes stay exact).
PR #34 CI is green at `7100461` on SDK `10.0.402`: all six packaged consumer
gates, package consumption tests, unit tests and provenance checks passed.

- `dotnet test CodeFixTests/CodingRules.CodeFixes.Tests.csproj -c Release
  --filter FullyQualifiedName~PackageConsumptionTests`: **8/8 passed**.
- All nine packaged consumer gates exit 0 against the archive directory:
  formatting (21 initial warnings, 3 deliberate A1 caller-information warnings
  after the manual pass), tuple (6 CR0900), contract (9 CR1000 plus the exact
  3-error unresolved pass), overload (14 CR1001), condition (6 findings),
  computation (7 findings), sealed classes (9 CR1400), repeated formatting
  (7 CR1300) and agent guidance (explicit export and read directive).
- CodingRules broad suite excluding package tests: **1,358 passed** (250
  analyzer tests, 1,108 code-fix tests), plus **15 passed** `build/tests`
  release-guard tests. The SDK was `10.0.113`.

The unfiltered ScheduleLib baseline failures in this document remain unfixed
and are not part of the final gate.

Feature implementation (tickets 15–17), upstream package implementation coverage
(tickets 01–14), consumer adoption (18) and publication have separate evidence.
This work publishes nothing. RequiredStep analysis and the unresolved policy
boundaries in the ledger remain deferred/manual.
