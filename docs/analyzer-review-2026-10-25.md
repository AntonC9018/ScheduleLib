# Analyzer rollout review — 2026-10-25

## Validated result

ScheduleLib `d0b7d55` applies the stable CodingRules `9d24f23` candidate to
production and tests. The archive is `Anton.CodingRules.0.1.0.nupkg`, SHA-256
`c3291f06d0dd381455e1328f552c10d01a88c2a31191d48de09453376702eeb5`.
This remains a local candidate; no NuGet publication is implied.

All 1,444 CodingRules tests (262 analyzer and 1,182 code-fix/package tests)
and all 11 packaged consumer gates passed on AWS. The real formatter wrapper
also passed its scoped apply/check, runtime, literal/comment preservation and
unchanged-second-pass integration check.

Both ScheduleLib Release rebuilds pass with zero errors: the solution reports
384 warnings in 1m41s; the separate CLI test project reports 340 in 38s.
Deduplicating both targets gives **370 source warnings**, plus **55 project
advisory rows** (11 NU1902, 44 NU1903). Source warnings fell from 568 in the
previous rollout and 7,642 in the original combined baseline: 198 fewer since
the previous rollout and a 95.2% reduction from baseline. These are source-site
counts, rather than the sum of overlapping build summaries.

The cleanup includes the approved record-update layout, short type names for
new computation fixes, decision-value local functions, fluent-chain newlines,
comparer-overload acceptance and trailing CancellationToken count exception.
The approved accessor, subgroup receiver, Google login and history-output API
changes remain in place. CR1001 has no remaining findings (Desktop retains its
explicitly approved temporary suppression).

The original combined formatter command omitted CR0003–CR0006. Decision
extraction exposed 111 CR0006 findings; a completion pass included those rules
and cleared all 111. Both the broad pass and the completion pass converged,
with saved-source hashes checked before and after every invocation. All 505
transferred C# files were byte-verified against the tested final snapshot before
committing the 253 changed C# files and exported guidance.

