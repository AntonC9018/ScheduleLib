# ScheduleLib cleanup and remaining review cases

Updated 2026-10-28 for consumer source `b4270ba`, using the unchanged CodingRules
candidate `2abd37a`. This replaces the earlier proposed cluster list.

Within the reviewed clusters, findings fall from **134 to 62**. The 62 remaining
findings are all CR0300 cases held for the policy review in
[CodingRules #36](https://github.com/AntonC9018/CodingRules/issues/36).
The 55 dependency-advisory rows are separate from this source cleanup.

The previous table put one finding in the wrong bucket: there were 10 assertion
findings and 17 other production findings, rather than 11 and 16. Its combined
CR0300 total was correct. The counts below come from the refreshed inventory.

| Reviewed cluster | Before | Remaining |
| --- | ---: | ---: |
| Nested computations/construction in tests | 63 | 43 |
| Runtime-type checks in Debug.Assert arguments | 10 | 10 |
| Other production arguments | 17 | 9 |
| Embedded conditional values and arithmetic | 6 | 0 |
| Mutable Boolean decision trees | 19 | 0 |
| Return decisions | 7 | 0 |
| Projection and pipeline stages | 4 | 0 |
| Chains and initializer layout | 8 | 0 |
| **Reviewed scope** | **134** | **62** |

## Remaining: simple construction in test calls — 43

[RegistryStudentCoverageTests.cs:15](/home/anton/coding/muse/ScheduleLib/src/Features/Tests/OnlineRegistry/RegistryStudentCoverageTests.cs:15)
contains the reviewed example:

```csharp
fixture.Attendance(new Fixture.AttendanceSource(key, ["Synthetic Ana"]));
```

CR0300 currently reports the nested AttendanceSource constructor because it has
supplied arguments. It treats explicit construction and target-typed `new(...)`
the same way. The inline collection is accepted. The maintainer considers simple
implicit construction potentially readable, so this pass does not manufacture
more fixture locals before that boundary is settled.

The other cases construct credential requests, captured HTTP requests, website
teacher records, parser mappings, and a history-analysis input. Examples are
[AuthenticationTests.cs:34](/home/anton/coding/muse/ScheduleLib/src/MainCli/Tests/AuthenticationTests.cs:34)
and
[WebsiteExportTests.cs:142](/home/anton/coding/muse/ScheduleLib/src/MainCli/Tests/WebsiteExportTests.cs:142).

The unnecessary collection/receiver aliases were removed. The observation call
at
[RegistryStudentCoverageTests.cs:22](/home/anton/coding/muse/ScheduleLib/src/Features/Tests/OnlineRegistry/RegistryStudentCoverageTests.cs:22)
now passes `[key]` directly for
both routed and membership keys. Those are intentionally distinct input roles.

## Remaining: readable Debug.Assert calls — 10

These checks establish that an object implements the expected runtime type:

```csharp
Debug.Assert(
    merger.GetType().IsAssignableTo(typeof(IMerger<T>)),
    "The merger must implement the requested typed merger contract.");
```

See
[CallHelper.cs:26](/home/anton/coding/muse/ScheduleLib/src/Features/LayeredData/BasicOperations/CallHelper.cs:26)
and
[OpenHierarchy.cs:312](/home/anton/coding/muse/ScheduleLib/src/Features/LayeredData/BasicOperations/OpenHierarchy.cs:312).
They remain as written. CR0300 currently considers IsAssignableTo with its type
argument a nested computation. This is the whole C2 concern; it does not require
splitting this readable check into more variables.

## Remaining: simple production construction — 9

For example,
[RegistryIntakeLabCleanup.cs:51](/home/anton/coding/muse/ScheduleLib/src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs:51)
contains:

```csharp
destinations.Add(new(Group: group, Selection: selection));
```

The other cases add meaningful records to collections: grade inputs, context
factories, property registrations, settings parents, specialization entries and
unparsed modifiers. These share the inline-construction policy question above.
No collection-wide suppression or constructor exemption was introduced.

## Applied changes

- Replaced scratch Boolean trees with direct guards or meaningful helpers.
  [Attendance.cs:40](/home/anton/coding/muse/ScheduleLib/src/Features/OnlineRegistry/OnlineRegistry/PublicApi/Attendance.cs:40)
  shows explicit month/day/year/time matching.
  [PublicApi.cs:231](/home/anton/coding/muse/ScheduleLib/src/Features/OnlineRegistry/OnlineRegistry/PublicApi/PublicApi.cs:231)
  keeps group inputs explicit.
  [StringCleanHelper.cs:72](/home/anton/coding/muse/ScheduleLib/src/ScheduleLib/ScheduleLib.Core/Parsing/StringCleanHelper.cs:72)
  reads its own ref-struct state without a
  captured context.
- Preserved parser short-circuit reads and mutations. The explicit course-word
  decision is at
  [CourseNameParser.cs:412](/home/anton/coding/muse/ScheduleLib/src/ScheduleLib/ScheduleLib.Core/Parsing/CourseNameParser.cs:412);
  the terminal-state
  handling is at
  [LessonParser.cs:345](/home/anton/coding/muse/ScheduleLib/src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs:345).
- Named embedded computations and pipeline stages, including
  [PrintFreeHoursOfGroup.cs:154](/home/anton/coding/muse/ScheduleLib/src/Features/Integration/Tasks/PrintFreeHoursOfGroup.cs:154)
  and
  [GoogleAuthentication.cs:378](/home/anton/coding/muse/ScheduleLib/src/MainCli/GoogleAuthentication.cs:378).
  Callback data remains constructed
  only when a callback is present.
- Expanded return decisions and the JsonSerializerOptions initializer.
  [CliHost.cs:396](/home/anton/coding/muse/ScheduleLib/src/MainCli/CliHost.cs:396)
  shows the initializer layout.
- Corrected the reviewed argument layout and replaced the one-use collection
  helper with a local at
  [WebsiteExportTests.cs:104](/home/anton/coding/muse/ScheduleLib/src/MainCli/Tests/WebsiteExportTests.cs:104).
  The file
  count comparison now uses a named integer at
  [WebsiteExportTests.cs:77](/home/anton/coding/muse/ScheduleLib/src/MainCli/Tests/WebsiteExportTests.cs:77).
- Restored inline `[key]` inputs and removed the shown unchanged receiver/value
  copies.
  [RegistryIntakeLabCleanup.cs:124](/home/anton/coding/muse/ScheduleLib/src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs:124)
  passes the existing course
  and group values directly.
- Named the Excel column radix at
[ExcelRangeHelper.cs:110](/home/anton/coding/muse/ScheduleLib/src/ScheduleLib/ScheduleLib.Helper.Excel/ExcelRangeHelper.cs:110).

## Separate review cases

[CodingRules #36](https://github.com/AntonC9018/CodingRules/issues/36)
preserves the missing analyzer/fixer coverage and the
remaining design questions. The large routing predicate is still visible at
[RegistryIntakeLabCleanup.cs:91](/home/anton/coding/muse/ScheduleLib/src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs:91).
The string action kind is at
[RegistryIntakeLabCleanup.cs:129](/home/anton/coding/muse/ScheduleLib/src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryIntakeLabCleanup.cs:129).
They need the later focused review;
an absence of a warning is not evidence that either shape is satisfactory.

No analyzer-only regression cases were added to ScheduleLib. Reusable analyzer
regressions remain in CodingRules.

## Validation

Both final Release rebuilds pass with zero errors. Focused parser/attendance and
CLI checks pass. The complete suites match the established failure baseline:

- Solution: 715 cases; 697 passed, 11 existing failures, 7 skipped.
- CLI: 770 cases; 767 passed, 3 existing failures.

The failure names exactly match the previous baseline tracked in
[ScheduleLib #248](https://github.com/AntonC9018/ScheduleLib/issues/248);
no new failures were introduced. The 538 C# source
hashes match the snapshot transferred for validation. No analyzer implementation
or package binary changed during this consumer pass.
