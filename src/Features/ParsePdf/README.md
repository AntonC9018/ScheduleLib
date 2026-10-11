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
