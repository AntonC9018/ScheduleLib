# Ticket 18 validation and stopped handoff

Status: **partial**. Work stopped when the fresh candidate exposed a failing
CodingRules-owned contract-consumer gate. CodingRules was not edited. The existing
CleanupCode formatter, fake formatting project/solution, configuration and
source-preservation test remain intact. Proposed formatter changes and their
unvalidated fixture were removed before handoff. No new PR or package publication.

## Candidate and delivered consumer assets

CodingRules HEAD: `4749125e707a114e265bb06a19a6474134a540dd` (clean before/after).
ScheduleLib starting HEAD: `6d271e3bb2b2c19ee458195e8873463b9244716f`.
SDKs: CodingRules/gates **10.0.113**; ScheduleLib **11.0.100-preview.4.26230.115**.
JetBrains **2026.2.3.1**, running on .NET **10.0.13**.

One pack only, from the CodingRules checkout:

```sh
dotnet pack Package/CodingRules.Package.csproj -c Release --output /tmp/schedulelib-ticket18-4749125
```

Exit 0. Archive: `Anton.CodingRules.0.1.0.nupkg`; repository commit matches HEAD;
SHA-256 `88addc649ea0ccd9a0b6d5c52150e3e916a12958d634b0bb7f31a4c7abdfb81e`.
The archive contains 20 analyzer-directory DLLs (3 CodingRules, 2 SDK helpers,
15 Roslynator) and 2 library DLLs. The unsafe common Roslynator fixer is omitted;
the formatting fixer is present. The resolved package/code-fix graph supplies
Anton.SourceGeneration.Sdk 1.1.0, Roslyn 4.10.0, CodeAnalysis.Analyzers 3.3.4,
System.Composition.AttributedModel 8.0.0, and Roslynator 5.0.1. These are build/
host dependencies and bundled assets, not extra consumer PackageReferences.

From ScheduleLib, the exact adoption command that passed:

```sh
WarningsNotAsErrors='NU1902;NU1903' python3 build/use-codingrules-candidate.py /tmp/schedulelib-ticket18-4749125/Anton.CodingRules.0.1.0.nupkg
```

Exit 0; **24 projects restored**, exact candidate nupkg and **26** analyzer/fix/
configuration/guidance assets matched byte for byte. A subsequent read-only
`verify_graph` invocation from this script traversed all **24** restored projects
and verified the same candidate in each. The script now performs that traversal
automatically. Explicit guidance export matched package bytes; AGENTS.md points
to the committed export, which was read during this task. Normal build/restore
does not export guidance. The script uses an initially empty, SHA-256-specific
cache and an exact-ID-mapped local feed; it never removes a machine cache entry.

The same script without an exception initially failed restore on existing
NU1902/NU1903 advisories. An intermediate invocation with
`WarningsNotAsErrors=NU1903` still failed NU1902. The successful command above
keeps both visible; no permanent audit exception was added. Affected existing
dependencies include Pkcs and Xml 10.0.10 in the application graph. These failures
are separate from the candidate-gate blocker below.

## Existing-artifact package gates

Each command ran once, sequentially, from CodingRules, reusing that exact nupkg.
No CodingRules solution tests or other consumer gates were run.

```sh
python3 build/verify-formatting-consumer.py /tmp/schedulelib-ticket18-4749125/Anton.CodingRules.0.1.0.nupkg
python3 build/verify-computation-consumer.py /tmp/schedulelib-ticket18-4749125/Anton.CodingRules.0.1.0.nupkg
python3 build/verify-tuple-consumer.py /tmp/schedulelib-ticket18-4749125/Anton.CodingRules.0.1.0.nupkg
python3 build/verify-contract-consumer.py /tmp/schedulelib-ticket18-4749125/Anton.CodingRules.0.1.0.nupkg
```

- Formatting: **PASS**, exit 0. Exactly **21 initial build warnings**: RCS0042=1,
  RCS1016=6, CR0800=2, CR0801=8, CR0802=4. Actual exported fixes, exact golden
  output, unchanged runtime output, whitespace verification and byte-identical
  second passes passed. Final build retained only **3 deliberate caller-sensitive
  A1 warnings**. Analyzer verification returned expected exit 2 with exactly
  **11 manual diagnostics** (6 arrow-comment, 2 custom-task-like, 3 A1); these
  were explicitly checked rather than hidden.
