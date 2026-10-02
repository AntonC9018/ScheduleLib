# Group structure 2026–2027 — interpretation of the four source documents

Source files (copied into `docs/wayfinder/inputs/2026-2027/`):

| File | Content |
|---|---|
| `01-contingent_2026_2027_anonymized-1-.xlsx` | Full student roster: sheets `LicentaZi` (bachelor, day), `FR` (frecvența redusă / part-time, years I–IV), `Master` (years I–II) |
| `01-optional_courses_year2_day_anonymized-1-.xlsx` | Year 2 optional courses: final distribution of students into courses + lab subgroups |
| `01-optional_courses_year3_day_anonymized-1-.xlsx` | Year 3 optional courses, same format |
| `01-optional_courses_FR_years_2_3_4_anonymized-1-.xlsx` | frecvența redusă years 2/3/4 optional courses, same format + per-student final lists |

All files are anonymized (names → "Person Name", emails → "Person Email", phones → "Person Phone"). The contingent workbook also has original email hyperlink targets, comments, and comment-author/person metadata removed; the underlying XLSX package was checked as well as the displayed cells. Cohort counts and course allocations are preserved.

## 1. Contingent (roster)

Each sheet is divided into blocks per study year; inside a block, one table per **academic group** with a header like `IA 2403 (ru) | 2 sg.` (group code, language of instruction, number of lab subgroups). Student rows carry: name/email (anonymized), buget/contract, enrollment order, **"limba engleza continuare"** (continuing foreign language: Engleza/Franceza/Germana), **Subgr.** (lab subgroup, Arabic `1`/`2`, if the group splits), **Libera alegere** (empty for every student — see below), **Specializare** (empty for every student), **Modulul psiho-pedagogic** (11 "Da" in LicentaZi). Column positions of the attribute columns shift per block — a `Umanistic (sem. III)` column appears at H in most blocks, and `Specializare`/`Libera alegere` move between I/J/K — so naive fixed-column extraction misses them.

Group codes seen (bachelor day):

- `G 2601` — year 0, BAC-prep (14 students, 1 subgroup).
- `M 26xx/25xx/24xx` — Matematică.
- `I 26xx/25xx/24xx` — Informatică (0613.4).
- `IA 26xx/25xx/24xx` — Informatică Aplicată (0613.5); `IA 2601 (DUAL)` and `DU 2501` = dual-education variants (`IA dual` also appears in the year-2 optional file as its own sheet with optionals 1–3).
- `DJ 26xx/25xx/24xx` — **Designul Jocurilor** (game development) bachelor program (ro/ru/en year-1 groups), spelled "Designul Jocurilor" in the contingent's program titles. Note: "DJ" names *two* distinct real-world concepts — this specialty (program) and a `Specialization` value of the same code (confirmed by the user, 2026-09). Not a data error; see §4 gap 4.
- FR sheet: `IAFR`, `IȘEFR` (merged into IAFR: "Se duc ci IAFR 2601"), `MFR`. **FR = Frecvența Redusă (part-time), not a francophone track** — confirmed by the user (2026-09), by the FR Sinteză title ("licență frecvență redusă"), by Română/Rusă `Limba de instruire` values, and by the code: `GroupParser` maps an "FR" infix to `AttendanceMode.FrecventaRedusa` (faculty "IA" for `IAFR 2601`).
- Master: `SMMSPA`, `PMS`, `MIA`, `IASD` (ro + ru groups). In-list markers: `M`/`MA`/`MȘE`/`IȘE` track markers in LicentaZi column B; `ru:` markers in Master blocks where Russian-medium students start.

Group sizes range 3–34. Groups marked `2 sg.` split into two lab subgroups (sizes ≈ 7–18 each); many groups do not split. Per-student `Subgr.` values are Arabic `1`/`2`; one FR group (`IAFR 2602 (ro)`) has a `3`. `DJ 2501` annotates `2 sg. (I-ro, II-en)` — its two subgroups align with language of instruction. Year totals (verified 2026-09): year 0 = 14, year I = 256 numbered (41 empty reservation slots, 215 named), year II = 177, year III = 176; FR = 325 students (the earlier ≈420 counted empty reservation rows); Master = 132 numbered (117 named; the earlier ≈150 counted structural rows).

The free-choice module values in LicentaZi sit under the **`Umanistic (sem. III)`** column (year-2 blocks only; the `Libera alegere` column is empty for every student): `Antrepr.` (73), `Psihol.` (71), `Int.Eur.` (17), `Cult.Com.` (15) — Antreprenoriat, Psihologie, Integrare Europeană, Cultură Comunicării. Correction (2026-09): the psiho-pedagogic module is **not** mutually exclusive with the module slot — 7 of the 11 "Da" students (all year 2) also carry a module choice.

