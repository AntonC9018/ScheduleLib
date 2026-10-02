# Research: Mine computer-need exceptions from schedules

Resolution of [ScheduleLib-6xo.12](beads) — Research: Mine computer-need exceptions from schedules. 2026-09-06. Worktree `feat/schedule-engine-wayfinder` @ f513629.

**Scope note:** executed inline (main session, user-requested speedup) over the **parsed JSON fixtures only** — `schedule_Default.json` (2024-25 S2) and `schedule_New.json` (2025-26 S1; byte-identical to `src/Tests/data/schedule_2025_1.json`, deduped). The docx/xlsx buckets (2024-25 S1, 2025-26 S2, 2026-27 S1, FR sessions) were **not** mined; room-inventory research found room usage stable across all semester buckets, so the narrowing costs little. Method: rooms classified by majority of typed records within the same fixtures (matches room-inventory §5); every claim below is reproducible from the two JSONs.

Corpus: 3,077 records with rooms (Regular + OneTime). Types seen: Curs / Lab / Seminar / Unspecified (regular), Lab / Prelegere / Seminar (one-time).

## 1. Room classification (typed records ≥ 5)

**Computer-lab** (lab-share ≥ 50%): 350/4 (188/193), 251/4 (180/189), 143/4 (147/161), 423/4 (140/160), 218/4a (134/151), 145/4 (141/150), 145a/4 (142/147), 326/4 (134/138), 237/4 (113/118), 219/4a (100/111), 216a/4a (85/98), 239/4 (26/33), 254/4 (25/29), 10/2a (20/20), 9/2a (15/17), 7/2a (16/16), 412/c (10/14), 327/4 (11/11), 7/2Anexă (9/9), 429/4 (8/8).

**Mixed** (labs present, share < 50%): **415/4** (20/118), 419/4 (3/100), 401/4 (3/99). 415/4 is the substantive one — its 20 lab records belong to the exception courses below.

**Computer-less**: 213a/4, 404/4, 113/4, 214/4, 218/4, 222/4, 122/4, 433/3, 312/c, 425/4, Mediacor II (all ≤ 2 stray lab records; 213a/4, 404/4, 113/4 have zero).

Low-evidence (typed < 5, unclassified): 141/c, Mediacor I, 408/4, 419a/3, 411/4, 405/4, 527/3, 203/3, `404` (typo), 205/3, 236/4, 317/4, 219/4; plus `2374` (typo for 237/4) hosting only untyped records.

## 2. Lab courses vs the default

86 distinct (course, Lab) pairs, 1,535 lab records; **86 courses match the default** (labs sit in computer rooms) except the candidates below. The default "lab-type lesson needs one computer per attending student" holds for **97%+ of lab records** (1,510/1,535 in computer-lab rooms).

### Candidate exceptions — high confidence (propose declaring)

| Course (lab) | Off-lab / total | Rooms | Semesters |
|---|---|---|---|
| **Mat. discr. 1** | 12/12 | 415/4 ×10, 419/4 ×2 | both |
| **Calcul variațional** | 3/3 | 415/4 ×3 | both |

Both are math courses; their "labs" are pen-and-paper problem sessions in seminar-side rooms. Proposal: **declare 0 computers**.

### Mixed evidence — user call

| Course (lab) | Off-lab / total | Rooms | Reading |
|---|---|---|---|
| MDL | 8/22 (13 in labs) | 415/4 ×7, 419/4 ×1 | 1/3 of its labs computer-less; possibly some groups need machines, others don't |
| Testarea și optimizarea jocurilor | 3/9 | 401/4 ×3 | game-testing labs plausibly need machines; 401/4 sessions may be overflow |

### Weak/noise (propose keeping the default)

- Rețele de calcul.: 1/23 in 214/4 (but note its *curs* ran 3/3 in 145a/4 — see §3).
- Tehnologii de programare: 2/72 in 214/4, 218/4.

### The PSI caveat — placement mining is not enough

**No Psihologie records exist in the JSON fixtures** (course name absent). The docx evidence (room-inventory §4) shows PSI `(lab)` lessons running *inside* ordinary computer labs (145/4, 143/4, 326/4, 237/4, 145a/4, 219/4a) — i.e. placement-based mining classifies them as default-matching, while the map's standing note says they need no computers. A lab occupying a computer room without using computers is **invisible to room-based mining**; the exception list needs course-name semantics as well (confirming the user's "mined from the course names logically"). PSI is the known case: **user call — declare PSI lab → 0?**

Limba străină: 134 records, types Unspecified (106) / Seminar (28), never Lab — outside the lab-default question; its avoidance of labs is covered by the untyped default (0 computers).

## 3. Secondary: computer-dependent despite type (reverse candidates)

Untyped (or curs/prel) lessons that systematically sit in computer labs — candidates for "needs computers" declarations:

| Course (type) | In-lab / total | Rooms |
|---|---|---|
| Securitatea aplicațiilor web și mobile (Unspecified) | 24/30 | 218/4a, 326/4, 350/4, 219/4a |
| Dezv. apl. server-side cu Node.js (Unspecified) | 15/15 | 145/4 |
| ACI IT (Unspecified) | 15/18 | 145a/4, 219/4a |
| Fotogrametria și scanarea 3D (Unspecified) | 12/12 | 429/4 |
| Realitate virtuală și augmentată (Unspecified) | 12/18 | 145/4 |
| SMAI IT (Unspecified 9/9; Curs 3/3) | 12/12 | 145/4, 216a/4a, 218/4a |
| Spring Security și Spring Testing (Unspecified) | 9/9 | 423/4 |
| Securitatea rețelelor (Unspecified) | 9/9 | 145/4 |
| React (Unspecified 6/6; Prelegere 7/8), React Native (Unspecified 6/6) | 12/12 | 145a/4, 216a/4a, 219/4a, 237/4, 423/4 |
| Producție virtuală 3D… (Curs) | 4/4 | 423/4 |
| Rețele de calcul. (Curs) | 3/3 | 145a/4 |

Small-section `(prel)` lectures of optional courses also drift through labs (Elab. aplic. graf., Containizare, Framework web, Spring, Dezv. web PHP — 4–8 records each, half in labs) — consistent with room-inventory §4's "small-section lectures run in labs"; treat as incidental unless the same course shows the §3 pattern.

## 4. Declarations to approve (the deliverable)

Proposed builder seed declarations (course + lesson-type → computer need):

1. `Mat. discr. 1, Lab → 0` — 12/12 off-lab, both semesters.
2. `Calcul variațional, Lab → 0` — 3/3 off-lab.
3. `Psihologie (PSI), Lab → 0` — user call; docx evidence of computer-room squatting; absent from JSON fixtures.
4. `MDL, Lab → 0?` — user call; mixed 8/22.
5. Keep default for: Testarea și optimizarea jocurilor, Rețele de calcul., Tehnologii de programare.

And, if the reverse mechanism is wanted now (untyped → needs computers): Securitatea aplicațiilor web și mobile, Dezv. apl. server-side cu Node.js, ACI IT, Fotogrametria și scanarea 3D, Realitate virtuală și augmentată, SMAI IT, Spring Security și Spring Testing, Securitatea rețelelor, React, React Native, Producție virtuală 3D (each → computer need = attendance).

Caveats: 2-semester corpus; low-evidence rooms unclassified; fixture course names are abbreviations in places (MDL, ACI IT, SMAI IT, GAC) — full names unresolved; full-corpus re-run may add rows but is unlikely to remove these (room usage stable across buckets per room-inventory §4).