- Computation: **PASS**, exit 0; **7 exact findings**, accepted/generated cases,
  severity transport, service-result fixes, stable second pass and runtime
  `True;6:1;paths;section;defaults/key:profiles/user/key;True:False`. The accepted
  service chain, empty/direct/simple-factory initializers and lexer custom-handler
  interpolation remained unchanged and unreported.
- Tuple: **PASS**, exit 0; **6 exact CR0900 findings**, accepted/generated cases,
  manual-only source stability and severity transport. Local-function tuple
  results remained accepted.
- Contract: **FAIL**, exit 1, at the final unresolved-signature assertion.
  Earlier passes proved **9 exact CR1000 findings**, accepted/generated/external
  signatures, manual-only source stability and warning/error/none transport.
  These partial passes do not make the gate pass.

The planned agent-guidance gate did **not run**: the sequential runner stopped
on the contract failure. Actual ScheduleLib guidance export/byte comparison above
passed independently. Full final CodingRules package validation remains the
orchestrator's responsibility after this blocker is resolved.

## Exact blocker reproduction

Run the contract command above with this archive at CodingRules `4749125`.
In `build/manual_consumer.py:70`, after `Unresolved.cs.txt` is copied to
`Unresolved.cs`, the command

```sh
dotnet build Consumer.csproj --no-restore --no-incremental
```

returns exit 1 as expected, but reports only:

```text
Unresolved.cs(5,27): error CS0246: The type or namespace name 'Missing' could not be found
Unresolved.cs(6,30): error CS0246: The type or namespace name 'Missing' could not be found
Unresolved.cs(7,32): error CS0246: The type or namespace name 'Missing' could not be found
0 Warning(s), 3 Error(s)
```

The gate asserts the union of those **3 compiler errors** and the **9 CR1000
warnings** in the independent `Findings.cs`. None of the nine analyzer warnings
appeared in that final compilation. The assertion fails at line 70. Earlier
valid compilations did report all nine warnings. No AD0001 analyzer crash was
reported. The evidence establishes a CodingRules validation mismatch; it does
not yet distinguish an analyzer defect from the gate's expectations/SDK behavior
on erroneous compilations. That investigation belongs to CodingRules; this
consumer ticket did not modify its analyzer, gate, expectations or fixtures.

## Formatter probes and remaining validation

Before that blocker arrived, a replacement was explored using snapshots of the
real consumer workspace rather than a synthesized formatting project.
The tool probe was:

```sh
dotnet jb cleanupcode --help
```

The help advertises a project or solution, but a direct `.csproj` run logged
`Custom cleanup profile is used for solution-less mode` and treated the csproj
itself as a file. Scoped C# selection returned `No items were found to cleanup`
(exit 3). Two provisional fixture runs of
`WarningsNotAsErrors='NU1902;NU1903' python3 build/verify-formatting.py` therefore
did not establish wrapper equivalence. Both checked selections failed before
apply; a separate project-only diagnostic probe confirmed solution-less mode.
An existing-solution approach was prepared but not run before the package gate
failed. It was removed rather than shipping an unproved replacement.

The provisional Python boundary checks ran with
`python3 -m unittest discover -s build/tests -v`: **5 passed** (selection/check,
preflight and per-write concurrent-edit protection, workspace-input selection,
and explicit layout diagnostic filtering). Those provisional edits/tests were
removed with the unvalidated pipeline. The original source-preservation test
remains in the repository; these five passes are investigation evidence only.
After rollback, the same unittest command passed the retained **1 original
source-preservation test**. `git diff --check` also passed.

The temporary executable fixture built with **12 warnings, 0 errors** and ran,
but a successful combined formatter apply/check/second pass was **not obtained**.
CleanupCode remains needed for braces, trailing commas, statement layout and
width-based wrapping; the candidate's isolated gate proves the A1/A2/A3/A12 slice,
not their full consumer-pipeline equivalence. No application C# source was changed.

ScheduleLib application builds, focused application tests, full solution tests,
and application formatter apply/check/second-pass validation were **not run**
after the stop condition. The known unrelated
`RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext`
failure was supplied in the handoff and was **not reproduced or fixed here**.
It remains separate from both NuGet audit baselines and the CodingRules gate
failure. Ticket 18 is not complete, and no policy decision is requested.
