# 2026 sem1 clash fixes — parity research (Q10 closure)

Date: 2026-09-05. Scope: the three genuine validator conflicts in the 2026 sem1 Zi build (An-I: IA2603 Limba rom./Limba straină; IA2604 Limba straină/Educația fizică; An-II: I2502 Baze de date/Tehnologii de programare).

Method: grid-aware extraction of the docx tables (a cell vMerged across a band's sub-rows = every week; sub-row 1 = impar week, sub-row 2 = par week — the convention is corroborated by 10+ explicit `(imp)`/`(par)` sibling pairs across both files), cross-group load patterns inside the 2026 docs, and the 2025 sem1 parsed schedules (`schedule_New.json`, previous cohorts IA2503/IA2504/I2502/I2402) as proxy.

**No curriculum/plan documents are committed in the repo** — the only curriculum file is a single-course syllabus (`src/Features/Tests/Curriculum/data/11_I_an1_RC_Capcelea_2024.docx`, Rețele de Calculatoare). The research is therefore limited to doc-internal consistency + previous-year proxy evidence; the real curriculum plans would be needed if further load disputes arise.

## Verdicts — all three are missing parity markers in the source doc

1. **IA2603, Joi 13:15 `Limba straina / G.Ciudin 222/4` → add `(par)`.** It is a separate sub-row-2 cell under `Limba rom.(imp)` — **not a duplicate** (an earlier non-grid-aware read wrongly called it a byte-identical copy of the 8:00 lesson; they are distinct cells in the band's sub-rows). Load math: the general cohort gets 2.5 straina sessions/week with it — the universal pattern (M2601, I2601, I2602, DJ2601, DJ2602 and all three 2025 An-I proxies are 2.5 = 2 every-week + 1 parity-only); deleting it would drop IA2603 to 2.0, below every comparator. The same-day rom pair (11:30 every-week + 13:15 `(imp)`) is the standard 1.5/week rom load for Russian-language groups — not an anomaly.
2. **IA2604, Joi 13:15 sub-row 1 `Limba straina / O.Basirov 214/4` → add `(imp)`.** Sub-row-1 is the impar position (sub-row 2 is explicitly `Educația fizică (par)`). Load: Bașirov cohort = 2.5/week, matching the universal pattern; unmarked-as-every-week would give 3.0.
3. **I2502, Vineri band III sub-row 1 `Baze de date(lab)` → add `(imp)`.** BD lab total = 0.5 (imp, this cell) + 0.5 (par, V-I) + 0.5 (par, V-IV) = 1.5/week — exactly the BD lab load of IA2503, IA2504 (same doc) and I2402 (2025 proxy); unmarked would give 2.0 lab / 3.0 total, above every peer.

Caveat: the assignment totals confirm that a parity-only slot must exist, but cannot by themselves distinguish impar from par — the direction comes from the sub-row convention.

## Conclusion

The lesson-overlap allowlist (9 entries, `ScheduleDefaults/Config.cs:104-172`) is removed entirely:

- Entries 1–6 (elective stacks) dissolve under per-group partition resolution (Alternative assigned per (lesson, group) at build time from observed partitions).
- Entries 7–9 are fixed by the three marker additions above. **Applied to the source docs on 2026-09-05** (byte-level run edits; verified: exactly 3 changed cells across both files, all other zip entries byte-identical, namespaces/prologue intact, structural inspector clean). The next full build should pass overlap validation.

## Assignment evidence (condensed)

An-I Limba straină per language cohort (începători = Ciudin; general = Ciudin/Bașirov): universal pattern **2.5/week** (2 every-week + 1 parity-only) for M2601, I2601, I2602, DJ2601, DJ2602, IA2603 (with the fix), IA2604 (with the fix), and all 2025 proxies; IA2601 1.5, IA2602 1.5–2.0 (smaller cohorts).

Limba română (Russian-language groups): standard **1.5/week** = 1 every-week + 1 parity-only (IA2603, IA2604, I2602; 2025 proxies the same).

Educația fizică: every group exactly **0.5/week** (one parity-only session; imp for most, par for IA2604/DJ2601–3).

I2502 (2026) vs I2402 (2025 proxy): Baze de date 2.5 total (lab 1.5) — matches with the fix; Tehnologii de programare 2.5; Criptografie 3.0 (an extra curs(imp) — not disputed, above last year's 2.5, flagged here for completeness).
