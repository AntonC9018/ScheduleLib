# Research: Room inventory & curs/lab inference from schedules

Resolution of [ScheduleLib-6xo.9](beads) — Research: Room inventory & curs/lab inference from schedules. Report produced by a research subagent, 2026-09-05. Worktree `feat/schedule-engine-wayfinder` @ f513629.

Method: every room string was extracted programmatically from all schedule sources in the repo (Word tables, Excel workbooks, parsed JSON fixtures), paired with the parenthesized lesson-type marker of its lesson, and cross-tabulated. Count columns below = typed lesson records (one record = one lesson line naming that room), pooled over all sources. "Semesters" = semester buckets in which the room appears. All file paths are relative to the repo root; schedule docs live under `src/Features/Integration/data/`.

## 1. Evidence base (what "current and previous years" actually exist here)

| Corpus | Files | What it is |
|---|---|---|
| 2024–25 sem 1 | `data/Orar_An_II Lic.docx` (root) | Year-2 zi schedule, groups `M2301/I2301/IA2301/IA2302` (2023 cohort). Only file older than 2024-09. |
| 2024–25 sem 2 | `data/2024_sem2/zi/{01.09.24,19.03.25}/Orar_An_{I,II,III} Lic.docx` (2 snapshots), `data/2024_sem2/zi/master.docx` | Official Master year-I schedule ("CICLUL II, MASTER … 2024-2025 SEMESTRUL II", groups SMMSPA/PMS/MIA/IASD 2401) — only 8 room-bearing lines, rest online ("link de acces") or in 415/4, 419/4. |
| 2025–26 sem 1 | `data/2025_sem1/zi/{01.09.25,15.09.25,05.11.25}/Orar_An_{I,II,III} Lic.docx` (3 snapshots), `data/2025_sem1/fr/1.xlsx` | xlsx = "ORARUL SESIUNII DE INSTRUIRE … (învățămînt cu frecvență redusă)", sheets `sem I`,`bd`; groups MFR/IAFR/IȘEFR years I–IV. |
| 2025–26 sem 2 | `data/2025_sem2/zi/{27.01.26,13.02.26}/Orar_An_{I,II,III} Lic.docx` (2 snapshots), `data/2025_sem2/zi/master.xlsx`, `data/2025_sem2/fr/{1,2}.xlsx` | master.xlsx = "ANUL I MASTER", MIA 2501/IASD 2501, Sem II 02.02–24.05.2026; fr/2.xlsx = part-time session "11 mai – 7 iunie 2026". |
| 2026–27 sem 1 | `data/2026_sem1/zi/01.09.26/Orar_An_{I,II,III} Lic.docx` | Most recent snapshot. |
| Parsed fixtures | `src/ScheduleLib/Tests/ScheduleFromDoc/schedule_{Default,New}.json` + `*_verify_schedule_json.verified.json` | Parser output: Default = **2024 sem2** docx, New = **2025 sem1** docx (`IntegrationTestHelper.cs:84-102`: years 2024/Sem2, 2025/Sem1). Give typed `LessonType` + normalized `Room`. |
| Website export | `src/Tests/data/schedule_2025_1.json` | Same model, 2025 sem1, incl. dated `OneTimeLessons` (`Type:"Prelegere"`). |
| Non-schedule | `data/Paritate.docx` (week-parity calendar 2024-25 S2), `src/Features/FmiWebsiteInterop/example.json` (thesis list), `data/topics/*` (topic lists) | **No rooms.** Excluded. |

No schedule older than academic 2024-25 exists in the repo — "previous years" = 2024-25 at most.

## 2. Room naming grammar and model facts

