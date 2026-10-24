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

The 2026-10-24 AWS baseline has **7,707 unique findings** across both targets.
The solution alone has 5,066 unique findings; its MSBuild summary reports 5,139
warnings and zero errors. These are different measurements.

## Patterns to discuss

These are review candidates, not newly accepted exemptions. Existing policy
deliberately diagnoses several of these shapes. Keep findings visible until
the boundary is decided rather than turning off a rule family.

### CR1001: externally required overloads

`ManualObservableList<T>` implements both `IList<T>` and `IList`.
Its `Insert(int, T)` / `Insert(int, object?)` and
`CopyTo(T[], int)` / `CopyTo(Array, int)` pairs satisfy those interfaces.
Replacing these signatures with a configuration builder cannot satisfy the
external contract. These are strong false-positive candidates; analyze the
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

`CalendarCommands` also has a tested virtual seam. Keep it under architectural
review rather than deleting or sealing the seam to satisfy the warning.

## Baseline test limitations

All builds and tests run on the temporary Terraform-managed AWS runner,
sequentially. No local application data or credentials are uploaded.
Before fixes, AWS reproduces these 15 failing cases:

- `ThesisNameTests`: `CurriculumParsedCorrectly`, `FileNameParsedCorrectly`.
- `FmiWebsiteInteropTests.CountTest`.
- `Desktop.Tests.EventTests`: `NodeChangedTest`, `LayerChangedTest`, `ReloadWorks`.
- `ScheduleLib.OnlineRegistry.Tests.TokenTests.TokenGeneratedOnLogIn`.
- `ScheduleSourceSanityTests.Schedule2026Semester1PassesSanityChecks`.
- `PdfImportTests.SourcePdfsMatchReviewedCellContentAndMembership`, years 1–3.
- `CurrentSourceMergeTests`: both fresh/cached merge comparison cases.
- `RegistryHistoryTests.ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext`.

The last case is a real existing privacy regression tracked by #228; a warning
cleanup must not claim to resolve it. Data/live-service failures on a runner
without private local configuration are recorded separately from regressions
introduced by code fixes.
