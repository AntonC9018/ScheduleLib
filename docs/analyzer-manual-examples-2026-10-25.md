# Analyzer manual examples (2026-10-25)

This is a review aid for the findings in `complete-inventory.json` from the
CodingRules review. The examples are representative source snippets, not an
approved migration plan. Proposed names are semantic candidates; inspect all
producers and consumers before choosing a shared type. CR1400 is intentionally
not covered here.

## CR0900 — tuple data, grouped by domain meaning

The 94 inventory locations are assigned once to the clusters below. Counts are
diagnostic locations, not a claim that every occurrence in a cluster should
share one type. A record should represent one stable concept and may have a
different name or shape from another record in the same cluster.

| Meaning cluster | Findings | Representative location | Candidate record name(s) |
| --- | ---: | --- | --- |
| Spreadsheet style allocation and registration | 9 | [`Stylesheet.cs:81`](../src/Features/Integration/ExcelBuilder/Stylesheet.cs#L81) | `StylesheetResourceId` plus the existing OpenXml element, or a purpose-built `StylesheetResourceItem` |
| Moodle quiz paths, attempts and parsed path components | 7 | [`MoodleQuizScraper.cs:19`](../src/Features/MoodleInterop/MoodleQuizScraper.cs#L19) | `QuizPathSegment`; keep parsing-result shapes distinct |
| Registry attendance, routing, history and lesson-selection projections | 19 | [`RegistryLessonRouting.cs:287`](../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryLessonRouting.cs#L287) | `RegistryLessonDestination`; other projections need their own names |
| Registry test fixtures/equations and route expectations | 17 | [`RegistryLessonRoutingTests.cs:184`](../src/Features/Tests/OnlineRegistry/RegistryLessonRoutingTests.cs#L184) | `ExpectedLessonRoute` or a test-local fixture record, according to each case |
| CLI protocol updates, settings/storage and provider test data | 14 | [`CliCommandContext.cs:283`](../src/MainCli/CliCommandContext.cs#L283) | `CliStreamUpdate`; storage and provider tuples are separate concepts |
| Core schedule model and parsing intermediates | 10 | [`LessonParser.cs:648`](../src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs#L648) | `UnparsedSubGroupModifier`; separately `ParsedCourseName` / `ScheduleDocumentPart` where applicable |
| Helper expression bounds, enumerable projections and converter state | 6 | [`Box.cs:11`](../src/ScheduleLib/ScheduleLib.Helper/Expressions/Box.cs#L11) | `BoxBounds`; converter protocol state should be named independently |
| PDF table/layout and text normalization | 3 | [`PdfTableReader.cs:136`](../src/Features/ParsePdf/PdfTableReader.cs#L136) | `PdfCellPosition`; `NormalizedTextRange` is distinct |
| Layered configuration markers and topic metadata | 3 | [`MarkerConfigHelper.cs:51`](../src/Features/LayeredConfig/BuilderNew/Impl/MarkerConfigHelper.cs#L51) | `MarkerConfigEntry`; topic metadata gets its own type |
| Desktop property-set key/value metadata | 2 | [`NodeDataPropertySet.cs:47`](../src/Gui/Desktop/NodeData/Common/PropertySet/NodeDataPropertySet.cs#L47) | `NodeDataPropertyMetadata` |
| Layered-data DFS traversal state | 1 | [`Dfs.cs:218`](../src/Features/LayeredData/TreeImpl/Enumeration/Infrastructure/Dfs.cs#L218) | `DfsFrame` |
| CLI default configuration pair | 1 | [`Config.cs:30`](../src/ScheduleDefaults/Config.cs#L30) | `ScheduleDefaultOverride` |
| PDF shared layout measurement | 1 | [`SharedLayout.cs:98`](../src/ScheduleLib/ScheduleLib.Generator.Pdf/Generation/GroupColumn/SharedLayout.cs#L98) | `SharedLayoutMeasurement` |
| Parser sanity-check expected data | 1 | [`ScheduleSanityChecksTests.cs:401`](../src/ScheduleLib/Tests/Parser/ScheduleSanityChecksTests.cs#L401) | Test-local name based on the asserted invariant |

**Total: 94.** The examples illustrate why matching tuple arity is not a
grouping rule. For instance, `StylesheetResource.Next` and its `INext.Next`
contract carry an identifier and an OpenXML object ([`Stylesheet.cs:81`](../src/Features/Integration/ExcelBuilder/Stylesheet.cs#L81),
[`Stylesheet.cs:163`](../src/Features/Integration/ExcelBuilder/Stylesheet.cs#L163));
they should be considered together. In contrast, the parser's
`(LimitedLexerScope Lexer, ReadOnlyMemory<char> SubGroup)` list is a deferred
parse state ([`LessonParser.cs:648`](../src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs#L648)),
not another generic pair. Confirm mutability, lifetime, and serialization
behavior before selecting `record` versus `record struct`.

Representative current tuple declarations:

```csharp
// Stylesheet: registration returns an identifier with its OpenXML element.
internal (uint Id, OpenXmlElement Obj) Next(StylesheetResource resource)

// Moodle breadcrumb navigation; this has a separate domain meaning.
public required List<(string Name, string? Href)> Path { get; init; }

// Registry routing passes a destination and its selected lesson audience together.
static void AddAudience(
    Dictionary<Uri, Dictionary<AnyLessonId, LessonGroups>> result,
    (GroupLink Group, RegistryLessonSelection Selection) destination,
    AnyLessonId id,
    GroupId parent)
```

## CR0303 — branching conditions and non-capturable inputs

The relevant current code in `StringSearchHelper` is:

```csharp
bool conditionResult;
if (p.IsEmpty)
{
    conditionResult = remainingMem.Length > currentSlice.Length;
}
else
{
    conditionResult = false;
}

if (conditionResult)
{
    ref var x = ref extraStuffFromMatch.Array[sectionIndex];
    if (x.IsEmpty)
    {
        x = remainingMem[currentSlice.Length..];
    }
}
```

The decision means “did the match exhaust the section while input remains?”
A local function could accept the reader and lengths explicitly; separate
nested guards could express this small case directly without an assignment
scratch variable. These are proposals awaiting review.

There are 21 locations. A generated local function cannot capture `ref`, `in`,
or ref-like values. Preserve the explicit branching decision while passing
such inputs as parameters, or leave the guard inline where that reads better.

`RegistryLabMerges.Find` takes `in LessonGroups groups`; the branch's meaning
is “is this merge applicable to one valid group and the all partition?”
([`RegistryLabMerge.cs:19`](../src/Features/OnlineRegistry/OnlineRegistry/PublicApi/RegistryLabMerge.cs#L19)).
An explicit-parameter alternative is:

```csharp
bool ShouldSkipMerge(in LessonGroups candidateGroups, CourseId candidateCourse,
    GroupPartitionKey candidatePartition)
{
    if (!candidateGroups.IsSingleGroup)
    {
        return true;
    }
    if (candidateCourse.IsInvalid)
    {
        return true;
    }
    return candidatePartition != GroupPartitionKey.All;
}

if (ShouldSkipMerge(groups, course, partition))
{
    return null;
}
```

The local function can accept an `in` parameter explicitly; the automatic
fix currently declines this rewrite because it would capture the enclosing parameter. The
other distinct constraint is the ref-like `SequenceReader` flow in
[`StringSearchHelper.cs:75`](../src/Features/CurriculumShared/StringSearchHelper.cs#L75).
Pass the reader explicitly (and by `ref` if mutation is required), or retain
its direct guard; do not capture it. The remaining findings include ordinary
locals where a simple local function is viable, but the same helper name should
not be imposed across unrelated parser and registry decisions.

## CR0805 — fluent-chain line layout

There are 22 findings: 13 in `Debug.Assert` expressions and 9 inside
interpolated strings. `Debug.Assert(condition)` supplies caller-expression
text automatically. A line-wrap edit can alter that diagnostic text even when
the Boolean value is unchanged. If the call is reformatted, preserve the old
text explicitly using the overload's `message` argument (or retain the source
layout). Example locations include [`CallHelper.cs:18`](../src/Features/LayeredData/BasicOperations/CallHelper.cs#L18)
and [`OpenHierarchy.cs:290`](../src/Features/LayeredData/BasicOperations/OpenHierarchy.cs#L290).

For example, the current assertion is:

```csharp
Debug.Assert(merger.GetType().IsAssignableTo(typeof(IMerger<T>)));
```

Applying the chain layout while preserving its original diagnostic text would be:

```csharp
Debug.Assert(merger.GetType()
    .IsAssignableTo(typeof(IMerger<T>)),
    message: "merger.GetType().IsAssignableTo(typeof(IMerger<T>))");
```

This duplicates source text deliberately, so retaining the current assertion
layout with a narrow suppression is also a concrete review option. Neither
option is applied in this pass.

The nine interpolation cases are in `FreeHoursTests`; moving a chain across a
line can accidentally change the interpolation's braces or text. Keep each
expression inside the same interpolation hole and preserve the resulting
string. Representative: [`FreeHoursTests.cs:665`](../src/MainCli/Tests/FreeHoursTests.cs#L665).
A current interpolation hole is `day.GetProperty("day").GetString()` in the
`Day(JsonElement day)` renderer; a related hole does
`x.GetProperty("start").GetString()` for each interval. The proposed change
moves `.GetString()` onto its own line inside the existing hole, preserving
all surrounding literal text. These are formatting-sensitive source contexts;
assertion text and expected output must remain unchanged.

## CR1000 — three owned contracts and the reviewed reflection delegate

The three Google contracts are owned APIs with cohesive credential inputs to
review. A possible shape is a narrowly scoped `GoogleCredentialRequest` (for
credential, scopes, teacher, and service provider where those values truly
travel together); token-provider operations may need distinct
`GoogleTokenRequest` and `GoogleRefreshRequest` because authorization and
`IDataStore` have different roles. Do not force the same bag onto all methods.

Actual signatures:

- [`GoogleCredentialsConfig.cs:281`](../src/Features/LayeredConfig/BuilderNew/Impl/Google/GoogleCredentialsConfig.cs#L281):
  `Resolve(BuiltGoogleCredentialsConfig config, string[] scopes, string teacher, IServiceProvider services, CancellationToken cancellationToken)`
- [`GoogleAuthentication.cs:27`](../src/MainCli/GoogleAuthentication.cs#L27):
  `Refresh(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, CancellationToken token)`
- [`GoogleAuthentication.cs:34`](../src/MainCli/GoogleAuthentication.cs#L34):
  `CreateCredential(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, IDataStore store)`

`Consent` has only three data parameters and is accepted. `CancellationToken`
remains a trailing execution control, not a member of a
credential-data record. The fourth finding is a private delegate that mirrors
a nonpublic QuestPDF method obtained by reflection:

```csharp
private delegate IContainer BorderDelegate(
    IContainer element, float top = 0, float bottom = 0,
    float left = 0, float right = 0);
```

Its exact five-parameter shape is needed by `CreateDelegate<BorderDelegate>()`
([`GroupColumnScheduleTableDocumentGenerator.cs:493`](../src/ScheduleLib/ScheduleLib.Generator.Pdf/Generation/GroupColumn/GroupColumnScheduleTableDocumentGenerator.cs#L493)).
The implementation now marks this delegate with
`[ExternalSignature(typeof(BorderExtensions), "Border")]`. It no longer
counts as an owned contract finding. The marker names the external member;
it does not verify reflection binding or exempt other delegates.

## CR1300 — expected calendar JSON timestamps

One finding in [`CalendarSyncTests.cs:181`](../src/MainCli/Tests/CalendarSyncTests.cs#L181).
The expected `start` and `end` values are serialized with the same invariant
round-trip policy:

```csharp
start?.ToString("o", CultureInfo.InvariantCulture)
end?.ToString("o", CultureInfo.InvariantCulture)
```

A test-local `ExpectedCalendarTimestamp(DateTimeOffset? value)` helper is a
reasonable proposal because both assertions model the same JSON wire
representation, including null propagation. Keep this policy scoped to these
calendar expectations; it does not establish a universal timestamp formatter.
