# PDF template placement proofs

This tool draws each field rectangle and its name over a clean PDF export. It does not change the production document generator.

The layouts live in `data/templates` next to the Word masters. Coordinates use PDF points from the top-left corner of the page. Set `cover` to `true` when the renderer must hide existing fixed text before drawing a value.

## Generate the proofs

1. Export clean PDF backgrounds from the edited Word masters:

   ```powershell
   .\tools\export_pdf_template_backgrounds.ps1
   ```

2. Run the proof renderer with an italic Liberation Serif TTF:

   ```text
    dotnet run --project tools/PdfTemplateProof -- \
      tmp/pdfs/backgrounds \
      data/templates \
      output/pdf/proofs \
      /usr/share/fonts/truetype/liberation/LiberationSerif-Italic.ttf
   ```

The red outline is the exact placement rectangle. The red label uses the same real italic font intended for generated values. A narrow field can set `proofLabel` to a representative short value, such as `26` for a two-digit year, so the proof stays legible without changing the renderer's field name.
