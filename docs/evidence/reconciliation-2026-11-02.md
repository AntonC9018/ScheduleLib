# Reconciliation and first readability cleanup batch (2026-11-02)

Tracks [epic #269][epic], [#248][tests], [#246][rollout] and [#265][readability].

## Integrated work

- Merge `a2358b4c` integrates the ten baseline-test-audit commits into
  `feat/registry-history-analysis`, alongside the typed message/action changes from #267.
  The four overlapping CLI test files merge without conflicts.
- The older StudyWeeks, temporary-directory and catch-all-handler branches have equivalent
  patches already integrated. They require no duplicate changes.
- Keep the reviewed documentation split and later policy trimming from `a7c759fa` and
  `61cccd5f`. The older documentation draft would restore removed generic rules, obsolete
  runner notes and broken links; it does not supersede these versions.

## Analyzer readiness and adoption

The clean CodingRules source is `8e727c91a2795665e09441b792e770ae892af756`, on
`feat/readability-fixer-policy`. Its local package is `0.1.0-local.20261102`, with SHA-256
`0c1701f04b372d26942391f1938225720278272015fb5cb40650678aa11e3a3f`.

Fresh serial validation passes 287 analyzer tests, 1,369 fixer tests, eight package tests and
all twelve packaged consumer gates. Each gate uses the same existing local archive.
No analyzer source change, upstream merge or package publication is part of this reconciliation.

Consumer adoption in `595600ed` pins this package, verifies its exact archive and assets through
23 CLI dependency projects, and refreshes exported guidance. `d432ddca` includes CR0404 and
CR0701 in the established warning-only legacy backlog; findings remain visible. CR1700 remains
an error under the consumer's strict warning policy, requiring generated `TODO_` names to be
renamed. The first broad build exposed existing CR0404 findings before that policy update.

## First #246/#265 cleanup batch

Commit `b74c7d61` removes six generated Boolean local functions and their scratch results:

- `SequenceComparableImmutableArray.Equals`: directly check both-default and either-default.
- `SizedItemArray.Replace`: directly check the three integer offset/remainder boundaries.
- `IgnoreDiacriticsAndCaseComparer.Compare`: directly check the both-null case.

The same operands are evaluated in the same order and each branch retains its result.
The batch removes 97 net lines across three files. It adds no analyzer-only consumer fixtures.
The focused helper suite passes 48 cases, with one existing skip.

## Combined validation

Run sequentially in Release with `DiffEngine_Disabled=true`, `SCHEDULELIB_LIVE_TESTS=0` and the
command-scoped `WarningsNotAsErrors='NU1902;NU1903'` advisory exception.

- Solution: 786 cases, 774 passed, four failed, eight skipped.
- Separate CLI suite: all 812 cases pass, with no skips.

The four solution failures are the unchanged `ThesisNameTests.CurriculumParsedCorrectly` and
the three Desktop `EventTests` cases (`LayerChangedTest`, `NodeChangedTest`, `ReloadWorks`).
The curriculum scope decision remains in #248; Desktop failures remain in [#271][desktop].
All 35 PDF tests pass. No tests are retired to obtain these results.

## Remaining work

#246 and #265 remain open for the other generated-code clusters and diagnostic triage. This
batch establishes a validated candidate and starts the consumer cleanup; it does not establish
quiet builds or completion of every upstream analyzer issue. The public-API checks and command
test-seam designs remain separate in #249 and #247.

The surrounding `SequenceComparableImmutableArray` equality/hash implementation also deserves
separate semantic review: typed equality compares elements, while hashing uses the backing array.
That contract is outside this guard-only batch and should receive runtime regression coverage
before it changes.

[epic]: https://github.com/AntonC9018/ScheduleLib/issues/269
[tests]: https://github.com/AntonC9018/ScheduleLib/issues/248
[rollout]: https://github.com/AntonC9018/ScheduleLib/issues/246
[readability]: https://github.com/AntonC9018/ScheduleLib/issues/265
[desktop]: https://github.com/AntonC9018/ScheduleLib/issues/271
