# Group structure 2026–2027 — interpretation of the four source documents

Source files (copied into `docs/wayfinder/inputs/2026-2027/`):

| File | Content |
|---|---|
| `01-contingent_2026_2027_anonymized-1-.xlsx` | Full student roster: sheets `LicentaZi` (bachelor, day), `FR` (francophone track, years I–IV), `Master` (years I–II) |
| `01-optional_courses_year2_day_anonymized-1-.xlsx` | Year 2 optional courses: final distribution of students into courses + lab subgroups |
| `01-optional_courses_year3_day_anonymized-1-.xlsx` | Year 3 optional courses, same format |
| `01-optional_courses_FR_years_2_3_4_anonymized-1-.xlsx` | FR track years 2/3/4 optional courses, same format + per-student final lists |

All files are anonymized (names → "Person Name", emails → "Person Email", phones → "Person Phone"). The contingent workbook also has original email hyperlink targets, comments, and comment-author/person metadata removed; the underlying XLSX package was checked as well as the displayed cells. Cohort counts and course allocations are preserved.

## 1. Contingent (roster)

Each sheet is divided into blocks per study year; inside a block, one table per **academic group** with a header like `IA 2403 (ru) | 2 sg.` (group code, language of instruction, number of lab subgroups). Student rows carry: name/email (anonymized), buget/contract, enrollment order, **"limba engleza continuare"** (continuing foreign language: Engleza/Franceza/Germana), **Subgr.** (lab subgroup 1/2 if the group splits), **Libera alegere**, **Specializare**, **Modulul psiho-pedagogic** (11 "Da" in LicentaZi). Column positions of the last three shift per block (H/I vs I/J/K), so naive fixed-column extraction misses them.

Group codes seen (bachelor day):

- `G 2601` — year 0, BAC-prep (14 students, 1 subgroup).
- `M 26xx/25xx/24xx` — Matematică.
- `I 26xx/25xx/24xx` — Informatică (0613.4).
- `IA 26xx/25xx/24xx` — Informatică Aplicată (0613.5); `IA 2601 (DUAL)` and `DU 2501` = dual-education variants (`IA dual` also appears in the year-2 optional file as its own sheet with optionals 1–3).
- `DJ 26xx/25xx/24xx` — **Dezvoltarea Jocurilor** (Game Development) bachelor program (ro/ru/en year-1 groups). Note: "DJ" names *two* distinct real-world concepts — this specialty (program) and a `Specialization` value of the same name (confirmed by the user, 2026-09). Not a data error; see §4 gap 4.
- FR sheet: `IAFR`, `IȘEFR` (merged into IAFR: "Se duc ci IAFR 2601"), `MFR`.
- Master: `SMMSPA`, `PMS`, `MIA`, `IASD` (ro + ru groups).

Group sizes range 3–34. Groups marked `2 sg.` split into two lab subgroups (sizes ≈ 7–18 each); many groups do not split. Year totals (LicentaZi): year 0 ≈ 14, year I ≈ 256, year II ≈ 177, year III ≈ 176; FR ≈ 420; Master ≈ 150.

The `Libera alegere` values in LicentaZi: `Antrepr.` (73), `Psihol.` (71), `Int.Eur.` (17), `Cult.Com.` (15) — free-choice modules (Antreprenoriat, Psihologie, Integrare Europeană, Cultură Comunicării).

## 2. Optional courses ("blocs")

The three optional-course files describe, per study year, the **final distribution** of students into optional courses. Structure:

- A year has several **blocs** (sets). A student picks exactly one course per bloc → **all courses of a bloc must run in parallel** (same day+slot), otherwise a student would miss one. This is the scheduling semantics of a bloc.
- The `Sinteză` sheet is the composition summary, one row per (course, language):
  `Bloc | Disciplina | Limba | Componență finală | Total | Subgrupe lab. | Mărimi subgrupe`.
- **Componență finala** shows the merged population: e.g. `Iru 19 + IAru 28` = 19 students from group I + 28 from IA, Russian language. A bloc can merge students **across programs** (I + IA) — e.g. year 3 "Blocul 1 I + Blocul 2 IA" (the numbering of a bloc can differ per program).
- Every merged section then splits into **lab subgroups of ~13–21 students** (typical lab-room capacity ~16–20): e.g. 47 students → 3 subgroups `16+16+15`; 77 students → 5 subgroups `16+16+15+15+15`.
- Cota propusă vs Cota finală differ occasionally (12→14, 12→10), i.e. final numbers were corrected after distribution.
- Annotations: "fără IȘE" (excludes IȘE students), "corectat din 14", "28+13 indicat în cerință".
- One FR row combines two courses in one section: "Securitatea rețelelor / Realitate virtuală și augmentată" (17 students) — a merged/joint section.
- The FR file also has per-student sheets (Grupa academică | Limba de instruire | Disciplina finală | Subgrupa laborator) — the resolved choice per student.

Year 2 blocs: B1 {Grafică și animație 2D, Design UI/UX}, B2 {Dezvoltarea de aplicații enterprise, …}, B3 {Containerizare și virtualizare, …}, plus IA-dual optionals 1–3. Year 3 blocs: B1–B4 (B4 = "Sisteme de administrare a conținutului web", I+IA merged, 41 ro / 77 ru). FR: years 2/3/4 with blocs 1–3 (+ IȘE bloc year 3).

## 3. What this means for the engine

