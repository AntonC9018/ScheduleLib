# PDF schedule importer

A C# library that imports FMI bachelor timetable PDFs into `ScheduleImportContext`.
It references Core and PdfPig 0.1.16. No Python, subprocess, network access or
intermediate JSON file is needed.

## Import stages

1. `PdfTableReader` reads paths and glyphs into `PdfBorder`, `PdfBox`,
   `PdfTableCell` and `PdfScheduleTable`. Coordinates are PDF points measured from
   the top left. Nearby borders are snapped and joined before rectangular cells
   are found. Partial vertical borders split shared upper regions from distinct
   lower regions. Glyph midpoints assign text to one cell. Rotated day labels
   and expanded character spacing are handled here.
2. `PdfScheduleImporter.Resolve` carries normalized group-column centers across
   continuation pages. Merged cells retain all covered groups, even when a
   continuation page omits a vertical border. Day and time come from encompassing
   cells; actual start times take precedence over Roman row numbers.
3. `PdfTextNormalizer` repairs PDF wrapping and known source defects, expands
   explicitly named entrepreneurship cohorts, and applies the three documented
   missing-parity repairs. `PdfScheduleCell` preserves raw text, source, page,
   bounding box and repair/cohort annotations alongside normalized lesson text.
4. `PdfScheduleImporter.Import` feeds the existing lesson parser and context.
   It resolves full academic-group prefixes and reports parsing failures with
   source coordinates. The optional lesson handler supports the FMI cohort
   projection without coupling the PDF library to ScheduleDefaults.

For ordinary imports:

```csharp
using var pdf = File.OpenRead(path);
PdfScheduleImporter.Import(context, pdf, Path.GetFileName(path));
```

For document-wide preprocessing, call `Read` first, inspect the returned cells,
then call `Import(context, cells, handler)`. Table geometry is separately available
through `PdfTableReader.Read`. JSON exports are diagnostics, never pipeline inputs.

## Shared context

`ScheduleImportContext` lives in `ScheduleLib.Parsing` in Core, replacing
`DocParseContext` and its Word-specific namespace. Word, Excel, initialization
and tests use the renamed type. Word and PDF share `ParseLessons` and
`AddOrMergeLesson`, including course/teacher/room resolution, period assignment,
explicit start-time overrides, subgroup classification and lesson merging.

The PDF/Word path carries one `ImportedLessonInput` readonly record struct from
`ScheduleLib.Parsing`: parsed lesson, day, time slot, group IDs and typed
`Alternative` (default means unrestricted). `ImportedLessonHandler`,
`ElectiveCohorts.Add` and `ScheduleImportContext.AddOrMergeLesson` consume that
same type. Cohort expansion uses record copies to adjust groups and alternative;
the merge operation still owns explicit group/time overrides and merge policy.
Excel retains its separate `AddLessonParams` ref/span/date contract.

This implements [#242](https://github.com/AntonC9018/ScheduleLib/issues/242),
following the accepted Q20 decision in the
[CodingRules follow-up](https://github.com/AntonC9018/CodingRules/blob/main/docs/schedulelib-follow-up.md#overload-scope-and-usage-context-q19q20).
The maintainer identified the reviewed import/plumbing source at `4c26748` as
Astra-generated; owner attribution in Git is not evidence of a human-style
precedent. The record groups structured import values without rendering them.

## Source-specific limits

Only the regular daytime bachelor table format is supported. Master, part-time
and exam timetables have different layouts.

The parity repairs follow `docs/wayfinder/2026-sem1-clash-fixes.md` on
`feat/domain-model-group-structure`. They match group, weekday, start time, course
and teacher; geometric half-rows alone do not imply parity. The current I2502
lab names Cr. Ulmanu rather than the older document's D. Semeniuc.

Entrepreneurship Gr.1/Gr.2 membership is preserved as an annotation. The FMI
runner temporarily projects those teaching sections onto elective alternatives,
with shared lectures copied into both choices. Numeric laboratory subgroups and
specializations remain independent. This is still limited by the current scalar
alternative model; it does not implement the domain branch's future choice blocs.

See [the FMI runner](../FmiScheduleImport/README.md) for website discovery,
hash checkpoints, automatic period selection and document generation.

## Verification

`dotnet test src/Features/Tests/ParsePdf` checks all 493 reviewed cells from the
three September 21 source fixtures, including exact lesson text, groups, days,
times and cohort membership. It also checks partial borders, normalization,
parity repair, discovery, date selection, unchanged downloads and failed-update
retries. Parser and DOCX regression suites cover the shared-context move.

### Shared-record refactor (#242, 2026-10-18)

Focused validation passed 4 `ScheduleCellParserTests` and 12
`PdfLessonImportTests`/`ElectiveCohortTests`. These cover group merging, distinct
alternatives, explicit group/time overrides, cohort expansion and fallback,
course routing, optional callbacks, and wrapped versus unwrapped failures.

The broader suites ran sequentially and passed:

- `Parser.Tests`: 208 passed, 3 existing skips.
- `ParsePdf.Tests`: 29 passed.
- `ScheduleFromDoc.Tests`: 24 passed, 1 existing cache-regeneration skip.
- `BuilderNewTests.Tests`: 18 passed.

Run from the repository root. The build commands retain the existing CR warning
exceptions and add command-scoped NU1902/NU1903 exceptions for the existing
transitive Cryptography.Pkcs/Xml 10.0.10 advisories. Project warning policy and
dependencies are unchanged. `DiffEngine_Disabled` prevents interactive snapshot
diff viewers:

```sh
schedulelib_test_warnings=$(python3 -c 'import xml.etree.ElementTree as E; print((E.parse("Directory.Build.props").find(".//AntonCodingRulesDiagnosticIds").text + ";NU1902;NU1903").replace(";", "%3B"))')
DiffEngine_Disabled=true dotnet test src/ScheduleLib/Tests/Parser/Parser.Tests.csproj --filter FullyQualifiedName~ScheduleCellParserTests -m:1 --verbosity quiet "-p:WarningsNotAsErrors=$schedulelib_test_warnings"
DiffEngine_Disabled=true dotnet test src/Features/Tests/ParsePdf/ParsePdf.Tests.csproj --filter 'FullyQualifiedName~PdfLessonImportTests|FullyQualifiedName~ElectiveCohortTests' -m:1 --verbosity quiet "-p:WarningsNotAsErrors=$schedulelib_test_warnings"
for schedulelib_test_project in \
    src/ScheduleLib/Tests/Parser/Parser.Tests.csproj \
    src/Features/Tests/ParsePdf/ParsePdf.Tests.csproj \
    src/ScheduleLib/Tests/ScheduleFromDoc/ScheduleFromDoc.Tests.csproj \
    src/Features/Tests/BuilderNewTests/BuilderNewTests.Tests.csproj
do
    DiffEngine_Disabled=true dotnet test "$schedulelib_test_project" -m:1 --verbosity quiet "-p:WarningsNotAsErrors=$schedulelib_test_warnings" || break
done
```

The formatter completed on the new record and changed cohort/test files;
`git diff --check` passed. The unrelated registry-history CLI failure documented
in [calendar reporting](../../../docs/calendar-reporting.md#verification-2026-10-18)
was outside these suites. Its logger and test files remain identical to the
approved `4c26748` snapshot. No solution-wide, package, live-provider or CI checks
were run for this refactor.
