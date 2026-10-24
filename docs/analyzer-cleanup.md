# Analyzer cleanup and warning review

Work item: [#246](https://github.com/AntonC9018/ScheduleLib/issues/246).
This review covers `All.sln` **and** the separately built
`src/MainCli/Tests/MainCli.Tests.csproj`. The latter is not part of the solution;
solution-only counts understate the CLI cleanup.

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

## Validated candidate and applied changes

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

## Final inventory — 2026-10-24

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

## Patterns to discuss

These are review candidates, not newly accepted exemptions. Existing policy
deliberately diagnoses several of these shapes. Keep findings visible until
the boundary is decided rather than turning off a rule family.

### CR1001: externally required overloads

`ManualObservableList<T>` implements both `IList<T>` and `IList`.
Its `Insert(int, T)` / `Insert(int, object?)` and
`CopyTo(T[], int)` / `CopyTo(Array, int)` pairs satisfy those interfaces.
Replacing these signatures with a configuration builder cannot satisfy the
external contract. The four required implementations now carry method-scoped,
justified suppressions; the analyzer policy itself remains unchanged. These are strong false-positive candidates; analyze the
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

## Runner cleanup

Terraform destroy completed successfully (three managed resources). AWS queries
confirm the instance is terminated and its root volume, temporary SSH key and
security group are absent; Terraform state is empty. Logs and package artifacts
were retrieved before destruction. The runner used one `m7i-flex.large`, with
no NAT gateway or reserved commitment. Estimated compute cost is under $2,
well within the authorized $100 budget; this is an estimate, not a billing total.
