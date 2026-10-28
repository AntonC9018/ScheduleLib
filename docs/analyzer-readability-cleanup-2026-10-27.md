# Reviewed analyzer and ScheduleLib cleanup

The approved source snapshot is ScheduleLib `b1c9aed853774e41048fd75df971ec0a6ffd7309`.
It implements the [review decisions](analyzer-manual-examples-2026-10-25.md):
94 tuple-data sites now use meaningful record structs, Google token operations
share their client/account/scope context, and credential resolution has its own
request. StringSearch uses explicit inputs and returns remaining memory;
ShouldSkipMerge explicitly receives its `in LessonGroups` input. The parser's
modifier record retains mutable fields because its lexer is advanced by ref.

The old CR0200 mixed-condition lowering produced mutable Boolean trees;
CR0303 then extracted those trees while retaining nested branches. The updated
fixes produce early returns, preserving lazy evaluation and result values.
A lone return ternary now qualifies for CR0001; CR0205 reports redundant else
after return/throw. Built-in negation and equality simplify safely. Reviewed
source also folds redundant negated patterns. All unnecessary `global::`
qualifiers were removed through semantic simplification; zero remain in the
538-file source selection.

Single-member object initializers expand. Chains of at most two method calls
may remain on one line when the complete line is at most 90 characters.
Wrapped arguments begin below the enclosing opening parenthesis. Reviewed
assertions have explicit meaningful messages. The separate safe-by-default
public API check migration is tracked in [#249](https://github.com/AntonC9018/ScheduleLib/issues/249).

## Candidate and validation

The candidate is CodingRules `f2902d4f671b4a30b2c8bd84f6b6212b86f3d3bd`.
Its package SHA-256 is
`79972b6df1af48368f3e6e9cff36c01e5dbb858a82c8ba5d59b654a78be7edd6`.
All **1,470 analyzer/code-fix/package tests** and **11 consumer gates** pass.
Regressions cover flat conjunctions, all 16 mixed-condition input combinations
with their lazy call traces, safe negation, singleton initializers and the
90-character chain boundary. A runtime regression caught an overloaded-negation
rewrite changing False to True; the corrected rewrite retains that operator.

AWS adoption verified the candidate's 26 analyzer/fix/configuration/guidance
assets throughout the 24-project restored graph. Both Release rebuilds pass
with zero errors. Final formatting checks report zero changes across 502
solution files and 36 CLI test files.

The full solution run has 715 cases: 697 passed, seven skipped and the same
11 baseline failures. The CLI run has 770 cases: 767 passed and the same three
baseline failures. Failure names match the recorded baseline exactly. With the
known failures tracked by [#248](https://github.com/AntonC9018/ScheduleLib/issues/248)
excluded, the solution has 697 passed/seven skipped and the CLI has 767 passed;
both filtered runs have zero failures.

The tested archive was retrieved, checked against the original local input
hashes to avoid overwriting concurrent edits, and all 538 source files were
byte-compared after copying. The source archive SHA-256 is
`c7ffddda66afcc85a78eaf6083a550e8ceb7ee47103834af2f5c36b490717be2`.
Detailed logs, TRX results, timings and the inventory are retained in CodingRules
under `artifacts/schedulelib-readability-f2902d4/`.

The local candidate feed and exported guidance are configured for this archive.
Local restore remains unverified because of the previously recorded Nix CoreCLR
runtime mismatch; the successful restore/build/test evidence is from AWS.
The package is unpublished and all new repository commits remain local.

## Remaining findings

There are **142 unique source findings**, down from 180 in the previous source
snapshot. The 55 dependency-advisory rows are unchanged. No diagnostic family
was broadly disabled. The approved tuple, owned Google contract and repeated
calendar timestamp findings are clear, as are CR0200–CR0203, CR0205, CR0500 and
CR1600. New record constructions expose computation-step findings, so removing
94 tuple findings does not reduce the total by 94.

| Rule | Findings | Remaining work |
| --- | ---: | --- |
| CR0001 | 5 | Five return decisions without an applied automatic fix. |
| CR0006 | 2 | Two nested returns in TeacherBuilder. |
| CR0300 | 90 | Argument computations, including new record construction. |
| CR0301 | 5 | Conditional values needing named variables. |
| CR0302 | 1 | Computation-step extraction. |
| CR0303 | 19 | Decision functions requiring manual capture/binding review. |
| CR0400 | 3 | Projection staging. |
| CR0401 | 1 | Local stage implementation. |
| CR0801 | 1 | Remaining CliHost JsonSerializerOptions initializer. |
| CR0805 | 7 | Seven chain sites inside interpolated strings. |
| CR1400 | 8 | Deferred inheritance design. |

These remaining findings are visible in builds. Their correction is separate
from the reviewed migration; no inheritance redesign or controlled-check
implementation was included. The tuple signature changes intentionally require
external tuple-literal callers to use the corresponding named records.

## CodeRabbit follow-up and refreshed clusters

CodeRabbit reviewed the latest CodingRules change range in
[CodingRules #35](https://github.com/AntonC9018/CodingRules/pull/35).
All four findings were fixed in `2abd37a`: classify CR0205 as a new rule,
follow receiver wrappers when checking the short-chain allowance, and reconcile
the earlier initializer and CR0001 spec text with their approved refinements.
The five wrapper regressions fail before the fix and pass afterward. All 18
focused chain tests, all 1,475 solution tests and all 11 consumer gates pass.

The reviewed candidate archive SHA-256 is
`b87e5d8e8d5c87a7ade0dbc3583c15a27ce354c48e2cfb1c64f457bddb81b50a`.
Both ScheduleLib Release rebuilds pass with this package. Their refreshed
inventory still contains 142 source findings and 55 advisory rows; all 538
source hashes match the earlier tested snapshot. The full ScheduleLib test
suites were not repeated because no consumer source changed. The unchanged
baseline evidence above remains applicable.

The local feed is configured for the reviewed candidate; local restore remains
unverified. [Remaining clusters](analyzer-remaining-clusters-2026-10-27.md)
separate fixture arguments, invariant assertions, production computations,
Boolean scratch decisions and other outstanding review work. They are proposals,
not additional source migrations. Analyzer regressions belong in CodingRules;
temporary debugging probes do not belong in ScheduleLib's permanent suite.

## Reviewed consumer cleanup — 2026-10-28

Consumer source `b4270ba` cleans the unambiguous remaining conditional-value,
Boolean scratch-decision, return, pipeline and layout clusters. The refreshed
[remaining-case report](analyzer-remaining-clusters-2026-10-27.md) now supplies
exact line links and separates the readable constructor/assertion policy cases
from applied fixes. New analyzer/fixer complaints are recorded in
[CodingRules #36](https://github.com/AntonC9018/CodingRules/issues/36).

Within the reviewed scope, findings fall from 134 to 62: 43 test constructions,
9 production constructions and 10 readable assertions, all CR0300. This also
corrects the previous assertion/production bucket counts without changing their
combined original total. No suppressions or blanket constructor exemptions were
introduced. The analyzer binary and candidate package remain `2abd37a`.

Both final AWS Release rebuilds pass. Focused checks pass, and full test failures
match the established baseline exactly: solution 715 cases / 11 failures /
7 skipped; CLI 770 cases / 3 failures. No ScheduleLib test cases were added.
All 538 source hashes match the tested archive, whose SHA-256 is:

```text
4414330526cd4975fe78db5e316705e076de852455ec0afdaa5a79167156666b
```

Validation logs, TRX reports, inventory and source hashes are retained in the
ignored CodingRules `artifacts/schedulelib-clusters-20261028/` directory.
