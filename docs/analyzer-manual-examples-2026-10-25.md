# Analyzer review decisions

This records the review of the 2026-10-25 inventory and the maintainer's
2026-10-26 decisions. The original 94 tuple locations are migrated to named
record structs. Counts below describe the original inventory, not the current
warning count. Links target files, with symbols named in the text; GitHub `#L`
anchors did not work reliably in the local review viewer.

## Record structs, grouped by meaning

- **Spreadsheet resources (9):** `StylesheetResourceEntry<TElement>` carries the
  appended element and its stylesheet identifier. `StylesheetBuilder.Next`,
  `INext.Next` and `CountedAdderBase.Next` share that result contract.
  [Stylesheet](../src/Features/Integration/ExcelBuilder/Stylesheet.cs).
- **Moodle navigation and parsing (7):** `MoodleBreadcrumbItem` holds Name/Href;
  `ParsedQualification` and `ParsedSection` represent separate parsing results.
  [Quiz scraper](../src/Features/MoodleInterop/MoodleQuizScraper.cs),
  [path parser](../src/Features/MoodleInterop/PathParser.cs).
- **Registry projections (19):** `RegistryLessonDestination` connects one group
  to its lesson selection; attendance, course names and assessment results use
  their own types. [Destination](../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryLessonDestination.cs).
- **Registry tests (17):** fixture-specific records retain their actual meanings.
  Routing expectations use the production destination type.
  [RegistryLessonRoutingTests](../src/Features/Tests/OnlineRegistry/RegistryLessonRoutingTests.cs).
- **CLI protocol and fixtures (14):** `StreamedUpdate`, test-local
  `CalendarRequest`, `DriveRequest`, `ProcessResult`, `ThesisRow` and
  `TeacherIdentity` remain separate concepts.
  [Command context](../src/MainCli/CliCommandContext.cs).
- **Core schedule/parser data (10):** `ScheduleDateRange`, `CourseNameMapping`,
  `GroupLabel`, `DateInterval`, `TableCellWithPosition` and
  `UnparsedModifierGroup` name their individual roles. The modifier group's
  members remain fields so its lexer can still be advanced through a ref local.
  [Lesson parser](../src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs).
- **Helper results (6):** expression `Expressions` results, `ItemPair<T>` and
  converter-specific reflection candidates retain their own contracts.
  [Expression boxes](../src/ScheduleLib/ScheduleLib.Helper/Expressions/Box.cs).
- **PDF layout and normalization (3):** `ColumnHeader`, `BorderIntersection` and
  `ParityRepairRule`. [PDF table reader](../src/Features/ParsePdf/PdfTableReader.cs).
- **Layered configuration (3):** `MarkerNode`, `NodeDeletion` and `ProviderSetup`.
  [Marker configuration](../src/Features/LayeredConfig/BuilderNew/Impl/MarkerConfigHelper.cs).
- **Desktop metadata (2):** `PropertyRegistration` and `ParentRegistration`.
  [Property set](../src/Gui/Desktop/NodeData/Common/PropertySet/NodeDataPropertySet.cs).
- **DFS traversal (1):** `ContextFactory`, which describes context construction;
  it is not a general traversal frame.
  [DFS](../src/Features/LayeredData/TreeImpl/Enumeration/Infrastructure/Dfs.cs).
- **Default configuration (1):** reuse `CourseNameMapping` for the same
  source/destination course-name mapping.
  [Defaults](../src/ScheduleDefaults/Config.cs).
- **Shared PDF measurement (1):** `OptimizedColumnArrangement`.
  [Shared layout](../src/ScheduleLib/ScheduleLib.Generator.Pdf/Generation/GroupColumn/SharedLayout.cs).
- **Parser test context (1):** `DocContext`.
  [Sanity tests](../src/ScheduleLib/Tests/Parser/ScheduleSanityChecksTests.cs).

Examples of the resulting contracts:

```csharp
StylesheetResourceEntry<OpenXmlElement> Next(StylesheetResource resource)
List<MoodleBreadcrumbItem> Path
RegistryLessonDestination destination
```

The API migration intentionally changes tuple-based signatures. In-repository
callers migrate together; external callers constructing tuple literals will
need the corresponding records. Do not introduce implicit tuple conversions
solely to preserve the shape being removed.

## Decision functions and calculation results

The screenshot's reassigned Boolean tree came from CR0200's old mixed-condition
lowering. CR0303 then extracted adjacent assignment trees without flattening
else branches. Return predicates now use explicit early exits, retaining the
original short-circuit evaluation order. Redundant else after return/throw is
reported as CR0205; a lone return ternary is covered by CR0001.

The `Start` output-format check must still test both `Json` and `Jsonl`: it
rejects their combination, while JSONL alone is valid. Its predicate uses a
guard followed by `return Output.Jsonl`; dropping the Json check would change
that behavior.

For `ShouldSkipMerge`, `groups` is an `in` parameter. The helper receives it
explicitly, by `in`, instead of capturing it. This is the entire capture concern;
no LessonGroups redesign is included.
[RegistryLabMerge](../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryLabMerge.cs).

`StringSearchHelper.RemainingMatchText` takes the section-exhausted flag, input
memory and matched length and returns the remaining memory. It does not capture
a ref-like reader or mutate the caller's Boolean scratch state.
[StringSearchHelper](../src/Features/CurriculumShared/StringSearchHelper.cs).

## Chain layout and assertions

A chain of at most two method calls may remain on one line when the full line
is at most 90 characters. Longer chains wrap. A wrapped argument starts below
the enclosing call's opening parenthesis, with chain continuations indented
inside that argument.

Reviewed assertions now have meaningful explicit messages. Their messages no
longer depend on the source text captured by CallerArgumentExpression. The
Boolean checks retain their existing behavior. Converting public API assertions
to configurable safe-by-default checks is tracked in
[issue #249](https://github.com/AntonC9018/ScheduleLib/issues/249); that change is
separate from layout and message cleanup.

## Google authorization inputs and repeated formatting

Consent, Refresh and CreateCredential share the client, account and scope set
through `GoogleTokenContext`. Authorization and storage remain distinct inputs
for the stages that need them; cancellation remains an execution control.
Resolve uses `GoogleCredentialRequest` for configuration, scopes, teacher and
services. [Google authentication](../src/MainCli/GoogleAuthentication.cs),
[credential resolution](../src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleCredentialsConfig.cs).

The test-local `ExpectedCalendarTimestamp(DateTimeOffset? value)` captures the
shared invariant round-trip representation for both calendar endpoints.
[CalendarSyncTests](../src/MainCli/Tests/CalendarSyncTests.cs).

Command inheritance remains deferred. These decisions do not authorize the
separate public API runtime-check migration or a general LessonGroups redesign.

The implemented source and final validation results are recorded in the
[readability cleanup](analyzer-readability-cleanup-2026-10-27.md).
