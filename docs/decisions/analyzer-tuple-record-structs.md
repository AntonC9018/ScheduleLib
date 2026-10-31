# Tuple data becomes named record structs (CR0900)

- **Status:** accepted 2026-10-26; implemented in ScheduleLib `b1c9aed`.
- **Scope:** the original 94 CR0900 tuple locations.

## Decision

Each shared tuple concept becomes a record struct named for its meaning. Unrelated tuples are not
grouped merely by shape. The API migration intentionally changes tuple-based signatures:
in-repository callers migrate together, and external callers constructing tuple literals need the
corresponding records. No implicit tuple conversions preserve the removed shape.

- **Spreadsheet resources (9):** `StylesheetResourceEntry<TElement>`, shared by
  `StylesheetBuilder.Next`, `INext.Next`, `CountedAdderBase.Next`.
- **Moodle navigation and parsing (7):** `MoodleBreadcrumbItem` (Name/Href),
  `ParsedQualification`, `ParsedSection`.
- **Registry projections (19):** `RegistryLessonDestination` plus attendance, course-name and
  assessment types.
- **Registry tests (17):** fixture-specific records; routing expectations use the production
  destination type.
- **CLI protocol and fixtures (14):** `StreamedUpdate`, test-local `CalendarRequest`,
  `DriveRequest`, `ProcessResult`, `ThesisRow`, `TeacherIdentity`.
- **Core schedule/parser data (10):** `ScheduleDateRange`, `CourseNameMapping`, `GroupLabel`,
  `DateInterval`, `TableCellWithPosition`, `UnparsedModifierGroup`.
- **Helper results (6):** expression `Expressions` results, `ItemPair<T>`, converter reflection
  candidates.
- **PDF layout and normalization (3):** `ColumnHeader`, `BorderIntersection`, `ParityRepairRule`.
- **Layered configuration (3):** `MarkerNode`, `NodeDeletion`, `ProviderSetup`.
- **Desktop metadata (2):** `PropertyRegistration`, `ParentRegistration`.
- **DFS traversal (1):** `ContextFactory` (context construction, not a general traversal frame).
- **Default configuration (1):** reuse `CourseNameMapping`.
- **Shared PDF measurement (1):** `OptimizedColumnArrangement`.
- **Parser test context (1):** `DocContext`.

`UnparsedModifierGroup` keeps mutable fields because its lexer is advanced through a ref local.
Local-function iterator tuple results remain accepted (Q16), and the broader scalar-local tuple
policy is undecided.

## Links

- Source paths: `src/Features/Integration/ExcelBuilder/Stylesheet.cs`,
  `src/Features/MoodleInterop/`, `src/Features/OnlineRegistry/OnlineRegistry/PublicApi/`,
  `src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs`,
  `src/Features/ParsePdf/PdfTableReader.cs`.
- [Rollout evidence](../evidence/analyzer-rollout.md).
