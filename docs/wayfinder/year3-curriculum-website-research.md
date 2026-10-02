# Year-3 elective blocs — official website verification

Research date: 2026-09-09. Scope: the 2024-admission, full-time (`ZI`) **0613.5 Informatică Aplicată** curriculum followed by the `IA24xx` cohort in year 3 during 2026–2027.

## Conclusion

The screenshots reproduce the year-3 elective packages in the official **2024 full-time Informatică Aplicată study plan**. A bloc is best understood as a versioned curriculum choice package: a student selects one option, and the selected course becomes part of that student's compulsory path. It is not a specialization value and it does **not** imply that all of its courses must be scheduled simultaneously.

The model should therefore distinguish:

- a curriculum package, identified by at least `(program, admission-plan version, attendance mode, semester, package ordinal)`;
- the options belonging to the package;
- each student's selected option;
- the teaching sections actually opened for selected options; and
- an independent scheduling constraint when particular lessons really must share a slot.

The plan version is material: the official 2023 plan uses different discipline-code numbers for substantially the same packages, while the `IA24xx` students and the screenshots match the 2024 plan. A discipline code must not be treated as globally stable across plan versions.

## Authoritative curriculum and exact year-3 packages

The FMI [Informatică Aplicată programme page](https://fmi.usm.md/informatica-aplicata/) identifies the programme as 0613.5, offered in full-time, part-time and dual modes, and links plans separately by admission year and mode. The source matching the screenshots is the scanned [2024 full-time study plan](https://fmi.usm.md/wp-content/uploads/2025/01/Lic_Informatica-aplicata_2024.pdf#page=5), page 5.

The numerical columns below are `total hours / direct-contact hours / individual study`, followed by the direct-contact split `lecture / seminar / laboratory`.

| Curriculum location | Options in the shared plan row | Hours | ECTS |
|---|---|---:|---:|
| Semester V, package 1 | `S.05.A.48` Securitatea aplicațiilor enterprise (BaDe); `S.05.A.49` Dezvoltarea aplicațiilor mobile (BaDe&FrDe); `S.05.A.50` Designul audio și efecte vizuale (DAM) | 180 / 75 / 105; 30 / 0 / 45 | 6 |
| Semester V, package 2 | `S.05.A.51` Securitatea aplicațiilor web și mobile (BaDe&FrDe); `S.05.A.52` Securitatea rețelelor (BaDe&FrDe); `S.05.A.53` Realitate virtuală și augmentată (DAM) | 150 / 75 / 75; 30 / 0 / 45 | 5 |
| Semester V, package 3 | `S.05.A.54` Dezvoltarea de aplicații server-side (BaDe); `S.05.A.55` Dezvoltarea de aplicații client-side (FrDe); `S.05.A.56` Fotogrametria și scanarea 3D (**DATM** in the plan) | 150 / 60 / 90; 30 / 0 / 30 | 5 |
| Semester VI, package 1 | `S.06.A.60` Sisteme de administrare a conținutului web (CMS); `S.06.A.61` Tehnologii blockchain | 120 / 56 / 64; 28 / 0 / 28 | 4 |

The PDF does not print labels such as “Blocul 2”; it conveys each package structurally by putting several `A`-coded disciplines in one shared workload/assessment row. The screenshots explicitly number the three semester-V packages 1–3 and the semester-VI package 1. The operational year-3 distribution workbook instead calls the semester-VI package `Blocul 4`, apparently numbering packages continuously across the academic year. Consequently, `B4` is a source-local alias, not a canonical curriculum identity.

The `A` position in the code denotes an optional course under USM's [methodology for developing and evaluating study plans](https://jurnalism.usm.md/wp-content/uploads/Metodologia-de-elaborare-si-evaluare-a-planurilor-de-invatamant.pdf). It is not itself a globally unique bloc identifier.

USM's [university curriculum framework](https://usm.md/wp-content/uploads/Anexa-2.3.6-Cadrul-de-referinta-al-curriculumului-universitar.pdf#page=48) states that an optional-course choice is made from at least two offers and that the chosen option/package then becomes compulsory. The [USM ECTS regulation](https://usm.md/wp-content/uploads/5.-Regulament_de_aplicare_a_ECTS_USM.pdf#page=9), sections 6.8–6.11, separately requires students to select optional courses, permits opening a study formation/group only when viable, and fixes the selection for the academic year. These rules support explicit `StudentChoice` and opened `TeachingSection` concepts.

### The separate 0613.4 Informatică plan

The official [Informatică programme page](https://fmi.usm.md/informatica/) identifies a separate 0613.4 programme with Cybersecurity, DevOps and Compiler Development profiles. Its [2024 full-time plan](https://fmi.usm.md/wp-content/uploads/2025/01/Lic_Informatica_2024.pdf) gives year 3 these complete packages:

- Semester V package 1: `S.05.A.49` infrastructure/application configuration automation (DevOps), `S.05.A.50` Web/mobile application security (Cybersecurity), `S.05.A.51` parallel and distributed programming (Compiler Development); 6 ECTS, 30 lecture + 45 laboratory hours.
- Semester V package 2: `S.05.A.52` infrastructure/application monitoring and analysis (DevOps), `S.05.A.53` Network security (Cybersecurity), `S.05.A.54` embedded systems (Compiler Development); 5 ECTS, 30 + 45 hours.
- Semester V package 3: `S.05.A.55` Automation and scripting (DevOps), `S.05.A.56` cryptographic protocols (Cybersecurity), `S.05.A.57` formal languages and automata (Compiler Development); 5 ECTS, 30 + 30 hours.
- Semester VI package 1: `S.06.A.61` CMS and `S.06.A.62` Blockchain; 4 ECTS, 28 + 28 hours.

The distribution workbook lists only sections that were actually populated/opened, not this complete option catalogue. Course names can cross programmes with different codes and package positions: CMS is `S.06.A.60` in IA but `S.06.A.61` in I. The model consequently needs a stable course identity separate from the versioned `CurriculumOption`/discipline code.

## What the profile abbreviations establish

The official programme page describes the programme's specializations as web development “backend, frontend” and virtual/augmented reality “dezvoltarea de aplicații pentru multimedia.” Together with the course annotations, this makes the following interpretations strong, but the sources provide no formal acronym legend:

- `BaDe`: backend development;
- `FrDe`: frontend development;
- `DAM`: the multimedia-application profile associated with virtual/augmented reality.

Do not encode those expansions as verified official display names yet. The 2024 plan itself is inconsistent: it writes `DAM` beside audio design and VR/AR, but `DATM` beside `S.05.A.56` Photogrammetry and 3D scanning. Preserve the source token or map both through an explicit, versioned alias only after the faculty confirms their intended expansions.

The annotations are eligibility/profile metadata, not the student's choice. For example, both `S.05.A.51` and `S.05.A.52` are marked `BaDe&FrDe`, yet they are competing options inside the same package.

## No universal parallel-timeslot rule

Neither the programme page, the 2024 study plan, nor the university optional-course rules require alternatives in one package to run in parallel. They define choice, workload and opened student formations, not timetable synchronization.

More strongly, FMI's official [2026–2027 year-3 lesson timetable](https://fmi.usm.md/wp-content/uploads/2026/08/orar3.pdf) schedules alternatives from the same curriculum package at different times for the `IA24xx` cohort. In the Romanian columns, package-2 lectures include VR/AR on Thursday at 13:15, Web/mobile application security on Friday at 15:00, and Network security on Saturday at 11:30. Package 3 similarly has Photogrammetry on Wednesday and Server-side development on Saturday. Some other alternatives happen to coincide, but that is a contingent timetable choice, not a curriculum invariant.

The correct semantics are therefore:

```text
same package => mutually exclusive student audiences; lessons may overlap
same slot     => separate, explicit constraint on selected lesson occurrences
```

This also means all laboratory subgroups of a section must not automatically share one slot. Parity markers and the official timetable show repeated laboratory occurrences scheduled at different times.

## Schedule-choice cardinality

Count schedule variants per semester, not across the academic year. For the complete 2024 IA curriculum, semester V has three independent three-option blocs, so its theoretical choice space is `3 × 3 × 3 = 27` vectors. Semester VI has one two-option bloc, so it has `2` vectors. Multiplying those to obtain `54` describes possible full-year curriculum paths, not distinct schedules for either semester.

The 2026–2027 distribution workbook reports only opened sections. On its evidence, semester V exposes `3 × 3 × 2 = 18` combinations across all IA students because no Client-side section appears. Within either Romanian or Russian instruction, the populated sections imply at most `2 × 3 × 2 = 12` combinations. Semester VI currently shows only CMS, hence one opened choice. These are upper bounds: when resolved per-student choices are available, generate only the distinct choice vectors that actually occur; use the Cartesian product only as a fallback when the population data does not resolve them.

## “Client-side” versus “mobile”

There is no name alias to reconcile in the 2024 curriculum:

- `S.05.A.49` is **Dezvoltarea aplicațiilor mobile**, in semester-V package 1;
- `S.05.A.55` is **Dezvoltarea de aplicații client-side**, in semester-V package 3.

The 2026–2027 distribution workbook's `Blocul 1 IA` mobile rows therefore match `S.05.A.49`. Its `Blocul 3 IA` contains Server-side and Photogrammetry sections but no Client-side section and carries no discipline codes. The least speculative interpretation is that no Client-side teaching section was opened, likely because it received no final selections. It must **not** be renamed or aliased to Mobile. The official 2026–2027 timetable likewise contains Mobile but no Client-side entry.

## Availability notes

- The relevant 2024 full-time PDF is downloadable and readable, although it is image-scanned rather than text-native.
- As of the research date, the official programme page displays a `2026` full-time plan label with no hyperlink. The 2026 FR plan is linked, but the full-time 2026 document cannot be checked from the published page and no filename should be guessed.
- The official programme page and direct PDFs occasionally return an automated bot-check page to browser fetchers; direct HTTP download succeeded for the 2024 plan and the 2026–2027 timetable.
