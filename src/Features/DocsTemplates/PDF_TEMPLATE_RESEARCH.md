# PDF template workflow research

Date: 2026-09-22

## Recommendation

Use PDFsharp 6.2.4 to stamp values onto fixed PDF pages. Keep the printable page as a blank PDF and keep field positions in a small sidecar file. Bundle Liberation Serif Italic and draw every generated value with the real italic face.

Do not make interactive PDF form values the final output. AcroForm fields are useful as authoring markers, but PDFsharp documents only limited AcroForm support, and an open PDFsharp issue shows filled fields that appear in Adobe Reader but remain invisible in Apple Preview and Sumatra because their appearance streams were not regenerated. Static page-content stamping avoids that viewer-dependent behavior. [PDFsharp FAQ](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html), [PDFsharp AcroForm appearance issue](https://github.com/empira/PDFsharp/issues/64)

This is a good fit for this project:

- PDFsharp is a .NET PDF processing library that can modify existing files and draw on their pages. [PDFsharp introduction](https://docs.pdfsharp.net/PDFsharp/Overview/Introduction.html)
- The stable 6.2.4 package is compatible with .NET 8, 9, 10 and .NET Standard 2.0, so the current `net11.0` project can consume a compatible asset. It has about 71 million NuGet downloads and uses the MIT license. [PDFsharp on NuGet](https://www.nuget.org/packages/PdfSharp/6.2.4), [PDFsharp license](https://github.com/empira/PDFsharp/blob/master/LICENSE)
- PDFsharp supports real italic font faces, font embedding, and font subsetting. Its production guidance is to register a custom font resolver rather than depend on a machine's installed fonts. [Drawing text](https://docs.pdfsharp.net/PDFsharp/Topics/Start/First-PDF.html), [font resolving](https://docs.pdfsharp.net/PDFsharp/Topics/Fonts/Font-Resolving.html), [font embedding](https://docs.pdfsharp.net/PDFsharp/Topics/Fonts/Font-Embedding.html)
- Liberation Serif includes an italic TTF, is metrically compatible with Times New Roman, and is licensed under SIL OFL 1.1. This gives the generator a redistributable font with the Romanian glyphs it needs. [Liberation Fonts repository](https://github.com/liberationfonts/liberation-fonts)

## What I found in this repository

The current `new-docs` branch points at commit `82d7c39` (`WIP`). `DocsTemplates` is a small .NET console application. Its only project dependency is the Excel helper, and its current renderer opens DOCX as ZIP files and replaces `{{Field}}` inside Word XML.

The real complexity lives in `tools/prepare_templates.ps1`. It uses Word COM to find text, calculate page coordinates, add floating white text boxes, set their dimensions, and insert each token twice through Word's compatibility markup. The seven PDF layouts now contain 85 logical placements:

| Template | Pages | Placements | Fields |
| --- | ---: | ---: | --- |
| Additional agreement | 2 | 31 | 11 distinct fields, repeated across two copies |
| Hire request | 1 | 10 | 10 |
| Individual employment contract | 3 | 22 | 19 |
| Consent declaration | 1 | 5 | 4 |
| Information declaration | 1 | 5 | 4 |
| Own-responsibility declaration | 1 | 5 | 5 |
| Assistant job description | 4 | 7 | 6 |

The local templates are ignored by Git, so commit `82d7c39` contains the preparation code but not the actual edited binary templates. The structural audit currently reports that the agreement, employment contract, consent declaration, and information declaration no longer have the exact static text expected by the preparation script. That matches the user's report that several templates were edited after the scripted preparation. The hire request and job description still match their incoming source text.

I exported the seven current templates through Microsoft Word without changing them. They retain the inventory's page counts of 2/1/3/1/1/1/4 and all pages are A4. Word reports overflow in many placeholder text boxes, including the agreement date fields, most hire-request fields, several contract fields, and both split date fields in the declarations. The exported PDFs contain one page content stream per page, so they do not trigger the current PDFsharp multi-stream modification bug described below.

## Proposed authoring and rendering workflow

1. Keep the edited Word documents only as blank masters for changing official wording and layout. Remove the generated token text boxes once, then export each master to PDF. Ordinary Word editing remains possible, but Word no longer participates in field placement or packet generation.
2. Add one placement manifest beside each PDF. A placement records the field name, page, rectangle, font size, alignment, and whether the original blank needs a white cover. Coordinates use PDF points, where 72 points equal one inch.
3. Add a proof mode. It draws every placeholder name in red italic text and outlines its rectangle. This proof PDF is what the user approves before the renderer is changed. It uses the same manifest as the final generator, so approval covers the actual placements.
4. At generation time, open the blank PDF, draw the resolved value into every rectangle in black Liberation Serif Italic, save a new PDF, then reopen and render it for automated checks. The source PDF is never overwritten.
5. Preserve the existing person model, field derivations, unit splitting, file names, and `print` / `not-print` routing. Only the last rendering step changes from DOCX token replacement to PDF stamping.

The source PDF itself remains clean. The sidecar is the editable placeholder layer. For example:

```json
{
  "template": "cerere_angajare_didactica.pdf",
  "font": { "family": "Liberation Serif", "style": "Italic" },
  "fields": [
    { "name": "Name", "page": 1, "x": 129.0, "y": 168.0, "width": 350.0, "height": 15.0, "size": 12, "align": "left" },
    { "name": "Function", "page": 1, "x": 184.0, "y": 197.0, "width": 132.0, "height": 14.0, "size": 12, "align": "center" },
    { "name": "DocumentDate", "page": 1, "x": 72.0, "y": 720.0, "width": 86.0, "height": 14.0, "size": 12, "align": "center" }
  ]
}
```

Those numbers illustrate the file shape, not approved coordinates. The first proof pass should derive rectangles from the current templates and show them on every page. A proof page would display `{{Name}}`, `{{Function}}`, and `{{DocumentDate}}` in italic red inside thin red boxes. The final page would use the same boxes but draw `Popescu Ana`, `asistent universitar`, and `01.09.2026` in black italic with no outlines.

If physical placeholders inside the PDF are preferred, LibreOffice can add named text controls and export a PDF form. [LibreOffice form controls](https://help.libreoffice.org/latest/en-GB/text/shared/02/01170000.html), [LibreOffice PDF form export](https://help.libreoffice.org/latest/en-US/text/shared/01/ref_pdf_export_general.html). I would use those fields only to supply names and rectangles. The renderer should then stamp static text and remove the widgets instead of trusting a PDF viewer to generate field appearances.

## Why not the other realistic options

### AcroForm filling with PDFsharp

It offers attractive in-PDF field names, but PDFsharp calls its own AcroForm support limited. Viewer-generated appearances are unreliable for unattended printing. It also adds flattening and widget cleanup work that fixed page stamping does not need.

### iText

iText has stronger form APIs, but its no-cost license is AGPL. Its own guidance says an application using iText at no cost must disclose the application's source under AGPL; otherwise a commercial license is required. That does not meet an unconditional free-library requirement. [iText license](https://github.com/itext/itext-dotnet/blob/develop/LICENSE.md), [iText licensing explanation](https://kb.itextpdf.com/itext/is-itext-free)

### QuestPDF

The repository already uses QuestPDF elsewhere, and QuestPDF now has document overlay operations. Its current community license is source-available rather than OSI open source, has a USD 1 million revenue threshold, and excludes some public-sector entities. It is not unconditionally free. [QuestPDF community license](https://www.questpdf.com/license/community.html), [QuestPDF document operations](https://www.questpdf.com/concepts/document-operations.html)

### Apache PDFBox

PDFBox is Apache-2.0 and has strong AcroForm support, including appearance refresh and flattening. It is a Java library, so adopting it would add a Java runtime or a separate service to this .NET console app. [Apache PDFBox](https://pdfbox.apache.org/), [PDFBox AcroForm implementation](https://github.com/apache/pdfbox/blob/trunk/pdfbox/src/main/java/org/apache/pdfbox/pdmodel/interactive/form/PDAcroForm.java)

## Risks and checks

- Stamping is fixed-position. Long values need explicit fit rules such as a minimum font size, clipping rejection, or a permitted second line. The proof should include both normal and long stress data.
- Re-exporting a blank master after static layout edits may move fields. The proof command should make changed geometry obvious, and generation should reject an unapproved template hash.
- PDFsharp does not lay out paragraphs automatically. That is acceptable here because these are fixed forms, but the long external-employment clause needs a rectangle with explicit wrapping and line spacing.
- PDFsharp 6.2.4 has an open issue when appending to some PDFs whose page `/Contents` is an array of multiple streams. The current Word exports use one stream per page, but this should remain a preflight check. If a future source triggers it, generate a transparent overlay PDF with PDFsharp and merge it using qpdf. qpdf is Apache-2.0 and supports overlay and underlay operations. [PDFsharp issue 345](https://github.com/empira/PDFsharp/issues/345), [qpdf repository](https://github.com/qpdf/qpdf), [qpdf overlay documentation](https://qpdf.readthedocs.io/en/stable/cli.html#overlay-and-underlay)
- Generated PDFs should be reopened, rendered to images, and compared for page count, page size, missing fields, overflow, and unexpected visual differences before the DOCX path is removed.

## Decision

Proceed with a proof-only spike using PDFsharp 6.2.4, blank PDF backgrounds, sidecar placements, and Liberation Serif Italic. Produce the seven red-box proof PDFs first and stop for visual approval. Only after those placements are approved should the DOCX renderer be replaced.
