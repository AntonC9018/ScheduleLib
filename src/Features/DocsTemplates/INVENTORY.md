# DocsTemplates 2026 modernization inventory

Inventory date: 2026-09-21

## Scope and privacy boundary

- Legacy source: `/home/anton/coding/titu/old_data`
- Incoming source: `/home/anton/coding/titu/new_data`
- Program: `src/Features/DocsTemplates`
- `4. cerere concurs.docx` is listed for completeness and otherwise excluded until its rules are known.
- No personal data rows from `docs_data.xlsx` were read. Only its worksheet name and header row were inspected, as permitted.

## Source files and intended mapping

| Incoming file | Size | Legacy counterpart | Saved page count | Initial disposition |
| --- | ---: | --- | ---: | --- |
| `1. acord suplimentar  de modificare 2026.docx` | 28,488 B | None | 2 | New generated document; contains two copies of the agreement |
| `3. Cerere angajare_didactica.docx` | 22,934 B | `angajare_didactica.docx` (35,990 B) | 1 | Replace legacy hire request |
| `4. cerere concurs.docx` | 23,746 B | None | — | Excluded from this modernization |
| `5. CIM didactica 2026 red.3.docx` | 33,045 B | `cim.docx` (64,773 B) | 3 | Replace legacy individual employment contract |
| `7. DECLARAȚIE DE CONSIMȚĂMÂNT.docx` | 17,170 B | `declaratie_consimtand.docx` (26,108 B) | 1 | Replace legacy consent declaration |
| `8. DECLARAȚIE DE INFORMARE.docx` | 16,703 B | None | 1 | New generated document |
| `9. declaratie proprie raspundere.pdf` | 265,533 B | `declaratie_proprie_raspundere.docx` (15,278 B) | 1 | Reconstructed from the matching editable legacy layout and checked against the PDF |
| `10. fișa_Asistent universitar actualizat 2026.docx` | 32,270 B | None | 4 | New generated document, apparently role-specific |

The incoming DOCX files contain no templating tags. The declaration is supplied only as PDF, while the current assembler requires DOCX.

## Document structure and layout

| Document | Page size | Main layout features | Consequence for templating |
| --- | --- | --- | --- |
| Agreement | A4 | Four tables and six VML line shapes; the full form is duplicated as two copies | Apply every generated field to both copies |
| Hire request | A4 | One 6×2 table; ordinary inline blank runs | Use inline fields and table cells; legacy floating text boxes are unnecessary |
| CIM | A4 after modernization | One 5×2 remuneration table and one 2×2 identity table | Incoming Letter layout is normalized to A4 |
| Consent | A4 | Plain paragraphs and inline blanks | Use inline fields |
| Information notice | A4 | Plain paragraphs and inline blanks | Use inline fields |
| Assistant job description | A4 | One 1×2 table and one image/logo | Preserve the logo and use inline/table fields |
| Own-responsibility declaration | A4 PDF | Same five business blanks as the old DOCX | Requires an editable DOCX source or a deliberate reconstruction/conversion |

None of the DOCX files has a header or footer part, tracked changes, or existing content controls. The legacy hire, CIM, and consent templates placed fields in VML text boxes; the incoming layouts make normal inline/table alignment practical and more stable.

Word for Windows is installed at `C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE`. Final generated samples must be opened/rendered with Word, checked page by page, and adjusted until inserted values fit without unwanted wrapping, overlap, clipping, or page-count changes. LibreOffice is not an acceptable substitute for this final check.

## Legacy program behavior at inventory time

`Program.cs` recognizes exactly four normalized template/output names:

- `declaratie_proprie_raspundere.docx`
- `angajare_didactica.docx`
- `declaratie_consimtand.docx`
- `cim.docx`

For each person, the generator deletes and recreates `output/{LastName}_{FirstName}`, emits the four documents, then opens the output directory in Explorer. Agreement, information notice, and job description have no program path yet.

The current input columns are:

`FirstName`, `LastName`, `Function`, `Faculty`, `Department`, `Date`, `Units`, `HireType`, `WorkingPlace`, `WorkingMode`, `ContractEndDate`, `ProbationPeriodEndDate`, `HomeAddress`, `PhoneNumber`, `Email`, `BISeries`, `IDIssueDate`, `PersonalIdentifier`.

Romanian aliases are supported in code. All 18 columns are currently required, although the two end-date values may be blank.

Current derived values:

- Full name is `LastName FirstName`.
- The hire period starts on 1 September of `Date.Year`.
- The hire period ends on 5 July of the following year for external cumulation, otherwise 31 August.
- Consent splits `BISeries` into its letter prefix and numeric remainder.
- Dates normally use `dd.MM.yyyy`; legacy consent uses `dd.MM` plus a separate two-digit year.

## Legacy repeating-document behavior at inventory time

Only the hire request and CIM repeat today:

- `Units <= 1`: one hire request and one CIM.
- `Units > 1`: first pair receives `1.00` and the requested hire type; second pair receives the remainder and `CumulIntern`.
- Repeated output names receive numeric suffixes `1` and `2`.
- Declarations are emitted once per person.

There is a validation defect: after subtracting the first unit, the code rejects only a remainder above `2`, so an original value from `2.01` through `3.00` is accepted despite the stated maximum of `2`. Zero and negative units are also accepted. The modernization should preserve the intended repetition rule while defining the valid range explicitly.

## Field coverage by incoming document

### Covered by the present model or current derivation

- Hire request: name, function, faculty, department, academic year, hire mode, units, start date, end date, document date.
- Consent and information notice: name, IDNP, document date.
- Own-responsibility declaration: name, function, department, faculty, document date.
- CIM identity/contact: name, home address, phone, identity series/number, issue date, IDNP, email.
- CIM employment basics: function, units, hire mode, contract dates, probation end date, faculty/department where semantics are confirmed.
- Assistant job description: employee name, faculty, department, document date.

The new hire form says `angajare de bază / cumul intern / cumul extern`; the current enum renders `titular / cumul intern / cumul extern / contract`. The correct mapping for `Titular` and `Contract` is a policy decision.

### Not represented in `PersonInfo` or its spreadsheet columns

Potentially generated values visible in the incoming forms:

- Contract number and contract signing date.
- Occupation code, unless derived from the function.
- Workplace/subdivision street or a precise rule for combining faculty and department.
- External-cumulation base function and base employer.
- Probation length and probation start date; only the end date exists today.
- Weekly working hours, unless derived from units.
- Specific work risks.
- Base salary.
- Fixed monthly supplement.
- Scientific or scientific-didactic title supplement.
- Honorary-title supplement.
- Agreement number and date.
- Original CIM number and date referenced by an agreement.
- Contract point changed by an agreement.
- Agreement effective date.
- Agreement generation eligibility.
- Scientific-didactic title, scientific title, and work-tenure date/years/months on the hire form.
- Job-description eligibility by function.
- Job-description preparer and department-head names, if these should be generated.

The old CIM model also exposes eight values that are hardcoded to empty strings: `Risks`, `AdditionalRights`, `AdditionalObligations`, `WorkRegime`, `RestRegime`, `AnnualVacationDuration`, `AdditionalAnnualVacationDuration`, and `SpecialClauses`. The new CIM removes the free-text slots for most of these, fixes annual leave at 62 calendar days, and still visibly requests specific risks.

Signature, approval, receipt, and HR visa lines appear intended for manual completion unless explicitly brought into the model.

## Content changes with field implications