- Format: `<number 1-3 digits>[a-z]` + `/` + `<floor/building code>`: `222/4`, `145a/4`, `216a/4a`, `7/2a`, `7/2Anexă`, `412/c`, `429/c`. Off-site: `Mediacor, etajul I|II` (USM cinema hall). Placeholder `____` (6 lessons of A.Dabija, 2024s2 An II). Typos seen: `2374` (=237/4, `New_verify_schedule_json.verified.json`), `404` (=404/4, same), `145A/4` (=145a/4), `216/4a` (=216a/4a), `326/6` (fr/2.xlsx ×1, likely 326/4 or floor-6 room).
- Model: a room is **only a name** — `public readonly record struct RoomId(string? Id)` (`src/ScheduleLib/ScheduleLib.Core/Model/Schedule.cs:898`). No capacity, no kind, no building anywhere in the model or sources.
- Parser grammar: `RoomParser` (`src/ScheduleLib/ScheduleLib.Core/Parsing/LessonParser/LessonParser.cs:1711`) = number+optional letter, optional `/`+number-or-code, special cases Mediacor and all-underscore tokens. Room string goes into the schedule verbatim (`WordScheduleParser.cs:102-105`).
- Lesson-type markers parsed from parentheses: `curs`, `lab`, `sem`, `prel`, `exam` (`src/ScheduleLib/ScheduleLib.Core/Helpers/Services.cs:80-84`); default `Unspecified` (`LessonParser.cs:121`). `(prel)` = Prelegere; in the 2025s1 fixture all 211 Prelegere are **one-time dated** lessons (session week 2025-09-22, e.g. `Fundamentele programării 404/4 2025-09-22`) — i.e. weekly lectures are `(curs)`, session/one-time lectures are `(prel)`. Same room kinds serve both.

## 3. Room inventory

### 3.1 Computer labs ("lab" rooms)

Dedicated labs, ≥95% of typed lessons are `(lab)`:

| Room | lab | curs | sem | prel | exam | Semesters |
|---|---|---|---|---|---|---|
| 251/4 | 505 | 7 | 0 | 19 | 3 | 2024s1–2026s1 |
| 350/4 | 506 | 10 | 0 | 12 | 3 | 2024s1–2026s1 |
| 145/4 | 405 | 22 | 2 | 7 | 2 | 2024s1–2026s1 |
| 423/4 | 412 | 44 | 1 | 15 | 1 | 2024s1–2026s1 |
| 326/4 | 389 | 1 | 0 | 12 | 3 | 2024s1–2026s1 |
| 143/4 | 437 | 21 | 0 | 16 | 3 | 2024s1–2026s1 |
| 145a/4 | 391 | 8 | 0 | 6 | 4 | 2024s2–2026s1 |
| 218/4a | 359 | 6 | 0 | 32 | 6 | 2024s1–2026s1 |
| 219/4a | 313 | 0 | 3 | 31 | 7 | 2024s1–2026s1 |
| 237/4 | 366 | 4 | 1 | 11 | 1 | 2024s1–2026s1 |
| 216a/4a | 246 | 0 | 0 | 30 | 5 | 2024s1–2026s1 |
| 327/4 | 22 | 0 | 0 | 0 | 0 | 2024s2, 2025s1 |
| 429/4 | 21 | 2 | 0 | 0 | 0 | 2024s2–2026s1 |
| 412/c | 20 | 4 | 4 | 0 | 0 | 2024s2, 2025s1 |
| 408/4 | 2 | 0 | 0 | 0 | 0 | 2024s2 |
| 7/2Anexă | 18 | 0 | 0 | 0 | 0 | 2024s2 |

Annex building rooms (block "2a/2Anexă", used by zi 2025+ and by the part-time session workbooks): `7/2a` (39 lab), `9/2a` (42 lab, 4 sem), `10/2a` (40 lab), `11/2a` (2 lab) — `2025_sem1/fr/1.xlsx`, `2025_sem2/fr/1.xlsx`; e.g. `I: Sec. cibern. (lab), I. Tican, 7/2a`. Singletons: `208/4a` (1, fr/2.xlsx), `326/4a`, `219/4`, `236/4`.

