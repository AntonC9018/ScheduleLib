# ScheduleLib A1–A16 adoption ledger

This ledger follows [#229](https://github.com/AntonC9018/ScheduleLib/issues/229)
at reviewed source `12e601e7b62511bdecc44e280407fee12147b230`, superseded by the
accepted A6 and Q8–Q20 decisions in CodingRules
[`docs/schedulelib-follow-up.md`](https://github.com/AntonC9018/CodingRules/blob/070dbf9/docs/schedulelib-follow-up.md).
Ticket [#244](https://github.com/AntonC9018/ScheduleLib/issues/244) adopts the
single package; it does not implement another analyzer or publish a release.
Package identity and cache isolation are in [candidate delivery](codingrules-candidate.md).

**Covered** means the stated diagnostic/fix/configuration boundary has evidence,
not that every legacy finding was rewritten. **Manual** identifies an intentional
human/agent decision or a diagnostic without a safe automatic migration.
**Unresolved** remains policy work, never a suppressed rule or a clean-run claim.
The explicit `--style` workflow applies only CR0800/CR0801/CR0802/RCS0042/RCS1016
alongside the retained restricted CleanupCode profile and whitespace layout.
Package-only golden output proves that control-statement braces, trailing
commas, splitting same-line statements and count/width-based wrapping still
need CleanupCode. Its syntax-only project/solution is retained for that purpose;
package fixes instead run in a snapshot of the real consumer binding context.
Every architectural fix requires a separate review. No whole CR family is
disabled for adoption. Rows describe the candidate's implementation boundary;
the final column distinguishes fresh gate evidence from source-side evidence.

## Original and accepted cases

| Item | Original case and candidate coverage | Fix/configuration and accepted boundary | State and adoption evidence |
| --- | --- | --- | --- |
| A1 | AppTasks initializers at original lines 53/73/127 and Removals delegate at 269: CR0801 expands multi-member object initializers; CR0802 joins the sole positional constructor argument. Pinned reductions: CodingRules `InitializerCallFormattingTests`; packaged `Formatting/InitializerCalls.cs`. | Both exported layout fixes ran in the isolated package gate and are explicitly selected by the wrapper. Single/empty initializers, direct assignments, factory values, adjacent constructors, comments, nested initializers remain valid. Caller-information-sensitive fixes may be declined; three manual fixture warnings retain runtime values. CleanupCode still supplies broader wrapping and commas. | covered; manual safety skips; fresh formatting gate passed |
| A2 | AppTasks properties at original 346/400: RCS0042 diagnoses expanded bodyless accessor lists, including get/init. | Exported upstream fixer; explicit per-file severity plus `csharp_preserve_single_line_blocks=true`. Already compact properties stay compact; comment/attribute/directive cases are conservative skips. The isolated formatting gate checks exact compaction. | covered; fresh formatting gate passed |
| A3 | CalendarSync cancellation filter at 323 and CliRemoteFailure at 78: CR0800 moves existing trailing Boolean &&/\|\| to the next line's start. | Exported layout fix preserves evaluation; no automatic splitting of single-line expressions. Complex-filter extraction remains separate CR0201/CR0202/CR0203 work (A11). CleanupCode retains pattern-operator wrapping. | covered; manual broader filter policy; fresh formatting gate passed; compound gate deferred |
| A4 | Original plan.Add tuple, plan collection, ReportPlanned parameter and callback: CR0900 warns on tuple method results/inputs and catalogued stored collections. `TupleData/Findings.cs` pins six findings. Current calendar data uses named records from ticket 15. | Manual API/data migration; no automatic record generation. Local-function iterator results remain accepted (Q16), while private ordinary methods do not inherit that exception. Pure local swaps/general scalar-local tuple policy remain undecided. | covered diagnostic; manual migration; unresolved local policy; fresh tuple gate passed (6 findings) |
| A5 | Original static Finish (5 args) and Edit (10 args) are accepted after Q17; CR1000 instead diagnoses owned interface, virtual/abstract, delegate and abstract-class-constructor contracts with >3 args including cancellation. `ContractParameters` pins nine findings. | Manual grouping; static helpers and imposed external TryFormat signatures remain accepted. CR1001 independently flags the approved multi-parameter overload pairs; serializer builder and the shared PDF/Word payload are separate completed consumer features. No blanket count rule for ordinary nonvirtual instance methods; RequiredStep analysis is deferred. | covered diagnostic; manual builder review; unresolved broader count policy; fresh contract gate passed (9 findings; exactly 3 CS0246 errors in the separate declaration-error pass) |
| A6 | Original constructed StreamingFailure exception: CR0300; compound Report text and nested Join now follow the structured-message decision, not merely string-local extraction. Current `CalendarReports.cs`/generic reporting boundary are ticket 15 evidence. | Computation fixes are separate opt-in operations. Data records/rendering boundaries and typed exception context need semantic review, including handler gating, formatting/evaluation order and mutable snapshots. Lexer handler is explicitly accepted. A universal rule for arbitrary short messages/string APIs is open (Q7/Q9). | covered computation; manual architecture; unresolved universal message policy; fresh computation gate passed (7 findings); reporting architecture was not retested |
| A7 | Original nested Any ? 6 : 1, folder/path operations, GetOrAdd/NodeDataKey/Split, and Concat().ToArray() branches: CR0300/CR0301. `Computations/Findings.cs` and runtime fixture retain branch/lazy behavior. | Exported safe operation fixes exist, excluded from formatting. Direct/simple factories and straight pipelines are accepted; parameterless metadata exception and lazy composed-pipeline boundaries are documented in CodingRules `computed-operations.md`. | covered; fresh computation gate passed |
| A8 | Original GetGenericArguments → MakeGenericType → GetService plus service-result condition: CR0300/CR0301 and exact framework-lookup CR0203. | Separate operation fixes name intermediate values and the lookup result at its original branch. Accepted `GetRequiredService<ISlugProvider>().SlugMap(...)` receiver chain is retained; no global chaining ban. | covered; fresh computation gate passed |
| A9 | Original SettingsEdit target-typed return with file/scope/profile/key/changed: existing CR0500 binds string-role parameter names. CodingRules `NamedArguments` and `NamedArgumentTests` preserve exact source, overload binding and evaluation. | Exported named-argument fix stays outside formatting; Boolean is unchanged. Scope-name extraction is separate manual review: the parameterless ToString().ToLowerInvariant() chain is not a current CR03 finding. | covered named roles; manual scope naming; source evidence only; fresh dedicated gate deferred |
| A10 | Original desired-id invariant concatenation and file + .lock: CR1100 string-only concatenation boundary; `StringConcatenation` exact/runtime fixtures. | Exported interpolation fix preserves explicit invariant conversion, binding and evaluation; excluded from formatting. Raw literals and approved lexer handler remain accepted. General inline-message policy stays open. The existing restricted formatter does not apply CR1100. | covered; unresolved universal string policy; source evidence only; fresh dedicated gate deferred |
| A11 | Original config success/verb/type check, JSON TryGetValue validation, cancellation/authentication filters: CR0201/CR0202/CR0203. `ReviewedConditions` uses the actual pinned expressions. | Exported safe helper/guard fixes stay separate from layout. Scope-safe throws, nested property subjects and simple accepted invariants are covered. General semantic filter policy remains manual. | covered; manual generalization; source evidence only; fresh dedicated gate deferred |
| A12 | Original Compare reflection chain and Auth forwarding: RCS1016 detects multiline expressions, including method/local-function/property/accessor hosts. | CodingRules' guarded exported block fixer is selected; standard whitespace alone is insufficient. Simple single-line arrows and multiline headers alone remain accepted. Six arrow-comment and two custom-task-like cases intentionally remain unchanged; semantic naming of the reflection steps is A8. | covered; manual safety skips; fresh formatting gate passed on SDK 10.0.113; consumer SDK 11 golden workflow covers the same five IDs |
| A13 | Original null-preserving reflection assignment and generalized current-value branches: CR1200 for stable storage, with saved-source/runtime fixtures in `UnchangedAssignment`. | Exported conditional-update fix supports statement/return/property/accessor hosts, preserving result conversions. Effectful receiver/setter/indexer/ref/volatile targets remain conservative accepted cases; no speculative setter removal. | covered; source evidence only; fresh dedicated gate deferred |
| A14 | Original repeated start/end DateTimeDateTimeOffset?.ToString("o", InvariantCulture): CR1300 matching format/provider/null-propagation candidates in one method/local function. | No automatic extraction. Imported agent guidance requires semantic-role review and narrowly scoped explanation when intentionally separate. Different audit/export policies can retain separate helpers; structural similarity alone is insufficient. | covered diagnostic; manual reuse decision; source evidence only; fresh dedicated gate deferred |
| A15 | Original exact numeric set 400/403/404/409/412/422: CR0203 and named enum predicate inside the exception filter. | Exported targeted fix preserves filter timing; current consumer named predicates are prior feature work. Other numeric/status sets are outside the approved recognition boundary and remain manual review. | covered exact set; manual broader sets; source evidence only; fresh dedicated gate deferred |
| A16 | Original CalendarCommands plus its derived test seam: CR1400 flags nonabstract unsealed classes, including public/virtual/inherited classes. | No automatic sealing or test-architecture rewrite. Narrow generic type-forwarding attribute exception and concrete external framework constraints are accepted; see CodingRules `SealedClasses` fixtures. Existing inheritance remains visible for deliberate review. | covered diagnostic; manual sealing decision; source evidence only; fresh dedicated gate deferred |

## Acceptance and delivery evidence

CodingRules source-side implementation evidence is pinned to `070dbf9`; its final
common-package gate belongs to the orchestrator. This adoption runs only the
relevant existing-artifact consumer checks, never the full CodingRules solution
suite or every consumer gate. The separate commands, counts, failures and
formatter equivalence results are recorded in [validation](codingrules-validation.md).

Required accepted regressions are not exemptions invented here: service receiver
chaining, direct/empty/factory initializers and the ListStringBuilder lexer
interpolation live in CodingRules `CodeFixTests/Consumers/Computations/Accepted.cs`;
local-function tuple results in `TupleData/Accepted.cs`; imposed external
TryFormat contracts in `ContractParameters/Accepted.cs`. The fresh computation, tuple and contract gates passed their exact diagnostic
sets, leaving the accepted cases unreported and unchanged. The previous
unresolved-signature gate assertion was a gate defect: declaration errors skip
all analyzers in csc. CodingRules `070dbf9` fixes that expectation; analyzer-side
invalid-signature handling remains covered by `SkipsInvalidSignatures` unit tests.
Whole-family disabling is exercised only as a temporary negative control inside
the upstream consumer tests, then restored; it is not consumer configuration.

New feature implementation (tickets 15–17), package implementation coverage
(CodingRules tickets 01–14), this downstream adoption (18), and eventual package
publication have distinct completion evidence. Nothing in this ledger closes
the unresolved policy boundaries listed above or asserts a published release.
