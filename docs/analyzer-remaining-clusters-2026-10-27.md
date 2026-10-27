# Remaining ScheduleLib review clusters

This groups the remaining findings by the change a maintainer would review.
It proposes follow-up work; it does not apply those changes. Counts describe
source diagnostics, so one expression can have findings from two rules.

The source is `b1c9aed`; the analyzer candidate is `2abd37a`, which fixes all
four CodeRabbit findings from [CodingRules #35](https://github.com/AntonC9018/CodingRules/pull/35).
The refreshed build inventory contains 142 source findings and 55 separate
dependency-advisory rows. Both Release rebuilds pass. Analyzer-only regressions
remain in CodingRules; this review adds no ScheduleLib tests.

| Cluster | Findings | Rule |
| --- | ---: | --- |
| C1: Argument computations in tests | 63 | CR0300 |
| C2: Invariant assertion arguments | 11 | CR0300 |
| C3: Other production argument computations | 16 | CR0300 |
| C4: Conditional values and arithmetic | 6 | CR0301–CR0302 |
| C5: Remaining Boolean scratch decisions | 19 | CR0303 |
| C6: Explicit return outcomes | 7 | CR0001/CR0006 |
| C7: Projection and pipeline stages | 4 | CR0400–CR0401 |
| C8: Interpolated chains and one initializer | 8 | CR0801/CR0805 |
| C9: Deferred inheritance design | 8 | CR1400 |
| **Total** | **142** | |

## C1: Argument computations in tests — 63

A large subset now constructs named records inside fixture calls, for example:

```csharp
fixture.Attendance(new Fixture.AttendanceSource(key, ["Synthetic Ana"]));
```

[RegistryStudentCoverageTests](../src/Features/Tests/OnlineRegistry/RegistryStudentCoverageTests.cs)
contains the attendance-source cases. Other examples include authorization
requests in [AuthenticationTests](../src/MainCli/Tests/AuthenticationTests.cs),
website teacher records in [WebsiteExportTests](../src/MainCli/Tests/WebsiteExportTests.cs),
and nested filesystem operations in CLI fixtures. Calendar timestamp expectations
also trigger argument-staging findings after their shared helper extraction.

The record migration is already approved. The next decision is how to stage
these call arguments without making fixtures harder to read. The current rule
requires named values, for example:

```csharp
var source = new Fixture.AttendanceSource(key, ["Synthetic Ana"]);
var attendance = fixture.Attendance(source);
```

Use fixture builders only where they express a shared setup operation; avoid
introducing a generic helper solely to disguise nested construction.

## C2: Invariant assertion arguments — 11

These are the GetType/IsAssignableTo checks in
[CallHelper](../src/Features/LayeredData/BasicOperations/CallHelper.cs) and
[OpenHierarchy](../src/Features/LayeredData/BasicOperations/OpenHierarchy.cs):

```csharp
Debug.Assert(
    merger.GetType().IsAssignableTo(typeof(IMerger<T>)),
    "The merger must implement the requested typed merger contract.");
```

The explicit messages and short-chain layout are already applied. CR0805's
allowance for two calls does not exempt an argument from CR0300's operation
staging. A DEBUG-only validation function is a candidate for this cluster;
its scope and message should describe the invariant. The broader safe-default
public API checks remain tracked separately in [#249][controlled-checks].

## C3: Other production argument computations — 16

These include records added to collections, the Google token context passed
to CreateCredential, a nested JSON deserialization, an outcome callback and
lookup results used in projections. Representative sites:

- [Registry intake][registry-intake]:
  `destinations.Add(new(Group: group, Selection: selection))`.
- [GoogleAuthentication](../src/MainCli/GoogleAuthentication.cs):
  construct `GoogleTokenContext` before CreateCredential.
- [MarkerConfigHelper](../src/Features/LayeredConfig/BuilderNew/Impl/MarkerConfigHelper.cs):
  create the node builder before constructing MarkerNode.
- [SpecializationRegistry](../src/ScheduleLib/ScheduleLib.Core/Model/SpecializationRegistry.cs):
  construct Entry before adding it to the registry.

Most can use meaningful local values. The three registry projection sites also
appear in C7; address their pipeline shape together rather than making two
independent rewrites of the same expression.

## C4: Conditional values and arithmetic — 6

Five findings concern conditional values: the attendance lesson type, a
MIME/body choice in MicrosoftTests, a login-page body in RegistryHistoryTests,
the nested year-dependent course IDs in RegistryHistoryTopicsTests, and the
optional start time in ScheduleImportContext. Each needs a named value with
explicit decision steps at its original evaluation point.

The sixth is Excel column-number arithmetic:

```csharp
col *= 'Z' - 'A' + 1;
```

[ExcelRangeHelper](../src/ScheduleLib/ScheduleLib.Helper.Excel/ExcelRangeHelper.cs)
should name the alphabet/radix calculation. Preserve numeric types and the
existing checked/unchecked context.

## C5: Remaining Boolean scratch decisions — 19

These are the remaining `bool conditionResult; if (...) ...` trees. They split
by their actual inputs and mutation constraints:

- Two worksheet/header decisions in
  [AttendanceExcel](../src/Features/OnlineRegistry/AttendanceExcel/AttendanceExcel.cs)
  use `in` header/parse parameters.
- One date-matching decision in
  [Attendance](../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/Attendance.cs)
  is in the readonly AttendanceDate record struct.
- Four roster/group decisions in
  [PublicApi](../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/PublicApi.cs)
  and one lesson comparison in
  [LessonBuilder](../src/ScheduleLib/ScheduleLib.Core/Model/Builders/LessonBuilder.cs)
  need their group/comparison inputs kept explicit.
- Nine parsing decisions span RegistryHistoryTopics, ScrapedStringsParser,
  ThesesListParser, CourseNameParser, three LessonParser locations, Name and
  ScheduleDocumentParserHelper. They involve spans, readers, lexer state or
  references whose reads/mutations must remain at the same point.
- Two punctuation/whitespace decisions in
  [StringCleanHelper](../src/ScheduleLib/ScheduleLib.Core/Parsing/StringCleanHelper.cs)
  use iterator/struct state.

Prefer direct guards when they fit the containing control flow. Otherwise use
small helpers with explicit inputs, including ref/in inputs where necessary,
instead of capturing ref-like state. Do not merge unrelated parser policies
into a common context merely because the generated trees look alike.

## C6: Explicit return outcomes — 7

Five final predicates remain in DriveApi, two AttendanceLabMerges lambdas,
RegistryAssessments and RegistrySyncDiagnostics. Two nested teacher-name
comparisons remain in TeacherBuilder. Example:

```csharp
return merge.SurvivingSubGroup == partition.SubGroup;
```

These need explicit outcome branches under the current return policy. Review
surrounding lambdas, comments and captures before applying a manual rewrite.
[TeacherBuilder](../src/ScheduleLib/ScheduleLib.Core/Model/Builders/TeacherBuilder.cs)
contains the equality/prefix choice that needs the nested-return treatment.

## C7: Projection and pipeline stages — 4

Three are the new named registry projections:

- [RegistryCourseNameLookup][registry-course-names]:
  normalize a course name before constructing NormalizedCourseName.
- [RegistryHistoryTopics][registry-history-topics]:
  two preceding-year projections calculate academic-year starts.

Keep these distinct domain operations. A block projection with a named result
is sufficient unless a local function improves reuse/readability.

The fourth is the larger projection in
[PrintFreeHoursOfGroup](../src/Features/Integration/Tasks/PrintFreeHoursOfGroup.cs),
which needs a named local stage rather than a large inline lambda.

## C8: Layout — 8

Seven chain findings are inside interpolated strings: six in
[FreeHoursTests](../src/MainCli/Tests/FreeHoursTests.cs) and one in
[GoogleCredentialsConfig.BuildLoginCommand][google-credentials].
The latter chains the escaping of backslashes and quotes. Name the escaped
teacher text before interpolation, preserving escape order. The test sites
combine JSON lookups and summary formatting; meaningful formatting helpers are
candidates, with evaluation order preserved.

The last is the single-member JsonSerializerOptions initializer in
[CliHost](../src/MainCli/CliHost.cs). It remains on one line in the current
source and requires the agreed expanded layout.

## C9: Inheritance — 8, explicitly deferred

Six command classes, FakeGraphCommands in MicrosoftTests, and
RegistrySubmissionRejectedException remain. Their framework dispatch, test
seams and exception hierarchy need design review; no sealing/base-class
migration is included in the current cleanup.

[controlled-checks]: https://github.com/AntonC9018/ScheduleLib/issues/249
[registry-intake]:
  ../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs
[registry-course-names]:
  ../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryCourseNameLookup.cs
[registry-history-topics]:
  ../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryHistoryTopics.cs
[google-credentials]:
  ../src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleCredentialsConfig.cs