Mixed rooms — host both `(lab)` and `(curs)/(prel)` (candidate "dual-kind"): `239/4` (107 lab / 13 curs / 13 prel; e.g. `Softuri mat. (curs) B.Hâncu 239/4`, root docx An I), `254/4` (55 lab / 11 curs).

### 3.2 Large computer-less ("curs") rooms

| Room | lab | curs | sem | prel | exam | Semesters |
|---|---|---|---|---|---|---|
| 213a/4 | 0 | 285 | 27 | 99 | 18 | 2024s1–2026s1 |
| 404/4 | 2 | 207 | 49 | 143 | 20 | 2024s1–2026s1 |
| 113/4 | 0 | 261 | 54 | 32 | 4 | 2024s1–2026s1 |
| 405/4 | 0 | 7 | 0 | 0 | 0 | 2024s1, 2025s1 |
| 425/4 | 0 | 10 | 0 | 0 | 0 | 2025s1 |
| 411/4 | 2 | 4 | 1 | 1 | 0 | 2024s2, 2025s2 |
| 113a/4 | 0 | 1 | 0 | 0 | 0 | 2026s1 |
| 141/c, 145/c, 413/c, 432/c | 0 | 4/2/2/5 | 0–3 | 0 | 0 | 2024s1–2024s2 (building "c") |
| 301/3, 528/3 | 0 | 1/2 | 0 | 0 | 0 | 2026s1 |
| 312/c | 0 | 8 | 4 | 0 | 0 | 2024s2 |
| Mediacor, etajul I | 0 | 4 | 0 | 0 | 0 | 2024s2 |
| Mediacor, etajul II | 0 | 8 | 0 | 0 | 0 | 2024s2 |

Three rooms absorb the bulk of weekly `(curs)`: **113/4, 213a/4, 404/4** (together 653 curs records ≈ 90% of all curs-typed records). Exams (`exam`) concentrate in 404/4 (20) and 213a/4 (18).

### 3.3 Mid-size seminar-type rooms (curs-side, host `(sem)` + untyped)

| Room | lab | curs | sem | prel | exam | Semesters |
|---|---|---|---|---|---|---|
| 415/4 | 40 | 62 | 188 | 66 | 8 | 2024s2–2026s1 |
| 419/4 | 8 | 97 | 145 | 49 | 5 | 2024s1–2026s1 |
| 401/4 | 7 | 129 | 88 | 62 | 7 | 2024s1–2026s1 |
| 214/4 | 4 | 84 | 100 | 81 | 4 | 2024s1–2026s1 |
| 218/4 | 2 | 95 | 81 | 25 | 4 | 2024s2–2026s1 |
| 222/4 | 1 | 32 | 53 | 26 | 5 | 2024s1–2026s1 |
| 433/3 | 0 | 60 | 59 | 0 | 0 | 2024s2–2026s1 |
| 122/4 | 2 | 8 | 22 | 42 | 3 | 2024s2–2025s2 |
| 527/3 | 0 | 0 | 16 | 0 | 0 | 2025s1 |
| 407/4 | 0 | 0 | 20 | 3 | 3 | 2024s1, 2025s2 |
| 205/3, 203/3, 419a/3, 408/3, 429/c | 0 | ≤2 | 2–4 | 0 | 0 | scattered |

`415/4` is the notable outlier: seminar-heavy but hosting real `(lab)` lessons (`Mat. discr. 1 (lab), I. Cucu, 415/4` — `2025_sem1/fr/1.xlsx`; 13 lab records in `New_verify_schedule_json.verified.json`). Either a large computer room or overflow use; user must classify.

### 3.4 Count summary

65 distinct room strings appear across all sources; after merging typo/alias forms (`2374`, `404`, `145A/4`, `216/4a`, `326/6?`, `____`) → **~59 real rooms**: **21 lab-kind** (16 dedicated + 5 annex/session), **17 lecture/curs-kind**, **15 seminar-side**, **2 mixed** (239/4, 254/4), plus off-site Mediacor I/II counted in lecture-kind. Full raw cross-tab: semesters per room in §3.1–3.3; per-file presence was verified for every room in every one of the 20 docx + 4 xlsx sources.

