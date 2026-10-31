# Analyzer rollout record

Work item: [#246](https://github.com/AntonC9/ScheduleLib/issues/246). This is the single
chronological record of the `Anton.CodingRules` rollout in ScheduleLib. Each dated
section describes the state at that time; the latest is at the end.
Design decisions live in [docs/decisions](../decisions/README.md).

Every count covers `All.sln` **and** the separately built `src/MainCli/Tests/MainCli.Tests.csproj`,
which is not part of the solution (solution-only counts understate the CLI cleanup). Source
warnings are deduplicated by path, location, severity, rule and message with
`build/analyzer-inventory.py`; MSBuild summary counts overlap between targets and are not
comparable. The 55 project/package advisory rows (11 NU1902, 44 NU1903) are unchanged throughout;
dependencies were not upgraded and the rows are not vulnerability remediation.

## Progression

| Date | Candidate (CodingRules) | Unique source warnings |
| --- | --- | ---: |
| 2026-10-24 baseline | none | 7,642 |
| 2026-10-24 first pass | `a78e907` | 2,902 |
| 2026-10-25 follow-up | `0f02bab` | 568 |
| 2026-10-25 review | `9d24f23` | 370 |
| 2026-10-26 exceptions/guards | `7fda0eb` | 180 |
| 2026-10-27 readability | `f2902d4` | 142 |
| 2026-10-28 reviewed scope | `97de786` | 26 of 134 |

The last row counts only the reviewed CR0300 scope (see
[remaining clusters](analyzer-remaining-clusters.md)); the latest full-inventory total is 142
(2026-10-27). The inventory baseline of 7,712 unique findings includes 70 advisory rows; an
earlier inventory missed five because restore and build emit different project suffixes (the
parser now handles both, with regression tests).

Test outcomes matched the [recorded baseline](analyzer-baseline-test-failures.md) in every
rollout: no new failures. Candidate hashes and test counts are in
[candidates](codingrules-candidates.md).

## 2026-10-24: first rollout

The first packaged pass (named arguments, interpolation, layout) changed 289 files and cut
warnings to 3,335; all 3,694 CR0500 findings disappeared. The second pass applied return,
condition, pipeline, span and assignment fixes; manual review sealed 30 concrete classes, made
two base/test fixtures abstract, named the constant curriculum glob, shared the calendar
start/end timestamp representation and suppressed the four externally required collection
overload implementations (later replaced by explicit interface implementations, see
[decision](../decisions/analyzer-overload-builders-cr1001.md)). Implementation commits `bac5d6f`
and `96f177c`.

Clean Release rebuilds: zero errors, **2,902** unique source warnings (4,740 removed; 62%).

| Remaining family | Findings |
| --- | ---: |
| CR0300–CR0302: named computation steps | 2,615 |
| CR0200–CR0203: compound conditions | 118 |
| CR0900: named tuple data | 95 |
| CR1001: overload configuration builders | 56 |
| CR1400: inheritance review | 10 |
| CR1000: contract argument groups | 7 |
| CR1300: repeated formatting review | 1 |

Review candidates from this pass (original positions, resolved by the decisions):
`WaitForExitBounded(process, TimeSpan.FromSeconds(2))`, `Process.GetProcessById((int)pid)`,
`throw new NotSupportedException($"Unknown preliminary field {field}")` in
`CurriculumNameParser`, `Equals1(name, "licenta")` positional comparisons, and
`WrongFormatException` / `CliOutputStreamingException` / `CalendarCommands` inheritance.

## 2026-10-25: follow-up rollout (`874f167`, 319 files)

Candidate CodingRules `0f02bab`; 1,401 tests and ten consumer gates passed on AWS. Release
rebuilds: solution 516 warnings, CLI 486; **568** unique source warnings (92.6% below baseline).

| Remaining family | Findings |
| --- | ---: |
| CR0300–CR0302: computation steps | 173 |
| CR1600: typed exception context | 153 |
| CR0200–CR0203: conditions | 113 |
| CR0900: named tuple data | 95 |
| CR1001: overload builders | 19 |
| CR1000: contract argument groups | 7 |
| CR1400: inheritance review | 7 |
| CR1300: repeated formatting review | 1 |

Generated continuation indentation was normalized with structural syntax and line-count checks.
Named-argument, braces/cast spacing and legacy collection API families were cleared.

## 2026-10-25: rollout review (`d0b7d55`)

Candidate `9d24f23`; 384 solution / 340 CLI Release warnings; **370** unique source warnings (198
fewer than the previous rollout, 95.2% below baseline). Cleanup included the approved
record-update layout, short type names for new computation fixes, decision-value local functions,
fluent-chain newlines, comparer-overload acceptance and the trailing-`CancellationToken`
exception. CR1001 had no remaining findings.

The original combined formatter command omitted CR0003–CR0006; decision extraction exposed 111
CR0006 findings that a completion pass cleared. Scoped JetBrains checking found one remaining
line wrap in `OwnedConversionDirectory`, applied in `4ce61ad` (35 conversion tests passed).

Remaining cases at that point, grouped for review:

| Type | Findings |
| --- | ---: |
| Typed exception context (CR1600) | 162 |
| Tuple data (CR0900) | 94 |
| Conditions (CR0200–CR0203) | 27 |
| Computation steps (CR0300–CR0302) | 32 |
| Boolean decision functions (CR0303) | 21 |
| Fluent-chain layout (CR0805), 13 in `Debug.Assert`, 9 in interpolations | 22 |
| Contract argument groups (CR1000) | 4 |
| Inheritance (CR1400) | 7 |
| Repeated formatting (CR1300) | 1 |

Representative cases: `StylesheetResource.Next` returning `(uint Id, OpenXmlElement Obj)`,
`RegistryLabMerges.Find(..., in LessonGroups groups, ...)`, `StringSearchHelper` branching on a
ref-like reader, three owned Google credential contracts, the private QuestPDF `BorderDelegate`,
and `CalendarSyncTests` formatting start/end timestamps with invariant round-trip formatting.

## 2026-10-26: exception context and guards (`7d235fe`)

Candidate `7fda0eb`; Release rebuilds pass with zero errors; **180** unique source findings
(from 370). CR1600, CR0200–CR0203 and CR0500 were clear. Remaining: 94 tuple locations, 31
computation-step, 21 decision-function, 22 fluent-layout (13 assertion-text-sensitive, nine
interpolation), three owned Google contracts, one repeated timestamp formatter and eight deferred
inheritance findings (the eighth is `RegistrySubmissionRejectedException`). The local restore
could not be verified because of the Nix CoreCLR mismatch (see
[tooling](analyzer-tooling-costs.md)); builds and tests ran on AWS.

## 2026-10-27: reviewed cleanup (`b1c9aed`)

Candidate `f2902d4`; 538 source files byte-verified (archive SHA-256
`c7ffddda66afcc85a78eaf6083a550e8ceb7ee47103834af2f5c36b490717be2`); formatting checks reported
zero changes across 502 solution files and 36 CLI test files. **142** unique findings (from 180);
the tuple, owned Google contract and repeated timestamp findings were clear, as were CR0200–CR0203,
CR0205, CR0500 and CR1600. New record constructions exposed computation-step findings, so removing
94 tuple findings did not reduce the total by 94.

| Rule | Findings | Remaining work |
| --- | ---: | --- |
| CR0001 | 5 | return decisions without an applied automatic fix |
| CR0006 | 2 | nested returns in TeacherBuilder |
| CR0300 | 90 | argument computations, including new record construction |
| CR0301 | 5 | conditional values needing named variables |
| CR0302 | 1 | computation-step extraction |
| CR0303 | 19 | decision functions needing manual capture/binding review |
| CR0400 | 3 | projection staging |
| CR0401 | 1 | local stage implementation |
| CR0801 | 1 | CliHost `JsonSerializerOptions` initializer |
| CR0805 | 7 | chain sites inside interpolated strings |
| CR1400 | 8 | deferred inheritance design |

CodeRabbit reviewed the change range in [CodingRules #35][cr35]; all four findings were fixed in
`2abd37a` (CR0205 classified as a new rule, receiver-wrapper handling in the short-chain
allowance, spec reconciliation). Candidate archive SHA-256
`b87e5d8e8d5c87a7ade0dbc3583c15a27ce354c48e2cfb1c64f457bddb81b50a`; both rebuilds pass with
142 source findings and the same 538 source hashes; full test suites were not repeated.

## 2026-10-28: reviewed consumer cleanup (`b4270ba`, `9e09740`)

Consumer source `b4270ba` cleaned the conditional-value, Boolean scratch-decision, return,
pipeline and layout clusters: reviewed findings fell from 134 to 62 (43 test constructions, 9
production constructions, 10 readable assertions). Candidate `97de786` then accepted one simple
construction per call ([decision](../decisions/analyzer-single-construction-allowance.md)),
reducing them to **26** (source snapshot `9e09740`, all 538 C# hashes match). Per-cluster
detail: [remaining clusters](analyzer-remaining-clusters.md). Full test suites at `b4270ba`:
solution 715 cases / 11 failures / 7 skipped; CLI 770 cases / 3 failures; no ScheduleLib test
cases were added. Source archive SHA-256
`4414330526cd4975fe78db5e316705e076de852455ec0afdaa5a79167156666b`.

## Artifacts

Logs, TRX reports, inventories and source hashes are retained under the ignored
`artifacts/` directory of the CodingRules checkout (not in Git): `schedulelib-cleanup-a78e907/`,
`schedulelib-review-9d24f23/` (including `logs/complete-inventory.json`),
`schedulelib-readability-f2902d4/`, `schedulelib-clusters-20261028/` and
`single-construction-97de786/`. The candidate packages remain local and unpublished; the commits
referenced here were local, unpublished commits.

[cr35]: https://github.com/AntonC9/CodingRules/pull/35

## Additional run measurements

The 384-warning solution run took 1m41s; the 340-warning CLI run took 38s.
The earlier 486-warning CLI run took 34s, and its solution run took 1m13s.
The corresponding review recorded 1,460 tests passed and 7 skipped.
