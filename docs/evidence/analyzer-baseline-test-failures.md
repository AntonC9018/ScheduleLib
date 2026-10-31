# Baseline test failures

Tracked in [#248](https://github.com/AntonC9/ScheduleLib/issues/248); the registry privacy
regression is also [#228](https://github.com/AntonC9/ScheduleLib/issues/228). These failures
predate the analyzer cleanup and were reproduced on AWS before any fix; they were not repaired,
suppressed or weakened by it. Unfiltered suites stay red.

## Failing cases

Solution (11 failures) and CLI tests (3 failures); the last case below is in the CLI target.

- `Curriculum.Tests.ThesisNameTests`: `CurriculumParsedCorrectly`, `FileNameParsedCorrectly`.
  The Windows-style fixture path yields a null curriculum key on Linux.
- `FmiWebsiteInterop.Tests.FmiWebsiteInteropTests.CountTest`. The snapshot expects `DU2501`;
  current code emits `DU-IA2501`.
- `Desktop.Tests.EventTests`: `NodeChangedTest`, `LayerChangedTest`, `ReloadWorks`. The unkeyed
  Registry registration cannot resolve its required option-key string constructor parameter;
  its explicit factory/default key belongs in a separate DI fix.
- `ScheduleLib.OnlineRegistry.Tests.TokenTests.TokenGeneratedOnLogIn`.
- `ScheduleSourceSanityTests.Schedule2026Semester1PassesSanityChecks`.
- `PdfImportTests.SourcePdfsMatchReviewedCellContentAndMembership`, years 1–3.
- `CurrentSourceMergeTests`: both fresh/cached merge comparison cases.
- `RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext`
  (`RegistryHistoryTests.cs:428`): a synthetic canary appears in error output. This is a real
  privacy regression.

The first six solution failures observed at adoption time were reproduced from an isolated
`git archive 6d271e3` of the starting sources; the Curriculum, website and Desktop files and their
implementations were unchanged from the starting commit, and no fixture or snapshot was accepted
to obtain passing results.

## Counts by rollout

- **Ticket 18 adoption:** solution 709 cases, 696 passed / 6 failed / 7 skipped; CLI 767 cases,
  766 passed / 1 failed. The other baseline failures were reproduced from the starting snapshot.
- **2026-10-24 first rollout:** solution 709 cases / 11 failures; CLI 767 cases / 3 failures.
- **2026-10-25 review:** solution 712 cases (694 passed / 7 skipped / 11 failed); CLI 769 cases
  (766 passed / 3 failed).
- **2026-10-26:** solution 715 cases / 11 failures; CLI 770 cases / 3 failures.
- **2026-10-27:** solution 715 cases (697 passed / 7 skipped / 11 failed); CLI 770 cases
  (767 passed / 3 failed).

Failing names matched the baseline exactly in each run; the additional passing cases come from
follow-up coverage. Runs excluding exactly the baseline names (command-local filters, not removed
tests or repository configuration) pass with zero failures, for example 694 passed / 7 skipped
plus 766 passed on 2026-10-25.

The ticket 18 filter excluded the seven failing names then known (the three Desktop, two
Curriculum, one website and one privacy case):

```sh
dotnet test All.sln -c Release --no-build --no-restore -m:1 --filter \
  'FullyQualifiedName!=<name 1>&FullyQualifiedName!=<name 2>'
```

Use `DiffEngine_Disabled=true` for test runs and run heavy jobs sequentially.