## 4. Lesson-type → room-kind patterns

| Lesson marker | Room kind it lands in | Evidence |
|---|---|---|
| `(lab)` | computer labs, ~97% of typed records; a `(lab)` never lands in 113/4/213a/4/404/4 except 3 stray records | §3.1 table; e.g. `Secur. ciber. (lab) D.Semeniuc 145a/4` (2026s1 An I), `Fund. progr. (lab) I: Cr.Crudu 237/4` (2026s1 An I) |
| `(curs)` weekly lecture | 113/4, 213a/4, 404/4 first, then 401/4/218/4/214/4/419/4; small optional-section curs can sit in labs | `Baze de date(curs) L.Novac 404/4` (root docx); `Logica și teor. mulț. (curs) I.Cucu 415/4` (2026s1 An I); `S22 React (prel) D. Negura 237/4` (2025_sem2/fr/1.xlsx) |
| `(prel)` one-time session lecture | same pools as curs (404/4: 143, 213a/4: 99, labs get small-section prel) | `Fundamentele programării 404/4 2025-09-22` (New fixture OneTime); `(prel)` markers pervasive in `2025_sem2/zi/master.xlsx` |
| `(sem)` | seminar-side mid rooms (§3.3), essentially never in computer labs (only 9/2a ×4, FR session) | `Opț. ped. (sem) N.Bîrnaz 407/4`, `Integrare europeană (sem) L.Beniuc 205/3` (root docx) |
| `(exam)` | 404/4, 213a/4, regular rooms (no dedicated exam halls in data) | 404/4 exam ×20 |
| no marker (Unspecified) | mostly Limba straină/Limba română/Math seminars in curs-side rooms | §4 exceptions below; 344 Unspecified in New fixture |

**Exceptions (the two known ones, verified against data):**

- **Limba străină** — never in a computer lab. Rooms: 214/4 and 222/4 dominant (2025–26), earlier 401/4/419/4/415/4/218/4/122/4. Examples: `Limba straina (începători) G.Ciudin 222/4` (2026s1 An I ×~57 occurrences of 222/4 in that file), `L. str. (sem) G. Ciudin 214/4` (2025s1 An I). Limba română behaves identically (401/4, 415/4, 218/4, 419/4).
- **Psihologie** — `(curs)`/`(sem)` in curs-side rooms (433/3 in 2024s2 An I: `Opț.psihol. (curs,imp), Psihologie (sem,par) V.Miron 433/3`; 401/4, 404/4, 113/4 in 2026s1 An II). **But** `(lab)`-marked PSI lessons in year 2 were *scheduled in ordinary computer labs*: `PSI (lab) I: M.Butnaru 145/4`, `PSI (lab) II: A.Gladei 143/4`, `PSI (lab) A.Gladei 326/4`, `PSI (lab) N.Pleșca 237/4` (2024s2 An II), `PSI(lab) M.Butnaru 145a/4`, `PSI (lab) I: A.Gladei 219/4a` (2025s2 An II), `PSI (lab,par) N.Pleșca 143/4` (2026s1 An I). So the exception is about **requirement** (these labs need no computers), not about a distinct room set — historically they landed wherever was free.

Additional inference-relevant patterns:

- **Small specialization-section lectures use lab rooms**: `S21 Containizare (prel) D. Borș 143/4`, `S22 React (prel) 237/4`, `S23 Dezv. joc. (prel) 219/4a`, `Framework web (prel) 216a/4a`, `HTML (prel) 401/4` (`2025_sem1/fr/1.xlsx`, `2025_sem2/fr/1.xlsx`). Sections are ~10–25 students, so "lecture in lab room" is normal for optionals — a hard curs→curs-room rule would be wrong.
- **Educ. fizică** carries no room at all (off-site halls); **online** lessons carry `link de acces` instead of a room (2024_sem2 master.docx). `Room = null` must stay expressible (74+46 such lessons in fixtures).
- Room usage is **stable across years and tracks**: every top-10 room keeps its class in all 5 semester buckets; the part-time (FR session) workbooks use the same rooms as zi.