- Rector references change to Otilia Dandara.
- The consent and information forms cite Law 195/2024. The new consent no longer asks for identity-series/type fields in its body.
- The new hire form hardcodes academic year 2026–2027 and a 2026 start date; legacy behavior derives these from input and should replace the hardcoded values if the rule remains annual.
- The CIM grows from two pages to three A4 pages and adds occupation, external-cumulation, remuneration, and weekly-hours blanks.
- The parenthetical employment-basis choice about a competition result or the study-year period remains a manual completion field while competition rules are excluded.
- The agreement contains two copies intended to carry identical person/contract data.
- The assistant job description is written specifically for `Asistent universitar/Asistentă universitară`.

## Dependencies and publish artifacts

| Artifact shown in the old distribution | Provenance | Current assessment |
| --- | --- | --- |
| `QuestPdfSkia.dll` | QuestPDF 2024.12.0 | Obsolete for DocsTemplates after the PDF generator was split out |
| `qpdf.dll` | QuestPDF native runtime | Obsolete for DocsTemplates |
| `libgcc_s_seh-1.dll` | QuestPDF/qpdf MinGW runtime | Obsolete for DocsTemplates |
| `libstdc++-6.dll` | QuestPDF/qpdf MinGW runtime | Obsolete for DocsTemplates |
| `libwinpthread-1.dll` | QuestPDF/qpdf MinGW runtime | Obsolete for DocsTemplates |
| `libSkiaSharp.dll` | `OpenXmlPowerTools.Core` → SkiaSharp | Required by the current assembler package; removing it requires replacing OpenXmlPowerTools or proving a narrower package path |
| `DocsTemplates.pdb` | This project’s portable debug symbols | Runtime-optional; publish can suppress it at the cost of poorer source-line diagnostics |

Commit `0885000` moved QuestPDF use into `ScheduleLib.Generator.Pdf`; current DocsTemplates has no reference path to that project. A clean Windows publish should prove the five QuestPDF/qpdf DLLs are gone. QuestPDF must remain for the separate schedule-PDF feature.

The direct `ScheduleLib.Core` project reference appears unused by DocsTemplates and can likely be removed. `ScheduleLib.Helper.Excel`, `OpenXmlPowerTools.Core`, UserSecrets, and the configuration binder are currently used. Replacing `OpenXmlPowerTools.Core` with a focused Open XML templating implementation is the route to eliminating `libSkiaSharp.dll`.

## Repository integration

- `data/.gitignore` already ignores `docs_data.xlsx` and `templates/*.docx`.
- It does not ignore `templates/*.pdf` or generated `output/`.
- `Directory.Build.targets` copies files under a project’s `data/` directory into build output, including ignored templates that exist locally.
- The incoming files still need normalized runtime filenames and entries in `FilePaths` and the generation pipeline.
- No DocsTemplates tests, ADRs, or project-specific `AGENTS.md` currently exist. Resolved vocabulary is recorded in the repository `CONTEXT.md`.

## Decisions settled during design

