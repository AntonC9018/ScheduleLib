# Follow-up cleanup review (2026-11-03)

Continues [#246][rollout] and [#265][readability] under [#269][epic].
The preceding [inventory][inventory] is the starting point, rather than an acceptance waiver.

## Merged analyzer and local package

CodingRules [PR #40][analyzer-pr] merged at `437d34d62985cb9c4737d3a8f91793feab777ec5`
after its complete CI passed. The merged tree is identical to validated candidate `8e727c9`.
The older #20 was included in the merge; #34 and #35 were closed as superseded after proving
that their heads are ancestors of merged main. Open analyzer design issues remain open.

ScheduleLib pins freshly packed `0.1.0-local.20261103.1`, using the existing local-feed mapping.
The package repository metadata names the merged commit. Its SHA-256 is
`26efbc032c36bbc9421b5785f49b3c19daab8e69cb45f3377e6950d24844335b`.
Exported guidance matches the package. The obsolete ignored candidate-only NuGet configuration
was backed up so normal restores use the tracked local-feed configuration.

## Source review

The pattern scan covers all 557 tracked C# files under `src`, including the separate CLI,
features, helpers, generators, tests and Desktop code. It searches equality/hash implementations,
catch boundaries, incomplete operations, generated names, aliases and repeated infrastructure.
Concrete candidates receive source and caller review. This does not prove every defect is found.

### Corrected value contracts

- `SequenceComparableImmutableArray<T>` compared elements but hashed backing-array identity.
  Its operator called object equality, whose override called the operator recursively.
  Operators and boxed equality now call typed equality; hashing uses sequence elements.
  Default and initialized-empty arrays remain distinct. Regression tests cover separate equal
  arrays, ordering, boxed/operator equality, default/empty and hash-set lookup.
- `LessonGroups` equality stops at the first invalid group, but hashing included unused storage.
  Its public indexer permits different trailing storage on equal values. Hashing now enumerates
  the same logical groups as equality. The regression fails before the change and passes after it.

### Readability and analyzer corrections

- Remove unchanged path aliases and replace numbered path stages with names describing profile,
  teacher, client, account, lock and summary roles. Keep separate credential hashing policies and
  exact file-name casing, delimiters, filesystem behavior and ordering.
- Simplify conversion staging-directory names and redundant history-output ID copies.
- Replace three ordinary test-string concatenations with equivalent interpolation (CR1100).
  No custom interpolated-string handler or deferred message contract is involved.

## Validation

- Fresh CodingRules source tests: 287 analyzer and 1,369 non-package fixer tests pass.
  The full upstream CI, including package tests and consumer gates, passes on merged PR #40.
- Both normal restores succeed through the tracked NuGet configuration. Exact package SHA-512,
  analyzer/fixer assets and guidance are verified in all 38 restored project asset files.
- Both clean Release build slices pass with zero errors after the final local-package pin.
  Existing CR findings remain warnings; CR1700 remains strict. Dependency-advisory exceptions
  are command-scoped (`NU1902;NU1903`), with no severity changes committed.
- Full runtime validation after the behavior changes: solution 797 cases, 788 pass, eight skip,
  one known `ThesisNameTests.CurriculumParsedCorrectly` failure (#248). CLI: 813/813 pass.
- After the final explicit-return/argument/layout corrections, affected suites pass again:
  helpers 51 pass/one skip; parser 230 pass/three skip; registry 385 pass/three skip.
  The later package repack changes provenance/version metadata; final builds verify its adoption.
- The exact merged local archive passes the packaged condition gate, including runtime behavior,
  severity transport and a stable second formatting pass.
- The focused hash regressions fail against the original implementations and pass after fixing.
  `git diff --check` passes.

The [final exact-site inventory][final-inventory] contains **194** visible findings:
137 CR0300, 25 CR0302, 22 CR0201–CR0203, one CR0401, six CR1400 and three CR1600.
It covers both clean build slices, deduplicated by source path/location, rule, severity and message.
The starting historical report predates additional registry changes; it is not a current baseline.
Six new assertion-expression findings remain in the assertion-computation cluster, rather than
being expanded into generic generated wrappers. CR1100, argument naming and initializer-layout
findings from this scan have been corrected.

## Existing clusters requiring maintainer review

### Assertions, fixture construction and compact arithmetic (#246 / CodingRules #36)

The prior inventory's 131 CR0300 sites mostly concern observed values in assertions and fixture
construction. For example, [LessonParserTests.cs:904][source-0] keeps `TryFromNamePrefix(..., out
...)`
inside `Assert.True`. Decide whether these shapes need domain locals or analyzer acceptance.
Do not apply a blanket assertion exemption or restore generated calculation wrappers.

The maintainer chose named insertion, deletion and replacement costs for the
[Levenshtein recurrence][source-1], plus a named minimum insertion/deletion cost before the final
`Math.Min`. This resolves that concrete operation-staging case without changing the recurrence.

The maintainer chose separate buffer writes and increments for both
[indexed buffer writes][source-2]. `buffer.Array[i] = item; i++;` and
`arr[written] = item; written++;` now count successful stores. The changed increment timing on
failure is unobserved: neither failure path reads the local counter. The helper suite passes
51 tests with one existing skip.

The existing CR0302 analyzer already reports both original shapes. Four dedicated CodingRules
regressions confirm findings for local-array and field-array index increments and acceptance
of the separate statements. No analyzer binary or consumer package version change is needed.
This resolves those concrete mutation cases; other arithmetic shapes remain for review.

The maintainer subsequently moved arithmetic and bitwise formula decomposition to semantic
agent review. CR03 no longer counts those operators; consumed mutations and logical-condition
checks remain enabled. The shipped guidance requires meaningful calculation abstractions and
useful intermediate concepts. [Package adoption and validation][calculation-policy] record this
policy change; the consumer formulas still need their individual semantic review.

The maintainer subsequently chose named boxed values for the sequence-equality assertions.
Those tests now assign `object boxedSecond` / `object boxedReversed` before calling `Equals`,
including the matching default-value case. All three focused equality tests pass. This resolves
that concrete shape; the broader assertion and arithmetic cases still require review. The CSV
above remains the historical inventory before this decision.

### Short conditions and pipeline classification (#246 / CodingRules #36)

The maintainer chose separate guards for [the single-letter token checks][source-3].
`TryConsumeSingleLetter` now rejects a non-word token, a length other than one, then a
non-letter, before moving the lexer. Evaluation order and safe indexing are preserved.
The maintainer chose separate guards for [RegistryHistory.cs:280][source-4]: first require
one form, then require one child. The code now makes those independent requirements explicit,
retaining short-circuit order and safe indexing. This resolves that concrete CR0202 case.
These two concrete condition cases are resolved; other condition sites still require review.
The maintainer rejected CR0401's automatic stage-extraction policy in case 11.
CodingRules removes the diagnostic/fixer and its computed-local/nested-decision threshold.
The [routed-topic block projection][source-5] stays inline; no consumer suppression or
forced local-function extraction is needed. CR0400 composed expression projections and
CR0402 collection selectors remain enabled. The corresponding package adoption is recorded
in [the CR0401 removal evidence][cr0401-removal].

### Fuzzy identity used as equality (#246, new evidence in the equality cluster)

[Parsing/Name.cs:26][source-6] defines `Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer`.
Its missing-patronymic wildcard deliberately matches either specific patronymic, while two
conflicting specific patronymics differ. That relation is not transitive. Hashing only first
and last names is necessary for its existing pairwise equality; adding patronymic to the hash
would introduce a defect.

The maintainer explicitly deferred this question to standalone [#273][name-equality-followup],
**outside epic #269**. Keep the comparer and its callers unchanged. This issue is neither an
epic child nor a blocker, and resolving it is not required for cleanup completion.
Examples include [PublicApi/Attendance.cs:630][source-7] and
[FmiWebsiteInterop/Theses.cs:105][source-8] (`GroupBy`). Any future policy change needs separate
authorization.

### Failure boundaries (#246, distinct from the completed CLI catch-all fixes)

The maintainer chose a domain-only failure boundary for [group-name parsing][source-9].
`TryParse` now catches only `InvalidGroupNameException`. The former short-name `ArgumentException`
is now `NameTooShortException` within that hierarchy, exposing minimum and actual lengths.
Existing malformed label/year/number/language failures already use the hierarchy. Unexpected
failures propagate unchanged; no catch-all or wrapper remains.

Regression tests first reproduced short names escaping the domain hierarchy and an unrelated
label-comparer `ArgumentException` being swallowed. The tests cover malformed input returning
null, valid group parsing, typed short-name context and propagation of that same failure instance.
Validation: full parser suite 243 passed with three existing skips; registry group-parser suite
48 passed. `git diff --check` passes.

The conversion cleanup catches and cache-deserialization catches have different meanings:
`OwnedConversionDirectory.Cleanup` explicitly preserves the primary conversion failure;
`ScheduleLoader` falls back from invalid cache data to source import. Do not mechanically add
logging or promote these failures without deciding noise and recovery policy.

### Shared converter contract and typed exception context (#246)

The maintainer confirmed that ScheduleLib is unpublished and has no external consumers;
backward compatibility is not required. Project `AGENTS.md` now explicitly rejects unrequested
compatibility wrappers, aliases and migration shims.

The duplicate bases formerly in [CliStates.cs][source-10] and [RegistryGradePlan.cs][source-11]
are consolidated directly into `ScheduleLib.Helper.JsonConverters.WireNameJsonConverter<T>`.
All seven derived converters use that shared base without compatibility wrappers. Output-only
serialization behavior is retained. Existing CLI state serialization tests pass (20 cases), as
do the registry grade-notice and sync-diagnostic tests (62 cases), including dictionary keys.

The maintainer subsequently settled the exception policy: always use meaningful
application-specific typed exceptions and expose typed read-only context, including infrastructure
and invariant failures. The old generic-invariant carve-out in coding standards is removed.
[Issue #120][typed-exceptions] is reopened for the remaining generic throws; its historical
infrastructure exemption is superseded.

The shared converter now throws `OutputOnlyJsonReadException` with a `TargetType` property.
It derives directly from `Exception`, so `JsonSerializer` exposes the typed failure itself rather
than wrapping a `NotSupportedException` to add JSON path text. Message content remains the same.
Integration tests exercise real CLI and registry enum deserialization and inspect typed context.
The remaining Drive invariant belongs to the recorded typed-exception work,
not a new policy question.

### Architecture and incomplete implementations

Command inheritance stays in #247, public controlled checks in #249, curriculum behavior in #248,
and the property-editor factory in #272. Enumerator `Reset` implementations that throw
`NotImplementedException` deserve a contract decision (`NotSupportedException` versus support),
not automatic feature implementation. Ordinary TODO comments are not CR1700 identifiers.
The older routing/action-kind examples have already been addressed; do not reopen stale evidence.

## 2026-11-08 review batch

The four analyzer errors that stopped a clean build are cleared in `69e47c44`
(`IcsCalendarWriter` and `OneForEachEnumMember`). A GPT-6.1-Sol review of
[PR #226][pr-226] then found a pre-existing disposal-ordering defect in the rented
converter; `9fdca2b4` disposes parsed elements before returning the buffer and adds
a regression test. See [rented-buffer disposal](rented-buffer-disposal-2026-11-08.md).

The arithmetic and bitwise policy is merged upstream as CodingRules PRs #41, #43
and #44; ScheduleLib keeps the package packed from that work. A full rebuild still
reports 146 warning lines, which are 73 diagnostics printed twice:

| Rule | Lines |
|---|---|
| CR0300 | 86 |
| CR0203 | 14 |
| CR0201 | 14 |
| CR1400 | 12 |
| CR0302 | 6 |
| CR0202 | 6 |
| CR0805 | 4 |
| CR1600 | 2 |
| CR0500 | 2 |

Only the two CR0805 findings on the PDF page-reading chain affect files changed in
this batch. The remaining clusters, including the 86 CR0300 sites and the unflagged
identity aliases and condition trees, still need per-cluster review and are not part
of this batch.

[pr-226]: https://github.com/AntonC9018/ScheduleLib/pull/226

[rollout]: https://github.com/AntonC9018/ScheduleLib/issues/246
[readability]: https://github.com/AntonC9018/ScheduleLib/issues/265
[epic]: https://github.com/AntonC9018/ScheduleLib/issues/269
[inventory]: readability-cleanup-2026-11-03.md

[source-0]: ../../src/ScheduleLib/Tests/Parser/LessonParserTests.cs#L904

[source-1]:
../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryCourseNameLookup.cs#L164

[source-2]: ../../src/ScheduleLib/ScheduleLib.Helper/Helper/RentedBuffer.cs#L60

[source-3]: ../../src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs#L2028

[source-4]: ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryHistory.cs#L280

[source-5]:
../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/AddLessonsToOnlineRegistryHandler.cs#L474

[source-6]: ../../src/ScheduleLib/ScheduleLib.Core/Parsing/Name.cs#L26

[source-7]: ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/Attendance.cs#L630

[source-8]: ../../src/Features/FmiWebsiteInterop/Theses.cs#L105

[source-9]: ../../src/ScheduleLib/ScheduleLib.Core/Parsing/GroupParser.cs#L57

[source-10]: ../../src/MainCli/CliStates.cs#L89

[source-11]: ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryGradePlan.cs#L55

[analyzer-pr]: https://github.com/AntonC9018/CodingRules/pull/40

[final-inventory]: analyzer-findings-follow-up-2026-11-03.csv

[name-equality-followup]: https://github.com/AntonC9018/ScheduleLib/issues/273

[typed-exceptions]: https://github.com/AntonC9018/ScheduleLib/issues/120

[cr0401-removal]: cr0401-removal-2026-11-03.md

[calculation-policy]: calculation-policy-2026-11-06.md