## 2. Optional courses ("blocs")

The three optional-course files describe, per study year, the **final distribution** of students into optional courses. Structure:

- A curriculum **choice bloc** is a semester-scoped package of options from which a student selects exactly one. The selected course becomes compulsory for that student. A bloc does **not** imply a common timeslot; alternatives have disjoint audiences and may overlap, but any required synchronization is a separate scheduling constraint.
- The `Sinteză` sheet is the composition summary, one row per (course, language):
  `Bloc | Disciplina | Limba | Componență finală | Total | Subgrupe lab. | Mărimi subgrupe`.
- **Componență finala** shows the merged population: e.g. `Iru 19 + IAru 28` = 19 students from group I + 28 from IA, Russian language. The resulting **teaching section** can merge students across programs. The label `Blocul 1 I + Blocul 2 IA` maps that one opened section to two distinct, program-specific choice blocs; it does not merge the curriculum blocs themselves.
- Every merged section then splits into **lab subgroups of ~13–21 students** (typical lab-room capacity ~16–20): e.g. 47 students → 3 subgroups `16+16+15`; 77 students → 5 subgroups `16+16+15+15+15`.
- Cota propusă vs Cota finală differ occasionally (12→14, 12→10), i.e. final numbers were corrected after distribution.
- Annotations: "fără IȘE" (excludes IȘE students), "corectat din 14", "28+13 indicat în cerință".
- One FR row combines two courses in one section: "Securitatea rețelelor / Realitate virtuală și augmentată" (17 students) — a merged/joint section.
- The FR file also has per-student sheets (Grupa academică | Limba de instruire | Disciplina finală | Subgrupa laborator) — the resolved choice per student.

Year 2 blocs: B1 {Grafică și animație 2D, Design UI/UX}, B2 {Dezvoltarea de aplicații enterprise, …}, B3 {Containerizare și virtualizare, …}, plus IA-dual optionals 1–3. The complete 2024 IA year-3 curriculum has three choice blocs in semester V and one in semester VI. The workbook numbers the latter continuously as `B4`, although its canonical curriculum ordinal is semester-VI bloc 1, and lists only options for which sections were opened; absent Client-side and Blockchain rows are not absent curriculum options. See [the official-website verification](year3-curriculum-website-research.md) for the complete IA and I catalogues. FR: years 2/3/4 with blocs 1–3 (+ IȘE bloc year 3). Coverage gaps (2026-09): the day year-2/3 files cover only I/IA groups (DJ, DU, M absent); the FR file covers IA+IȘE only (MFR absent); Master has no optional-course file at all.

## 3. What this means for the engine

1. **The schedulable unit is a (course, language) teaching section** — a merged population that can cross academic groups and programs, subdivided into lab subgroups of bounded size. "Keep composition exactly" = keep these Componență finală + subgroup splits as given in the Sinteză.
2. **Blocs are audience-choice dimensions, not timeslot constraints.** Different options in one bloc have mutually exclusive student audiences and may overlap. The official timetable does not align them universally, and laboratory subgroups of a section may also run at different times. Encode a same-slot requirement only when another source states it explicitly.
3. **Subgroup sizes are the partition-capacity data** the room model needs: lab subgroups are capped at ~16–21 (computer labs), whole academic groups (8–34) and merged lectures (up to ~77) drive curs-room (large room) needs.
4. The **contingent is the upstream population model** (groups → languages → lab subgroups → per-student attributes); the curriculum is the complete choice catalogue; and the **Sinteză sheets report the opened teaching sections**, not every option in that catalogue. The per-student FR sheets show choices are resolved before scheduling.
5. Naming convention for academic groups: `<Program>[Track]<YY><NN>(<language>)`, e.g. `IA2503ru`, `IAfr2501ro` (`FR` = frecvența redusă marker, `ro` language suffix), plus special variants DUAL/DU.

### Open questions (user to confirm)

- ~~What does `DJ` stand for?~~ **Answered (corrected 2026-09-05):** Designul Jocurilor (provisional); both a specialty (program) and a specialization carry the name (see §4 gap 4). The specialization's exact display name is unconfirmed — the contingent titles the program "Designul Jocurilor". Remaining: what are the Master program codes (SMMSPA, PMS, MIA, IASD)?
- ~~Is the FR track's language of instruction actually French (the group suffix language says ro/ru), or is "fr" only a track marker?~~ **Answered:** FR = Frecvența Redusă (user, 2026-09); languages of instruction are Română/Rusă.
- Do `Specializare` / `Libera alegere` / psiho-pedagogic module produce their own independent choice dimensions, and are their opened teaching sections present in the schedule documents?
- ~~Year 4 of the day (non-FR) bachelor is absent from the contingent — intentional?~~ **Answered:** structural — day licență is a 3-year program (Anul 0 + I–III); FR spans I–IV.