Unfiltered ScheduleLib tests still reproduce exactly the known failing names
from [audit #248](https://github.com/AntonC9018/ScheduleLib/issues/248):
solution **712 cases: 694 passed, 7 skipped, 11 failed**; CLI **769 cases:
766 passed, 3 failed**. There are no new failing cases. The additional passing
cases come from the follow-up coverage. In particular, the existing privacy
regression test remains failing and has not been removed or weakened.

A final run excluding exactly those recorded baseline names passes: solution
694 passed / 7 skipped and CLI 766 passed, for **1,460 passed / 7 skipped /
zero failures**. These exclusions are command-local filters recorded in the
artifacts, not removed tests or repository configuration. They do not make the
unfiltered suite green.

The scoped JetBrains check then found one remaining line-wrap difference in
`OwnedConversionDirectory`. Commit `4ce61ad` applies that formatting-only change;
its unchanged scoped check passes and all 35 Microsoft/conversion tests pass.
All 505 final C# files match the retrieved AWS snapshot. No additional broad
suite run was needed for that line wrap.

Evidence is retained in the CodingRules checkout under
`artifacts/schedulelib-review-9d24f23/`: the exact candidate, final source archive
and per-file hashes, both test runs and TRX results, package gates, timing logs,
and `logs/complete-inventory.json` with every remaining location. No private
application data is included in commits.

## Remaining cases, grouped for review

A declined automatic fix is not an accepted coding-policy exemption. The
following groups describe the remaining work without requiring individual
review of hundreds of equivalent sites.

| Type | Findings | Representative case and proposed treatment |
| --- | ---: | --- |
| Typed exception context (CR1600) | 162 | `CurriculumNameParser` throws `NotSupportedException($"Unknown preliminary field {field}")`. Choose meaningful exception types and context fields by domain. The subtype action is opt-in because exact runtime types change; do not bulk-apply it. |
| Tuple data (CR0900) | 94 | `StylesheetResource.Next` / `INext.Next` return `(uint Id, OpenXmlElement Obj)`. Review related producers and callers together and introduce a named record for each shared concept. Do not group unrelated tuples merely by shape. |
| Conditions (CR0200–CR0203) | 27 | Registry URL/form validation, exception filters and nullable-flow guards. The existing fixers declined these cases. Retain filter timing, pattern-variable scope and guarded dereferences when rewriting them manually. |
| Computation steps (CR0300–CR0302) | 32 | Arguments involving registry log values, record-update values and conditional fixture content, plus one Excel parser compound update. Review evaluation order and ref-like values before extraction. |
| Boolean decision functions (CR0303) | 21 | `RegistryLabMerges.Find(..., in LessonGroups groups, ...)` branches on `groups`; `StringSearchHelper` branches on a ref-like reader. A capturing local function cannot simply close over these inputs. Prefer an explicit parameterized local function or direct guards after reviewing each shared shape. Do not emit a noncompiling capture or exempt every such decision. |
| Fluent-chain layout (CR0805) | 22 | **13** are inside `Debug.Assert`, where changing the expression text changes implicit caller-expression text. **9** are inside interpolated strings, where the fixer conservatively avoids layout edits. Keep the findings for a decision about explicit caller text / interpolation-aware fixes rather than silently changing diagnostic text or string contents. |
| Contract argument groups (CR1000) | 4 | Three owned Google credential contracts need cohesive inputs. The fourth, the private QuestPDF reflection delegate below, is a possible external-contract exception. Trailing framework CancellationToken is already excluded. |
| Inheritance (CR1400) | 7 | Six command classes (`Query`, `Auth`, `Drive`, `Curricula`, `Export`, `Registry`) and the inherited `FakeGraphCommands` test seam. Review them with CalendarCommands in [audit #247](https://github.com/AntonC9018/ScheduleLib/issues/247). FakeGraphCommands itself has two subclasses, so sealing it is not a mechanical fix. |
| Repeated formatting (CR1300) | 1 | `CalendarSyncTests` formats the expected start/end JSON timestamps with invariant round-trip formatting. A shared expected-timestamp helper appears appropriate because both assert the same serialization policy; review it separately from unrelated timestamp policies. |

### Possible external-signature exception

`GroupColumnScheduleTableDocumentGenerator.cs` declares this private delegate:

```csharp
private delegate IContainer BorderDelegate(
    IContainer element,
    float top = 0,
    float bottom = 0,
    float left = 0,
    float right = 0);
```

It binds a nonpublic QuestPDF `BorderExtensions` method through
`CreateDelegate<BorderDelegate>()`. Grouping these arguments changes the required
reflection signature. This appears to deserve the existing external-contract
rationale, expressed as a narrow documented exception or a specifically designed
registration mechanism. No blanket delegate exemption or suppression was added.

### Decisions already settled

The cast around the reviewed HTML lookup is accepted. Record `with` member
assignments are construction while computations in values remain independently
checked. Comparer Equals representation overloads are accepted. Subgroup aliases
use a readonly receiver carrying the key, leaving one effective To input.
SelectionHelper's future builder redesign remains isolated to the approved
Desktop suppression. These cases do not need another policy review.

## Formatter cost

The style wrapper runs the shared JetBrains formatting profile and the explicitly
selected packaged style fixes. The rollout's `dotnet format analyzers` command
also includes semantic refactorings such as condition and computation extraction;
it is broader than routine formatting.

Unchanged full-target invocations still cost minutes on the 2-vCPU / 8-GiB AWS
worker. After broad convergence, the no-change solution/CLI passes took
**3m57s / 1m44s**; after whitespace normalization they took **3m31s / 1m44s**.
The completion pass, scoped to the nested-return files, took **3m16s / 1m20s**
for its final unchanged checks. Every quoted invocation left the saved C# hash
unchanged. The timings are individual measurements, not a throughput guarantee
or a controlled speedup comparison between different source sets.

The document-only Fix All optimization avoids repeatedly analyzing unrelated
documents after each edit; it does not eliminate workspace loading, compilation,
initial diagnostic discovery or safe-edit checks. Use `build/format.sh --project
<project> --path <changed-file> --style` for routine scoped work. Do not run a
whole-solution semantic cleanup on every compilation.


The concrete project/path example measured for routine use was:

```sh
bash build/format.sh --project src/Features/ConvertDocToDocxLibrary/ConvertDocToDocxLibrary.csproj --path src/Features/ConvertDocToDocxLibrary/OwnedConversionDirectory.cs --style --check
```

Its initial check found a line wrap and cost 44.5 seconds. Applying it took
39.3 seconds; the subsequent **clean check took 19.5 seconds**, stabilized in
one pass, and changed no source bytes. This includes tool restore, snapshot
setup and the formatting/style pipeline. It is cheaper than a whole-target
cleanup, but is still material overhead for a single file on this worker.
