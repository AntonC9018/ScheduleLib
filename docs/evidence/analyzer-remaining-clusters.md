# ScheduleLib cleanup and remaining review cases

Updated 2026-10-28 for consumer source `9e09740`, using CodingRules candidate
`97de786`. The single-construction allowance reduces the previous 62 reviewed
findings to **26**, down from 134 before the earlier cleanup. The remaining
findings are all CR0300: 16 multiple-construction arguments and 10 readable
assertions. Other analyzer/fixer review cases remain in [CodingRules #36][review].
The 55 dependency-advisory rows are separate from this source cleanup.

The previous table put one finding in the wrong bucket: there were 10 assertion
findings and 17 other production findings, rather than 11 and 16. Its combined
CR0300 total was correct. The counts below come from the refreshed inventory.

| Reviewed cluster | Before | Remaining |
| --- | ---: | ---: |
| Nested computations/construction in tests | 63 | 16 |
| Runtime-type checks in Debug.Assert arguments | 10 | 10 |
| Other production arguments | 17 | 0 |
| Embedded conditional values and arithmetic | 6 | 0 |
| Mutable Boolean decision trees | 19 | 0 |
| Return decisions | 7 | 0 |
| Projection and pipeline stages | 4 | 0 |
| Chains and initializer layout | 8 | 0 |
| **Reviewed scope** | **134** | **26** |

## Accepted: one simple directly constructed argument

[RegistryStudentCoverageTests.cs:15][coverage] contains the reviewed example:

```csharp
fixture.Attendance(new Fixture.AttendanceSource(key, ["Synthetic Ana"]));
```

This is now accepted; see the
[single-construction allowance](../decisions/analyzer-single-construction-allowance.md).

Credential requests in [AuthenticationTests.cs:34][authentication] and website
teacher records in [WebsiteExportTests.cs:137][website-teachers] also benefit.
The allowance removes 27 test findings and all 9 simple production-construction
findings, including [RegistryIntakeLabCleanup.cs:51][destination].

## Remaining: multiple constructed arguments in tests — 16

There are eight fixture calls with two directly constructed arguments each.
[RegistryStudentCoverageTests.cs:117][multiple-constructions] is one example:

```csharp
var attendance = fixture.Attendance(
    new Fixture.AttendanceSource(first, ["Synthetic Ana"]),
    new Fixture.AttendanceSource(second, ["Synthetic Boris"]));
```

These remain intended CR0300 findings. Name the separate roster inputs or use a
purposeful fixture builder when it clarifies the setup. The rule does not force
an arbitrary builder abstraction.

The inline-key change is separate: the observation call at
[RegistryStudentCoverageTests.cs:22][observe] passes `[key]` directly for routed
and membership keys. It is unrelated to the intake cleanup file.

## Remaining: readable Debug.Assert calls — 10

These checks establish that an object implements the expected runtime type:

```csharp
Debug.Assert(
    merger.GetType().IsAssignableTo(typeof(IMerger<T>)),
    "The merger must implement the requested typed merger contract.");
```

See [CallHelper.cs:26][assertion] and [OpenHierarchy.cs:312][hierarchy-assertion].
They remain as written. CR0300 currently considers IsAssignableTo with its type
argument a nested computation. This is the whole C2 concern; it does not require
splitting this readable check into more variables.

## Applied changes

- Replaced scratch Boolean trees with direct guards or meaningful helpers.
  [Attendance.cs:40][date-match] shows explicit month/day/year/time matching.
  [PublicApi.cs:231][partition] keeps group inputs explicit.
  [StringCleanHelper.cs:72][punctuation] reads its own ref-struct state without a
  captured context.
- Preserved parser short-circuit reads and mutations. The explicit course-word
  decision is at [CourseNameParser.cs:412][course-words]; the terminal-state
  handling is at [LessonParser.cs:345][lesson-parser].
- Named embedded computations and pipeline stages, including
  [PrintFreeHoursOfGroup.cs:154][free-hours-stage] and
  [GoogleAuthentication.cs:378][google-context]. Callback data remains constructed
  only when a callback is present.
- Expanded return decisions and the JsonSerializerOptions initializer.
  [CliHost.cs:396][json-options] shows the initializer layout. Its startup and
  output architecture is tracked separately in [ScheduleLib #250][cli-host].
- Corrected the reviewed argument layout and replaced the one-use collection
  helper with a local at [WebsiteExportTests.cs:99][schedule-files]. The file
  count comparison now uses a named integer at
  [WebsiteExportTests.cs:74][file-count].
- Restored inline `[key]` inputs in
  [RegistryStudentCoverageTests.cs:22][observe].
- Removed the shown unchanged receiver/value copies.
  [RegistryIntakeLabCleanup.cs:124][diagnostic] passes the existing course and
  group values directly.
- Named the Excel column radix at [ExcelRangeHelper.cs:110][excel-radix].

## Separate review cases

[CodingRules #36][review] preserves the missing analyzer/fixer coverage and the
remaining design questions. The large routing predicate is still visible at
[RegistryIntakeLabCleanup.cs:91][routing-predicate]. The string action kind is at
[RegistryIntakeLabCleanup.cs:129][action-kind]. They need the later focused review;
an absence of a warning is not evidence that either shape is satisfactory.

No analyzer-only regression cases were added to ScheduleLib. Reusable analyzer
regressions remain in CodingRules.

## Validation

The candidate passes 1,488 analyzer/fixer/package cases and all 11 consumer gates.
Both ScheduleLib Release rebuilds pass with zero errors, and all 18 website
export tests pass. All 538 C# hashes match the transferred source snapshot.
No ScheduleLib test cases were added.

The previous complete runtime suites at `b4270ba` established this failure
baseline: solution 715 cases / 11 existing failures / 7 skipped; CLI 770 cases /
3 existing failures. This pass changes receiver-chain whitespace in one test file
and analyzer policy; the complete runtime suites were not rerun. Existing runtime
failures remain tracked in [ScheduleLib #248][baseline].

The local Nix SDK still fails to load CoreCLR: its loaded glibc 2.42 libm lacks
GLIBC_2.44. Candidate restoration and builds are verified on the AWS worker. The
local candidate configuration and package are prepared, but local restoration is
explicitly unverified. No machine configuration changes are included here.

[review]: https://github.com/AntonC9018/CodingRules/issues/36
[baseline]: https://github.com/AntonC9018/ScheduleLib/issues/248
[coverage]:
  ../../src/Features/Tests/OnlineRegistry/RegistryStudentCoverageTests.cs#L15
[observe]:
  ../../src/Features/Tests/OnlineRegistry/RegistryStudentCoverageTests.cs#L22
[authentication]: ../../src/MainCli/Tests/AuthenticationTests.cs#L34
[website-teachers]: ../../src/MainCli/Tests/WebsiteExportTests.cs#L137
[assertion]: ../../src/Features/LayeredData/BasicOperations/CallHelper.cs#L26
[hierarchy-assertion]: ../../src/Features/LayeredData/BasicOperations/OpenHierarchy.cs#L312
[destination]:
  ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs#L51
[date-match]: ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/Attendance.cs#L40
[partition]: ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/PublicApi.cs#L231
[punctuation]: ../../src/ScheduleLib/ScheduleLib.Core/Parsing/StringCleanHelper.cs#L72
[course-words]: ../../src/ScheduleLib/ScheduleLib.Core/Parsing/CourseNameParser.cs#L412
[lesson-parser]:
  ../../src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs#L345
[free-hours-stage]: ../../src/Features/Integration/Tasks/PrintFreeHoursOfGroup.cs#L154
[google-context]: ../../src/MainCli/GoogleAuthentication.cs#L378
[json-options]: ../../src/MainCli/CliHost.cs#L396
[schedule-files]: ../../src/MainCli/Tests/WebsiteExportTests.cs#L99
[file-count]: ../../src/MainCli/Tests/WebsiteExportTests.cs#L74
[diagnostic]:
  ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs#L124
[excel-radix]: ../../src/ScheduleLib/ScheduleLib.Helper.Excel/ExcelRangeHelper.cs#L110
[routing-predicate]:
  ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs#L91
[action-kind]:
  ../../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs#L129

[cli-host]: https://github.com/AntonC9018/ScheduleLib/issues/250

[multiple-constructions]:
  ../../src/Features/Tests/OnlineRegistry/RegistryStudentCoverageTests.cs#L117
