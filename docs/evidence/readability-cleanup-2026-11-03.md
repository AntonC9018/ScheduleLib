# Readability cleanup and Desktop baseline repair (2026-11-03)

Tracks [#246][rollout], [#265][readability], [#244][adoption] and [#271][desktop].
The starting ScheduleLib snapshot is `7a78e8f0`. See the preceding
[reconciliation record](reconciliation-2026-11-02.md) for package validation and baseline tests.

## Candidate and scope

The candidate remains CodingRules `8e727c91a2795665e09441b792e770ae892af756`, packaged as
`0.1.0-local.20261102`. Its archive SHA-256 is
`0c1701f04b372d26942391f1938225720278272015fb5cb40650678aa11e3a3f`.
No analyzer source or diagnostic-severity configuration changed in this pass.
The preceding fresh validation of 1,664 analyzer/fixer/package tests and twelve consumer gates
continues to describe this exact candidate. Upstream merging and publication remain separate.

## Consumer implementation

- Reviewed and removed or renamed all 744 zero-argument `Calculate*`/`CheckCondition` helper
  definitions present at the starting snapshot. The reused `CalculateSchedulePath` test helper
  takes an argument and remains purposeful. No generated `TODO_` names are retained.
- Short conditions now use ordered direct expressions. Larger branching decisions use domain
  names and guard clauses. Speculative parser reads keep their state handling; ref-like contexts
  are passed explicitly rather than captured.
- Removed redundant receiver copies and numbered arithmetic pipelines. Necessary stages use
  domain names: registry destinations, fixture inputs, worksheet ranges, bit masks and dates.
  Computations remain inside their original asynchronous, exception and reporting callbacks.
- Staged nested chain lambdas inside their original callback bodies. The stages preserve lazy
  enumeration and short-circuit order. Constant UTF-8 data uses literals with explicit arrays
  where the receiving API requires `byte[]`.
- Applied the validated argument-naming and explicit-return fixes. Custom interpolation handlers
  and readable public assertions retain their contracts. No command inheritance redesign or
  public-API validation migration is included.
- Removed orphan Python tests for the intentionally retired one-off inventory script. The
  inventory itself runs as a temporary inspection utility; it is not restored as project tooling.

## Formatter integration

`--style` now includes CR0806, the reviewed chopped-first-argument layout rule. Its existing
allowlist still excludes argument naming and semantic computation refactors. Updated golden
fixtures exercise that additional layout without changing output behavior.

The real-tool formatter regression passes scoped check/apply, comments and literal preservation,
unchanged runtime output, exclusion of unrelated semantic fixes and a byte-identical second pass.
The Python tooling suite passes all sixteen tests. CleanupCode remains necessary for the
capabilities its package-only probe does not replace; #244 is not a claim that it can be removed.

## Desktop repair

The default registry registration now supplies its option key explicitly. Default configuration
uses `Options.DefaultName`; using null with named configuration would apply to every option set.
Two regressions cover validated service construction, default/named isolation and singleton reuse.
The teacher-name mock now has an initialized empty observable source.

The event fixture also excludes the unfinished property-editor display factory. Its production
`CreateFactory` still throws `NotImplementedException`, and is outside layer/node event coverage.
Its integration remains in [#272][ui]; this fixture repair does not implement the property editor.
All nineteen focused Desktop tests pass, including the three formerly failing event tests.
No machine-specific spreadsheet is required by these tests in the current source.

## Validation and remaining findings

Both clean Release rebuilds pass, covering `All.sln` and the separately built CLI test project.
Runs are serial and local, with `DiffEngine_Disabled=true`, `SCHEDULELIB_LIVE_TESTS=0` and
command-scoped `WarningsNotAsErrors='NU1902;NU1903'` for existing dependency advisories.

- Solution: 788 cases, 779 passed, eight skipped and one failed.
- Separate CLI suite: all 812 passed, with no skips.
- All 35 PDF tests and all nineteen Desktop tests pass.
- The only solution failure is `ThesisNameTests.CurriculumParsedCorrectly`, the unchanged
  unfinished curriculum parser requiring the scope/domain decision in #248.
- The final runtime pass includes the last explicit-return corrections and the four `CliHost`
  chopped-argument corrections.
The fresh deduplicated inventory contains **192 source findings**.
The starting snapshot contains 497. Both counts cover solution and separate CLI builds.
Repeated build summaries and shared-source compilation are deduplicated by source location, severity, rule and message.
The full saved-site inventory is [the CSV](analyzer-findings-2026-11-03.csv).

| Remaining family | Sites | Review boundary |
| --- | ---: | --- |
| CR0300 argument computations | 131 | Mainly fixture construction and assertion expressions |
| CR0302 operation staging | 26 | Compact arithmetic, indexing and configuration assignments |
| CR0201–CR0203 conditions | 22 | Short-circuit reads, patterns and method-call classification |
| CR0401 stage implementation | 1 | Reviewed block stage still requests a named function |
| CR1100 string construction | 3 | Synthetic test command construction |
| CR1400 inheritance | 6 | Command seams and an existing exception extension contract |
| CR1600 typed exception context | 3 | Output-only converters and an unreachable Drive action case |

CR0001/CR0003–CR0006, CR0301, CR0404, CR0500, CR0701, CR0800–CR0806 and RCS1016 have no remaining
findings in these builds. No rule family was disabled to obtain that result.

### Concrete remaining shapes

- **Fixture/input constructions:** [registry reader setup][reader-setup] still constructs a login
  URI and a pacing interval directly. Several roster tests likewise pass two partition values.
  These are distinct from the removed generic functions; deciding whether their inline setup
  merits extra names is remaining consumer review, not a reason to restore generated wrappers.
- **Assertions and observed values:** [subgroup parsing assertions][parser-assertions] keep the
  `TryFromNamePrefix(..., out var ...)` call beside `Assert.True`. JSON assertions similarly
  retain the property access beside the expected value. These are not declared blanket analyzer
  exemptions; representative assertion/fixture policy is still tracked by CodingRules #36.
- **Arithmetic:** [PDF row offsets][pdf-arithmetic] and [buffer writes][buffer-writes] retain
  compact expressions such as `arr[written++] = item`. Expanding those needs operation-order
  review; it must not recreate numbered temporaries or alter the increment timing.
- **Conditions:** [parser token guards][token-guard] retain ordered checks such as
  `t.Type == LessonTokenType.Word && t.Value.Length == 1 && char.IsLetter(t.Value.Span[0])`.
  The accepted short-condition policy and older condition-rule classifications need upstream
  reconciliation. The findings remain visible; there is no new suppression.
- **Architecture:** command inheritance stays with #247; safe/unsafe public checks stay with #249.
  Typed converter failures and the submission-rejection exception are explicit manual API
  decisions. The unfinished curriculum parser remains #248. The property editor remains #272.

#246 remains open for that consumer review and upstream rule reconciliation. The #265 generated
helper clusters are corrected, but its cross-repository merge/publication sequence is not done.
#244 still retains its documented package/tooling boundaries. These issues are not closed on the
strength of successful tests or a reduced warning count.

No test is retired or skipped to hide a regression. Dependency advisories remain separate from
source findings. Quiet compilation is not an acceptance claim: legitimate architectural findings
and disputed rule shapes remain visible for explicit review.

[rollout]: https://github.com/AntonC9018/ScheduleLib/issues/246
[readability]: https://github.com/AntonC9018/ScheduleLib/issues/265
[adoption]: https://github.com/AntonC9018/ScheduleLib/issues/244
[desktop]: https://github.com/AntonC9018/ScheduleLib/issues/271
[ui]: https://github.com/AntonC9018/ScheduleLib/issues/272

[reader-setup]:
  ../../src/MainCli/Tests/RegistryHistoryTests.cs#L278

[parser-assertions]:
  ../../src/ScheduleLib/Tests/Parser/LessonParserTests.cs#L904

[pdf-arithmetic]:
  ../../src/ScheduleLib/ScheduleLib.Generator.Pdf/Generation/GroupColumn/GroupColumnScheduleTableDocumentGenerator.cs#L233

[buffer-writes]:
  ../../src/ScheduleLib/ScheduleLib.Helper/Helper/RentedBuffer.cs#L60

[token-guard]:
  ../../src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs#L2028