## 5. How reliable is kind inference from the name alone?

- **The number says nothing verifiable about size or furnishing.** There is no capacity column in any source (docx, xlsx, JSON) and no room-metadata file anywhere in the repo.
- Weak-but-real structural signal: rooms suffixed `/4a`, `/2a`, `/2Anexă` are **always labs** (annex wings hold the computer rooms): 216a/4a, 218/4a, 219/4a, 208/4a, 7/2a, 9/2a, 10/2a, 11/2a, 7/2Anexă. `/3`-suffixed rooms are always curs/seminar-side (433/3, 527/3, 528/3, 419a/3, 408/3, 301/3, 203/3, 205/3). `/c` (building C) is mixed: 412/c lab vs 141/c, 145/c, 413/c, 432/c, 429/c lecture/seminar. Letter on the room number is a *different room*, not an alias: 218/4 (curs-side) vs 218/4a (lab); 213a/4 exists without a plain 213.
- Inference from *history* (which kind of lessons a room hosted) is highly reliable for the 33 rooms with ≥20 typed records — misclassification risk concentrates in low-count rooms (≤8 typed records: 141/c, 145/c, 413/c, 432/c, 405/4, 411/4, 113a/4, 114/4, 301/3, 528/3, 408/3, 408/4, 419a/3, 429/c, 203/3, 205/3, 219/4, 236/4, 208/4a, 326/4a) and in 415/4/239/4/254/4 (genuinely dual-use).
- Recommended rule for a builder: infer kind from **observed lesson types with majority vote**, then freeze into an explicit room table; do not re-derive per build.

## 6. Capacity evidence

- **None.** No source states a room capacity — not the xlsx (no such column), not the docx, not the JSON. The stub tiers in `group-structure-2026-2027.md` §5 (lab 24 / curs 40/50/80) rest entirely on **demand-side** numbers (lab subgroups 13–21, groups 3–34, merged sections up to 77), not on room data. This research neither confirms nor challenges the stubs; it only shows three very large lecture rooms exist (113/4, 213a/4, 404/4 absorbing ~90% of weekly curs), consistent with "a few large rooms" but giving no seat counts.
- Indirect hints only: Mediacor etajul I/II is a cinema hall (large); the three big curs rooms hosting all merged-curs traffic suggests they are the largest; part-time session groups are small ("6 (1 sg.)", "12 (1 sg.) +2(M)+1(MA)" — `2025_sem1/fr/1.xlsx` sheet `sem I` header rows).

## 7. Open questions for the user (for "Grilling: Room rules semantics")

