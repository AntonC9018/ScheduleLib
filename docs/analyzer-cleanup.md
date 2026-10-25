# Analyzer cleanup and warning review

Latest follow-up: [2026-10-25 rollout and grouped remaining cases](analyzer-review-2026-10-25.md). The dated results below describe earlier candidates.

Work item: [#246](https://github.com/AntonC9018/ScheduleLib/issues/246).
This review covers `All.sln` **and** the separately built
`src/MainCli/Tests/MainCli.Tests.csproj`. The latter is not part of the solution;
solution-only counts understate the CLI cleanup.

The [follow-up rollout result](#follow-up-rollout-result--2026-10-25) is the
latest validated state. Earlier sections retain the initial review and measurements.

## Current implementation status

The next reviewed source changes (`a12838a`, `d8c5d6c`, `332ebd6` and follow-ups)
are committed locally but **not yet built or tested**. AWS login expired before
worker creation; no replacement runner was launched. CodingRules source changes
`9bc6620` / `a0e3a29` are likewise pending focused/full tests and consumer gates.
The prior candidate remains the adopted archive. Counts and green checks below
refer to that earlier validated rollout, not to the new source revisions.

The next pass adds record-update acceptance/layout, lookup-cast acceptance,
comparer overload acceptance, trailing-cancellation grouping, readable generated
locals/types, and a manual branching-Boolean finding. ScheduleLib changes include
explicit accessor factories, the Google login builder, the Desktop-only CR1001
suppression, and the history writer/directory/course-comparer refactor. Existing
CSV, history-command, authentication and permission coverage must be rerun on AWS;
new tests cover comparer equality/hash consistency and missing builder inputs.

Remapping overload policy and the precise fluent-chain dot rule remain awaiting
maintainer clarification. Neither is broadened through an inferred exemption.
The six related command inheritance seams were audited in
[issue #247](https://github.com/AntonC9018/ScheduleLib/issues/247#issuecomment-6395472858).

## Measurement

`build/analyzer-inventory.py` deduplicates build diagnostics by source path,
location, severity, rule and message, removing the repeated MSBuild summary and
duplicate compilation of shared sources. Different findings on the same line
remain distinct. Package advisories without source locations are retained.

The 2026-10-24 AWS baseline has **7,712 unique findings** across both targets:
7,642 source warnings and 70 package-advisory rows. An earlier inventory missed
five advisory rows because restore and build emit different project suffixes;
the parser now handles both forms, with regression tests for deduplication.
The solution alone has 5,066 unique findings; its MSBuild summary reports 5,139
warnings and zero errors. These are different measurements.

The first packaged pass (named arguments, interpolation and layout) changes 289
files and reduces source warnings to **3,335**, with zero build errors in both
targets. All 3,694 CR0500 findings disappear. The package advisories are unchanged.
The corrected candidate comes from CodingRules `d8803f6`, which fixes the
unbound-generic CR0500 crash found during this rollout. All 1,370 CodingRules
analyzer, fixer and packaged-consumer tests pass on AWS.

## First rollout candidate and applied changes

The adopted candidate is CodingRules `a78e907161714e9e0ded51a84a5602b227bc9157`,
package `Anton.CodingRules.0.1.0.nupkg`, SHA-256
`b309e17db91f1c8bfd2fc043dd480d0532bc4d63b83d75ffb6b3a5b8b4f560ec`.
It includes both the unbound-generic named-argument fix and the null-forgiving
operation proof fix. All 1,372 analyzer/fixer/package tests and all nine separate
consumer gates pass on AWS against this stable candidate. The local adoption
script verifies package assets and exports the packaged agent guidance; the
candidate is verified across the 24-project CLI test dependency graph.

The second pass applies available return, condition, pipeline, span and
assignment fixes. Manual review seals 30 concrete classes, makes two base/test
fixtures abstract, names the constant curriculum glob, shares the calendar
start/end timestamp representation and suppresses the four externally required
collection overload implementations. Existing inheritance seams remain visible.
Bulk CR0300–CR0302 fixes are held for the readability reasons below.

## First rollout inventory — 2026-10-24

Clean Release rebuilds of both targets produce **zero errors** and **2,902
unique source warnings**, down from 7,642 (**4,740 removed; 62%**). The current
build logs also contain 55 project/package advisory rows; dependencies were not
updated by this cleanup. Advisory row counts depend on restore/build coverage
and should not be interpreted as vulnerability remediation.

| Remaining family | Findings |
| --- | ---: |
| CR0300–CR0302: named computation steps | 2,615 |
| CR0200–CR0203: compound conditions | 118 |
| CR0900: named tuple data | 95 |
| CR1001: overload configuration builders | 56 |
| CR1400: inheritance review | 10 |
| CR1000: contract argument groups | 7 |
| CR1300: repeated formatting review | 1 |

All return, pipeline, span, named-argument, interpolation, selected layout and
unchanged-assignment findings are cleared. Remaining condition findings were
not removed by the packaged fixes; retain their safety/manual review boundary.
The bulk computation fixes are deliberately held for the usability review below.
This is still a substantial warning inventory, not a claim of quiet builds.

The final TRX comparison matches the baseline exactly: solution 709 cases / 11
failures, CLI tests 767 cases / 3 failures; **no new failures**. All 1,476 cases
are accounted for, with shared projects potentially run in both targets. This
cleanup does not make the entire ScheduleLib suite green.

Implementation commits: `bac5d6f` (first packaged pass), `96f177c` (second pass
and reviewed manual fixes). Validation logs, TRX files, before/after inventories
and the candidate package are retained in the CodingRules checkout under
`artifacts/schedulelib-cleanup-a78e907/`; they are intentionally ignored by Git.
The final C# patch was byte-compared with the AWS-tested patch before committing.

## Original review candidates

This section records the original review. The follow-up decisions below settle
several boundaries; architectural findings still require individual review.

### CR1001: externally required overloads

`ManualObservableList<T>` implements both `IList<T>` and `IList`.
Its `Insert(int, T)` / `Insert(int, object?)` and
`CopyTo(T[], int)` / `CopyTo(Array, int)` pairs satisfy those interfaces.
Replacing these signatures with a configuration builder cannot satisfy the
external contract. The four implementations were initially narrowly suppressed. They now use
explicit non-generic interface implementation, following the maintainer decision. These are strong false-positive candidates; analyze the
resolved interface implementation before requiring a builder.

### CR1001: configuration already being built

`LessonTopics/Builder.cs` reports the two `FallbackProvider` overloads inside
the `NodeDataBuilder<LessonTopicsConfig>` extension. One adapts an
`ILessonNameProvider` to an `ILessonNameProviderFactory`; both configure the
same lesson-type/provider choice. Consider whether this needs an inner builder
or is already an appropriate builder operation.

`MarkerConfigHelper.CreateMarkerScope(IServiceProvider, Action<TeacherLayerConfig>)`
and its config-value overload similarly have one configuration input plus the
service-provider receiver. Whether a receiver should count as configuration
needs an explicit decision; creating a builder merely to remove this warning
would obscure the scope lifetime.

### CR0300: small conversions and factory values

`WaitForExitBounded(process, TimeSpan.FromSeconds(2))` and its three-second
counterpart are straightforward, strongly typed duration values. This differs
from a deeply nested lookup or calculation. The current accepted simple-factory
examples cover initializer values; they do not establish a blanket exemption
for factories in call arguments.

Likewise review casts such as `Process.GetProcessById((int)pid)` separately
from chains that query, convert, index and construct a result. A helper whose
only purpose is a numeric cast may add more indirection than meaning.

### CR0300–CR0302: automatic rewrite readability

The bulk computation rewrite is held after a saved-source probe of
`CliSettings.Removals.cs`. Preferring expansion executes 21 packaged actions,
but creates redundant `operationValue` copies and substantially expands short
conditions. Default extraction instead introduces many `CalculateValue` helpers.
This is a fixer usability problem, separate from the crash fixed in CodingRules
`a78e907`. Keep these warnings visible while naming/capture quality is improved;
see [CodingRules #26](https://github.com/AntonC9018/CodingRules/issues/26).

### CR0300: exception rendering versus structured context

Examples include `throw new NotSupportedException($"Unknown preliminary field {field}")`
in `CurriculumNameParser`. Naming an eagerly formatted string or adding an
extraction helper does not implement the separate structured-exception design.
Short diagnostic messages remain an open policy boundary. Do not treat an
automatic extraction as proof that the desired exception API is finished.

### CR0500: comparisons versus distinct argument roles

Custom comparisons such as `Equals1(name, "licenta")` and
`IgnoreDiacriticsAndCaseComparer.Instance.Equals(name, label)` receive the
same mechanical repeated-type treatment as genuinely ambiguous from/to or
path/content parameters. Review whether symmetric equality should join the
existing exact framework exceptions. This is a possible usability refinement,
not evidence that role-sensitive calls should remain positional.

### CR1400: intentional base classes

`WrongFormatException` has `RoomAlreadySpecifiedException` as an existing
subclass and is also instantiated directly by its own factories. Blindly
sealing it breaks the hierarchy; blindly making it abstract breaks those
factories. It needs a deliberate exception-model decision. Current CR1400
policy explicitly requests that review even when subclasses exist, so this
is a manual finding rather than a proven analyzer defect.

`CliOutputStreamingException` is instantiated directly and has calendar/drive
subclasses; it also requires an intentional exception to sealing.

`CalendarCommands` also has a tested virtual seam. Keep it under architectural
review rather than deleting or sealing the seam to satisfy the warning.

## Baseline test limitations

All builds and tests run on the temporary Terraform-managed AWS runner,
sequentially. No local application data or credentials are uploaded.
Before fixes, AWS reproduces these 14 failing cases:

- `ThesisNameTests`: `CurriculumParsedCorrectly`, `FileNameParsedCorrectly`.
- `FmiWebsiteInteropTests.CountTest`.
- `Desktop.Tests.EventTests`: `NodeChangedTest`, `LayerChangedTest`, `ReloadWorks`.
- `ScheduleLib.OnlineRegistry.Tests.TokenTests.TokenGeneratedOnLogIn`.
- `ScheduleSourceSanityTests.Schedule2026Semester1PassesSanityChecks`.
- `PdfImportTests.SourcePdfsMatchReviewedCellContentAndMembership`, years 1–3.
- `CurrentSourceMergeTests`: both fresh/cached merge comparison cases.
- `RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext`.

The TRX files contain 1,476 reported cases across both targets (with shared
project tests potentially executed in both). Fourteen fail. The last case is a real existing privacy regression tracked by #228; a warning
cleanup must not claim to resolve it. Data/live-service failures on a runner
without private local configuration are recorded separately from regressions
introduced by code fixes.

## First runner cleanup

Terraform destroy completed successfully (three managed resources). AWS queries
confirm the instance is terminated and its root volume, temporary SSH key and
security group are absent; Terraform state is empty. Logs and package artifacts
were retrieved before destruction. The runner used one `m7i-flex.large`, with
no NAT gateway or reserved commitment. Estimated compute cost is under $2,
well within the authorized $100 budget; this is an estimate, not a billing total.

## Follow-up decisions — 2026-10-24

- `ManualObservableList<T>` exposes its generic operations publicly; object-based
  IList and non-generic-only ICollection members are explicit. CR1500 generalizes
  that policy without hiding shared generic members such as Count/Clear.
- CR1001 recognizes builder declaring types and extension receivers. Default
  suffixes are Builder, Descriptor and Configurator; CodingRulesBuilderSuffixes
  replaces the list, and ConfigurationBuilder marks types directly. Extension
  receivers do not count as configuration inputs; one effective parameter is
  allowed. Explicit interface implementations are excluded.
- `WaitForExitBounded(..., TimeSpan.FromSeconds(2))` remains a CR0300 finding.
  A simple cast is accepted; CR0804 separately requires `(int) pid` spacing.
  CR0803 requires braces even for a single-line condition and statement.
- Computation names prefer the bound destination parameter, then operation or
  type, with collision handling. Redundant same-type generated copies are
  removed. The downstream computation fix pass is retried with these changes.
- Exception messages use CR1600 instead of CR03 string extraction. The opt-in
  action generates an internal sealed framework-exception subtype with typed
  read-only context properties and preserves eager message rendering. Side
  effects, mutable formatting and custom exception hierarchies remain manual;
  exact runtime type changes, so there is no Fix All.
- Boolean Equals/EqualsN methods allow positional arguments. Exact external
  methods can be registered by assembly attribute or the MSBuild property
  CodingRulesAllowPositionalArguments; the existing method marker remains.
- WrongFormatException and CliOutputStreamingException are abstract. Their
  direct constructions use sealed GeneralWrongFormatException and
  GeneralCliOutputStreamingException subclasses, preserving base catches.
- CalendarCommands has a temporary class-scoped CR1400 suppression pending
  [inheritance audit #247](https://github.com/AntonC9018/ScheduleLib/issues/247).
  [Test audit #248](https://github.com/AntonC9018/ScheduleLib/issues/248) covers
  the baseline failures and repair/retirement decisions.

The follow-up rollout also reproduced a CR0301 fixer failure on
`from == null ? nameof(from) : nameof(to)`. CodingRules `dfd239d` validates
nameof constants directly instead of treating them as method argument lists.
The two regression cases fail before the correction and preserve the saved
exception parameter name afterward. The correctness candidate before the
performance correction was
`dfd239df6145032ebd15ca12523810e475770924` (SHA-256
`359943fba8b92147885fe1905ad3799f6b3ea7812de1bba73c74f5bd7a951145`).
All 1,395 CodingRules tests and ten consumer gates pass on AWS. The new gate
covers default/custom builder suffixes, markers, C# 14 extension blocks, exact
external-method exemptions through assembly/MSBuild, and saved braces/cast fixes.
Suffixes use commas because semicolons are analyzer-config comment delimiters.
The local candidate restore is verified across all 39 solution projects plus
the separate CLI test graph. New diagnostics join the existing warning policy
rather than being unexpectedly promoted by the repository's warnings-as-errors.

## Follow-up rollout result — 2026-10-25

The current local candidate is CodingRules `0f02bab`, package
`Anton.CodingRules.0.1.0.nupkg`, SHA-256
`e0268e2bc377add6b2f5265fcb4fcc162c8ea4f276511a2ffb071690b978caca`.
All **1,401 analyzer/fixer/package tests** and **ten consumer gates** pass on
AWS. Its assets are verified locally across all 39 solution projects and the
24-project CLI dependency graph. This is a tested local candidate, not a NuGet
publication.

The applied C# patch is committed as `874f167` (319 files). It was byte-compared
with the AWS-tested patch. Generated continuation indentation was normalized
with structural syntax and line-count checks. The fixer still captures some
operands conservatively to preserve argument evaluation order; numeric name
suffixes resolve collisions rather than establish additional domain concepts.

Clean Release rebuilds pass with **zero errors**: solution 516 warnings in
1m13s; CLI 486 warnings in 34s. After deduplication across both targets, **568
source warnings** remain, down from 7,642 (**7,074 removed; 92.6%**). Another
55 project/package advisory rows remain (11 NU1902, 44 NU1903); dependencies
were not upgraded.

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

These are findings left after the packaged fixer passes, not newly accepted
exemptions. They need case review and, for architectural rules, deliberate
design changes. CR1600's exception subtype action is opt-in and has no Fix All
because it changes exact runtime types. Named-argument, braces/cast spacing,
legacy collection API and the earlier automatic rule families are cleared.

All TRX outcomes match baseline exactly: solution **709 cases / 11 failures**;
CLI **767 cases / 3 failures**. There are no new failures and no changed test
outcomes. Existing failures remain tracked in #248, including privacy issue
#228. CalendarCommands' local suppression remains tied to #247.

Statement-operation Fix All now reanalyzes only the edited document after each
edit while retaining the semantic proof checks. The AWS 256-document fixture
falls from **16.23s to 0.72s**, with identical saved-source hashes and unchanged
unrelated documents. See CodingRules' `docs/fix-all-performance.md` for the
reproduction command and measurement limits. The final real-project formatter
passes take 2m44s (solution) and 2m10s (CLI); those source states differ from the
earlier runs, so these times are not a controlled speedup comparison.

The runner has a tested systemd watchdog: every SSH connection renews a
ten-minute lease, checked every fifteen seconds. Background jobs cannot renew
it. Expiry stops the instance and preserves its disk; explicit Terraform cleanup
is still required. The watchdog and Terraform configuration are committed in
`6a1c9e7` and `c5a4930`. Validation artifacts and the tested source patch were
copied locally before final cleanup.

Final Terraform destroy removed all three managed resources. AWS verification
confirms the instance is terminated, its root volume/security group/SSH key are
absent, and no runner with this purpose tag remains active or stopped. Terraform
state is empty. The watchdog remains available in the committed provisioning
configuration for future runners.
