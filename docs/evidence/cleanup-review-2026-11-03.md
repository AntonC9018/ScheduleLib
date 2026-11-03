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

[RentedBuffer.cs:60][source-2], `buffer.Array[i++] = item`, remains for review. Extracting stages
must justify the new names and preserve indexing/increment/exception timing. It belongs to the
existing arithmetic cluster.

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
The [block projection][source-5] also still requests a named stage (CR0401).
Keep these visible pending reconciliation.

### Fuzzy identity used as equality (#246, new evidence in the equality cluster)

[Parsing/Name.cs:26][source-6] defines `Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer`.
Its missing-patronymic wildcard deliberately matches either specific patronymic, while two
conflicting specific patronymics differ. That relation is not transitive. Hashing only first
and last names is necessary for its existing pairwise equality; adding patronymic to the hash
would introduce a defect.

The unresolved question is whether wildcard matching belongs in dictionary/set equality at all.
Examples include [PublicApi/Attendance.cs:630][source-7] and
[FmiWebsiteInterop/Theses.cs:105][source-8] (`GroupBy`).
Choose an explicit ambiguous-match policy before replacing these collections or this comparer.

### Failure boundaries (#246, distinct from the completed CLI catch-all fixes)

[GroupParser.cs:57][source-9] catches every `Exception` and returns null from `TryParse`,
potentially
hiding implementation failures as invalid input. Decide the intended parse-failure taxonomy
before narrowing the catch; current parse helpers use multiple exception types.

The conversion cleanup catches and cache-deserialization catches have different meanings:
`OwnedConversionDirectory.Cleanup` explicitly preserves the primary conversion failure;
`ScheduleLoader` falls back from invalid cache data to source import. Do not mechanically add
logging or promote these failures without deciding noise and recovery policy.

### Shared converter contract and typed exception context (#246)

[CliStates.cs][source-10] and [RegistryGradePlan.cs][source-11] each declare an identical public
`WireNameJsonConverter<T>`. The serialization behavior appears shared, but consolidating it
moves a public base type. Decide whether to preserve compatibility wrappers or move it directly
to the helper JSON-converter namespace. Its output-only `Read` failure is also the existing
CR1600 policy question: retain framework `NotSupportedException` or introduce typed context.
The unreachable Drive action failure remains a separate invariant case.

### Architecture and incomplete implementations

Command inheritance stays in #247, public controlled checks in #249, curriculum behavior in #248,
and the property-editor factory in #272. Enumerator `Reset` implementations that throw
`NotImplementedException` deserve a contract decision (`NotSupportedException` versus support),
not automatic feature implementation. Ordinary TODO comments are not CR1700 identifiers.
The older routing/action-kind examples have already been addressed; do not reopen stale evidence.

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