## 4. Validity vs the current domain model (code check, feat/specializations-alternatives-domain-model @ f513629)

Verified against `src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs`, `GroupCombinations.cs`, `ImplicitSplitConfig.cs`, `src/ScheduleDefaults/Config.cs`:

**Parts of the model that match reality:**

- **Multi-group lessons are supported**: `LessonData.Groups` is a `LessonGroups` collection (capacity 16, `IsSingleGroup` helper) — merged I+IA sections are expressible as-is.
- **Group language** is per-group metadata — matches the language suffix in group codes and the per-language section split.
- **Numeric lab subgroups** (`SubGroup`, roman numerals) match the 1–2 subgroups per academic group and the `Subgr.` column.
- **Specialization** can represent a genuine study-profile partition, but the optional-course names currently registered as specializations do not prove that a choice bloc is a specialization. Year 3 has several independent blocs, and some options are eligible for multiple profiles.
- **Alternative** provides one generic scalar choice axis. It can approximate one optional dimension, but it cannot identify which curriculum bloc an option belongs to or represent simultaneous choices from several blocs. The humanistic and psiho-pedagogic memberships also show that one scalar axis is insufficient.
- `ImplicitSplitConfig` can bridge missing annotations in source schedules, but the current year-3 course-to-specialization mappings collapse distinct curriculum choices into one axis and are not a faithful bloc model.

**Gaps the data exposes:**

1. **Section-level lab subgroups do not fit the SubGroup dimension.** A merged section re-partitions a cross-group population (47 → 16+16+15 spanning I *and* IA). `GroupPartitionKey` carries one `SubGroup` value per lesson applied to *every* targeted group, and partition state (`GroupPartitionInfo`) is per-group — a section's subgroup belongs to the section, not to the academic groups. The Word parser's `PossiblyRegisterSubGroup` would register those subgroup values onto the involved groups (ghost subgroups on groups that don't split). Works mechanically, wrong semantically; the engine should take section subgroups straight from the Sinteză (explicit sizes), not from group-scoped partition info.
2. **No first-class Choice bloc or per-bloc Student choice.** The engine needs plan- and semester-scoped packages, their curriculum options, and one selected option per active bloc. A semester schedule is identified by a vector of these choices. Options in the same bloc are disjoint audiences, but that relation does not impose a shared timeslot.
3. **`Int.Eur.` (Integrare Europeană, 17 students) is not a registered Alternative** — only Antreprenoriat/Psihologie/Opț. ped./Cult. comun. are.
4. **"DJ" is both a program code and a `Specialization.AllKnown` value — RESOLVED (user, 2026-09):** the program is **Designul Jocurilor** (as titled in the contingent), and a specialization of the same code also legitimately exists. The specialization's exact full name is unconfirmed (user, 2026-09-05) — "Designul Jocurilor" is used provisionally for both until verified. Two distinct concepts sharing one name, so renaming is not the fix. The modeling direction: entities must reference each other by typed identity (group → specialty, lesson → specialization), never by string/regex name matching — all such regex searches are to be removed eventually; names are display-only. Any resolver that sees the string "DJ" must know which *kind* of thing it is looking for.
5. **One scalar `Alternative` cannot hold independent choices.** It cannot represent one selection for each of several curriculum blocs, nor the coexisting psiho-pedagogic and humanistic module memberships (7 year-2 students carry both). The domain relation is a set of choices keyed by their independent choice dimensions; the concrete implementation shape remains open.
6. **`AttendanceMode.Dual` is a second-class citizen in code.** The data contains `IA 2601 (DUAL)` and `DU 2501`, but `OneForEachAttendanceMode<T>` has no Dual slot (its indexer throws) and `AttendanceModeFlags` omits it.

## 5. Room-capacity inference (stub values for the engine)

From the Sinteză lab-subgroup sizes (74 subgroups / 59 sections) and the contingent group sizes:

- **Lab rooms (computers):** subgroup sizes 1–23, median **15**, typical split cap **15–17**, max observed **23**; 84% of sections are a single subgroup. Stub: **24 seats** per lab room (typical usage 12–18). Psihologie / Limba străină "lab" rooms are the exception (no computers).
- **Academic-group rooms:** 31 bachelor day groups, 3–34 students (only 4 groups ≥ 30, 11 ≥ 25). Whole-group lessons need ≈ **36 seats**; stub **40**.
- **Merged-section lectures (curs):** section totals reach 30/41/47/77 → largest curs rooms stub **80 seats**, mid-size tier **50**.
- Proposed stub tiers for the engine until exact capacities arrive: lab = 24 (computer), curs small = 40, curs medium = 50, curs large = 80.
