# Fișe postului per title — deferred work

Status: paused on 2026-09-22 to fix CIM issues first.

## Source masters

New documents in `/home/anton/coding/titu/fise_new/` (do not move them):

- `10. Fișa_Asistent universitar actualizat 2026.docx` — content-identical to the
  already integrated `data/templates/fisa_postului_asistent_universitar.docx`
  (only the `{{placeholders}}` are stripped). No action needed.
- `11. Fisa_post_lector_actualizata 2026.docx` — NEW, 4 pages, code 231005.
- `12. Fisa_post_Conferentiar_USM_actualizata_2026.docx` — NEW, 4 pages, code 231004.
- `13. Fișa post profesor USM actualizată 2026.docx` — NEW, 4 pages, code 231007.

## Decisions already taken with the user

- Lector "Întocmită de / Funcția ___" line: leave blank for handwriting
  (we only store preparer name + department, not their function).
- Every packet gets the fișa matching the person's function (not assistant-only),
  printed twice in `print.pdf` (same as the current asistent fișă).

## Placeholders to add (mirror the asistent master convention)

Stamp tokens live in white, borderless, anchored floating text boxes
(`mc:AlternateContent` + `wp:anchor`, page-relative EMU offsets), NOT as inline
text — inline tokens would survive `strip_docx.py` and leak into backgrounds.
Clone the 7 blocks from `fisa_postului_asistent_universitar.docx`, replacing
anchor/edit/docPr ids, `posOffset` H/V, `extent`, font size, and token text
(once in Choice, once in Fallback). Anchor paragraph must sit on the target page.

Fields per new fișă (same names as asistent):

- p1 item 7: `Faculty`, `Department` (two underline rules).
- Last page "Întocmită de": `PreparedByName`, `PreparedByDepartment`
  (NOT for lector — `Funcția` stays manual), `DocumentDate` on the Data rule.
  Semnătura stays manual.
- Last page "Am luat cunoștință": `Name`, `DocumentDate`. Semnătura manual.
- "Vizată de" block: fully manual (preprinted name + blank rules).

Canonical names: `fisa_postului_lector_universitar.docx`,
`fisa_postului_conferentiar_universitar.docx`,
`fisa_postului_profesor_universitar.docx`.

## Integration checklist

1. Strip copies via `strip_docx.py` → `data/pdf-backgrounds/*.pdf`
   (backgrounds must be re-exported from Word if layout drifts).
2. Author `data/templates/*.json` manifests from the textbox geometry,
   then converge baselines with `/tmp/opencode/measure_v3.py`.
3. Visual review pass 1: render 4 sample persons (one per function), inspect
   crops of item 7 + signature blocks.
4. Visual review pass 2: re-render after fixes, confirm gaps ~0.9pt.
5. Code: `FilePaths` + `Program.cs` gain the three templates; `EmploymentDocs`
   selects the fișă by normalized function and always generates it
   (`PrintCopiesPerJobDescription = 2` already covers the extra copy).
6. Tests + ValidateSamples (add lector/conferențiar/profesor persons),
   commit, republish desktops.