1. **The schedulable unit is a (course, language) section** — a merged population that can cross academic groups and programs, subdivided into lab subgroups of bounded size. "Keep composition exactly" = keep these Componență finală + subgroup splits as given in the Sinteză.
2. **Blocs are parallel-timeslot constraints**: all alternatives of one bloc (and all lab subgroups within each section) must land on the same day+slot; sections need parallel rooms (lab subgroups in separate lab rooms simultaneously).
3. **Subgroup sizes are the partition-capacity data** the room model needs: lab subgroups are capped at ~16–21 (computer labs), whole academic groups (8–34) and merged lectures (up to ~77) drive curs-room (large room) needs.
4. The **contingent is the upstream population model** (groups → languages → lab subgroups → per-student attributes); the **Sinteză sheets are the derived lesson-composition spec** the engine consumes. The per-student FR sheets show choices are resolved before scheduling.
5. Naming convention for academic groups: `<Program>[Track]<YY><NN>(<language>)`, e.g. `IA2503ru`, `IAfr2501ro` (francophone track, ro group code suffix), plus special variants DUAL/DU.

### Open questions (user to confirm)

- ~~What does `DJ` stand for?~~ **Answered:** Dezvoltarea Jocurilor; both a specialty (program) and a specialization carry the name (see §4 gap 4). Remaining: what are the Master program codes (SMMSPA, PMS, MIA, IASD)?
- Is the FR track's language of instruction actually French (the group suffix language says ro/ru), or is "fr" only a track marker?
- Do `Specializare` / `Libera alegere` / psiho-pedagogic module produce their own parallel sections (like optionals), and are those in the schedule documents too?
- Year 4 of the day (non-FR) bachelor is absent from the contingent — intentional?

## 4. Validity vs the current domain model (code check, feat/specializations-alternatives-domain-model @ f513629)

Verified against `src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs`, `GroupCombinations.cs`, `ImplicitSplitConfig.cs`, `src/ScheduleDefaults/Config.cs`:

**Model already matches reality:**

- **Multi-group lessons are supported**: `LessonData.Groups` is a `LessonGroups` collection (capacity 16, `IsSingleGroup` helper) — merged I+IA sections are expressible as-is.
- **Group language** is per-group metadata — matches the language suffix in group codes and the per-language section split.
- **Numeric lab subgroups** (`SubGroup`, roman numerals) match the 1–2 subgroups per academic group and the `Subgr.` column.
- **Specialization = optional-course track** is confirmed by the data: `Specialization.AllKnown` (GA2D, GA3D, React, UI, CV, SSI, DJ, Logica, …) are exactly the optional courses in the Sinteză sheets (Grafică și animație 2D/3D, Design UI/UX, React, Containerizare și virtualizare, Securitatea aplicațiilor web și mobile…).
- **Alternative = free-choice elective** matches the `Libera alegere` column: Antreprenoriat, Psihologie, Cult. comun. are registered (`ScheduleDefaults/Config.cs:88–93`); "Opț. ped." plausibly covers the psiho-pedagogic module (11 "Da").
- `ImplicitSplitConfig` (scopes inferring specialization/alternative when the source doc omits them) is validated by the data's "corectat din …" / per-program bloc renumbering messiness.

**Gaps the data exposes:**

1. **Section-level lab subgroups do not fit the SubGroup dimension.** A merged section re-partitions a cross-group population (47 → 16+16+15 spanning I *and* IA). `GroupPartitionKey` carries one `SubGroup` value per lesson applied to *every* targeted group, and partition state (`GroupPartitionInfo`) is per-group — a section's subgroup belongs to the section, not to the academic groups. The Word parser's `PossiblyRegisterSubGroup` would register those subgroup values onto the involved groups (ghost subgroups on groups that don't split). Works mechanically, wrong semantically; the engine should take section subgroups straight from the Sinteză (explicit sizes), not from group-scoped partition info.
2. **No first-class "bloc" (parallel alternative set).** Parallel electives exist only as overlap allowlist entries ("parallel alternatives sharing slots by design"). The engine needs explicit blocs (all courses of a bloc share day+slot); deriving them from per-grade specialization activation is possible but implicit.
3. **`Int.Eur.` (Integrare Europeană, 17 students) is not a registered Alternative** — only Antreprenoriat/Psihologie/Opț. ped./Cult. comun. are.
4. **"DJ" is both a program code and a `Specialization.AllKnown` value — RESOLVED (user, 2026-09):** the program is **Dezvoltarea Jocurilor** (Game Development), and a specialization of the same name also legitimately exists. Two distinct concepts sharing one name, so renaming is not the fix. The modeling direction: entities must reference each other by typed identity (group → specialty, lesson → specialization), never by string/regex name matching — all such regex searches are to be removed eventually; names are display-only. Any resolver that sees the string "DJ" must know which *kind* of thing it is looking for.

## 5. Room-capacity inference (stub values for the engine)

From the Sinteză lab-subgroup sizes (74 subgroups / 59 sections) and the contingent group sizes:

- **Lab rooms (computers):** subgroup sizes 1–23, median **15**, typical split cap **15–17**, max observed **23**; 84% of sections are a single subgroup. Stub: **24 seats** per lab room (typical usage 12–18). Psihologie / Limba străină "lab" rooms are the exception (no computers).
- **Academic-group rooms:** 31 bachelor day groups, 3–34 students (only 4 groups ≥ 30, 11 ≥ 25). Whole-group lessons need ≈ **36 seats**; stub **40**.
- **Merged-section lectures (curs):** section totals reach 30/41/47/77 → largest curs rooms stub **80 seats**, mid-size tier **50**.
- Proposed stub tiers for the engine until exact capacities arrive: lab = 24 (computer), curs small = 40, curs medium = 50, curs large = 80.