1. **Capacities**: no data exists in-repo. Supply per-room seat counts, or accept the §5 stubs (lab 24, curs 40/50/80) until then?
2. **Suffix semantics**: is `/4` a floor (etaj) or a building (corp)? What exactly are `4a` (4th-floor annex?), `2a`/`2Anexă` (block-2 annex), `c` (corp C)? Is `326/6` a real floor-6 room or a typo for 326/4?
3. **Scope**: are off-main-building rooms (Mediacor, building-c rooms, 2a-annex labs) schedulable by the engine, or fixed external venues to pin manually?
4. **Kind semantics — room attribute vs lesson requirement**: the data shows requirement-driven reality (PSI labs ran in computer labs; small-section lectures ran in labs). Should the engine (a) tag each *room* curs/lab and enforce lesson-type→room-kind, exempting Psihologie/Limba străină labs; or (b) tag each *lesson* with a "needs computers" flag and let any room satisfy it? (b) matches observed practice.
5. **Dual-use rooms**: are 415/4 (lab+seminar+curs), 239/4 and 254/4 (lab+curs) single rooms of one kind, or should the engine treat them as members of both pools?
6. **Unspecified-type lessons** (Limba străină, math seminars, ~16% of 2025s1): default them to "any curs-side room" or keep them requirement-free?
7. **Name normalization**: fix typo variants (`2374`→237/4, `404`→404/4, `145A/4`→145a/4, `216/4a`→216a/4a) at parse time, or keep verbatim strings and alias in the engine room table?
8. **Snapshot churn**: rooms do move between semester snapshots (e.g. 19.03.25 update adds 122/4/408/4/419a/3; 2026s1 adds 113a/4/301/3/528/3). Is the engine free to re-assign rooms on re-solve, or are historical placements binding?
9. **"FR" naming collision**: in the schedule files FR = *frecvență redusă* (part-time session track, workbook header "învățămînt cu frecvență redusă", groups MFR/IAFR/IȘEFR), while `group-structure-2026-2027.md` §1 reads the contingent's FR sheet as *francophone*. Same group codes — which meaning is intended for the 2026-2027 model? (No room impact; affects the group model.)
10. **null-room lessons** (sports, online "link de acces"): confirm they stay outside the day×slot×room axes (no room variable at all).

## 8. Implications for the engine

- A two-kind room model is **supported by the data**: ~21 lab rooms + ~32 curs/seminar rooms cover every semester in the repo, and typed `(lab)` lessons land in lab rooms ~97% of the time. No third kind is needed for feasibility; seminar-side rooms can be modeled as "curs" with a preference, since `(sem)` never competes for labs.
- Build the room table as **engine-side metadata** (name → {kind, capacity, building}) in one explicit file, with the typo/alias normalization of §2 — the parser should keep handing over raw strings (`RoomId` is a bare name today, `Schedule.cs:898`).
- Encode **"needs computers" per lesson** (default: Lab yes; Psihologie/Limba-străină labs and optional-section `(prel)`/`(curs)` no) rather than a hard lesson-type↔room-kind binding — that is what the schedules actually do, and it degrades gracefully when lab rooms run out.
- Keep `Room = null` expressible (sports/online); the axes day×slot×room must allow a "no room" outcome for ~5% of lessons.
- Capacity stubs cannot be validated from schedule data — any capacity constraint added to CP-SAT is an assumption to flag, not a fact (§6).

## Addendum (2026-09-06, user-confirmed facts)

- **423/4 carries VR equipment: VR headsets and a 3D printer** (user, 2026-09-06). This is the first concrete **room feature**: features are flags on the room (countable ones like computers are counts) and participate in lesson requirements with the same declaration mechanics as the computer need (per course/teacher/group, requirement or preference tier).
- Observed VR-flavored placements (both JSON fixtures): `Dezvolt. aplic. de RV pe Unity` labs → 423/4 exclusively (2024-25 S2); `XR pe UE` labs split 350/4 ×6 / 423/4 ×3; `RA cu Unity` labs → 350/4 only; `Dezv. Joc. 3D pe Unity` labs → 423/4 ×5, 350/4 ×2, 237/4 ×2; `GA 3D` → 219/4a + 423/4; `Producție virtuală 3D…` curs → 423/4 ×4; `Realitate virtuală și augmentată` → 145/4 ×12, 113/4, 401/4 (never 423/4 in 2025-26 S1). VR-hardware lessons demonstrably land outside 423/4 — whether a given lesson *required* the equipment is not derivable from placement; which lessons require VR remains an explicit builder declaration.
- **Known room feature set** (user, 2026-09-06): beyond computers (countable), rooms carry feature flags — **VR headsets, 3D printer, air conditioning (A/C), interactive whiteboard, regular whiteboard**. Which rooms carry which (beyond 423/4's VR gear) is not derivable from schedule data; the per-room feature values are builder-input catalog data, and per-lesson feature requirements use the same declaration mechanics as the computer need.