- Contract numbers remain blank; the program does not import or generate them.
- One spreadsheet document-date field is retained. It defaults to 1 September of the current year and supplies document signing/issue dates; identity-card issuance and employment-period end dates remain distinct.
- Workplace street/address becomes a spreadsheet field. Its default is `Str. Alexei Mateevici, nr. 60, biroul 225, blocul IV, MD-2009, Chișinău`, from the official FMI contact page.
- Employee function continues to come from the spreadsheet.
- Supported function values are `asistent universitar`, `lector universitar`, `conferențiar universitar`, and `profesor universitar`.
- Occupation codes come from CORM 006-2021 and are derived from the supported function: assistant `231001`, associate professor/conferențiar `231004`, lecturer `231005`, and professor `231007`.
- `PrimaryFunction` and `PrimaryEmployer` become person fields required only for external cumulation. For internal cumulation, the primary employer is Universitatea de Stat din Moldova and the CIM's external-employment sentence remains blank.
- The internal-cumulation employment period starts on 1 September of the document year and ends on 4 July of the following year.
- Probation, position-specific risks, weekly working hours, and all four remuneration values remain manual completion fields.
- Scientific-didactic title, scientific title, and tenure remain manual HR-completion fields.
- Faculty and department abbreviations will be separate spreadsheet fields; the documents that use full versus abbreviated values remain to be settled.
- Full faculty and department names are used in the hire request, declarations, and job description. Separate abbreviated values are used in the compact CIM workplace line, falling back to full names when blank.
- The job-description `Întocmită de` values are trailing person columns, defaulting to `Capcelea Titu`, `Informatica`, and the shared 1 September date. There is no separate configuration sheet; users copy the previous person row to retain defaults.
- The assistant job description is generated only when the normalized function is exactly `asistent universitar`.
- Consent, information notice, and own-responsibility declaration are generated for every supported academic function.
- The agreement is generated for every person. Known identity, contact, and date fields are filled; agreement number, contract number, changed point, remuneration, and signatures remain blank.
- Each person folder contains `print/` and `not-print/` subfolders.
- Document routing to `print/` and `not-print/` is fixed in code, not controlled by spreadsheet fields.
- The additional agreement is generated under `not-print/`. The hire request, CIM, consent declaration, information declaration, own-responsibility declaration, and assistant job description are generated under `print/` when applicable.
- `WorkingMode` and `ProbationPeriodEndDate` are removed from the model and generated workbook. No backward-compatible parsing is required.
- The old `WorkingPlace` field is removed. The USM workplace is derived from faculty, department, their abbreviations, and `WorkplaceAddress`; `PrimaryFunction` and `PrimaryEmployer` describe external primary employment.
- The schema may break freely; old workbooks do not need to remain readable.
- Confirmed period rules: `Titular` and `Contract` render as `de bază` and run from 1 September through 31 August of the following year. `CumulIntern` and `CumulExtern` run from 1 September through 4 July of the following year.
- Units must satisfy `0 < Units <= 2.00`; values above 1.00 retain the established two-document split.
- The own-responsibility declaration will be reconstructed as DOCX and must reproduce the supplied PDF when rendered by Word.
- The new CIM must use A4 paper.
- Portable PDB output may remain. Obsolete QuestPDF/qpdf native DLLs and `libSkiaSharp.dll` must disappear from the DocsTemplates distribution; the latter is removed by replacing OpenXmlPowerTools with focused token substitution.
- Generated values must follow the legacy template mechanism: place each value in a fixed-position, white, borderless Word text box over the existing blank or underlined form area. Never replace or rewrite the surrounding source paragraph to add a field. The original wording, underscores, line breaks, spacing, and paragraph geometry remain intact, and generated values must not cause document text to reflow. This rule applies to every template.

## Implemented result

- The program generates the agreement, hire request, CIM, consent declaration, information declaration, own-responsibility declaration, and assistant job description. Competition documents remain excluded.
- Every person packet contains `print/` and `not-print/`; the agreement is routed to `not-print/`, with the other applicable documents under `print/`.
- The spreadsheet schema contains 21 explicit columns. Removed fields are `WorkingPlace`, `WorkingMode`, `ContractEndDate`, and `ProbationPeriodEndDate`; added fields cover the shared document date, abbreviated subdivisions, workplace address, external primary employment, and job-description preparer.
- Function values are enforced against the four-value allowlist and mapped to CORM occupation codes.
- Unit values are enforced as `0 < Units <= 2.00`; a value above one generates two hire/CIM pairs, with the remainder rendered as internal cumulation.
- All seven DOCX outputs were rendered with Microsoft Word using both default data and long-field stress data. Page counts remain 2/1/3/1/1/1/4 respectively, all on A4, with no clipping or extra pages.
- The reconstructed blank own-responsibility DOCX visually matches the supplied PDF when rendered by Microsoft Word; a 200 DPI raster comparison produced SSIM 0.970776, with the only page-geometry difference being one raster pixel in width.
- A clean self-contained Windows single-file publish contains the executable, PDB files, and private templates. It contains none of `QuestPdfSkia.dll`, `qpdf.dll`, the three MinGW runtime DLLs, or `libSkiaSharp.dll`.
