# Dual schedule dates for 2026–2027

Official sources checked on 2026-10-12:

- [FMI academic calendars](https://fmi.usm.md/calendar-academic/) link calendars for Zi, FR and Master. The [Zi PDF](https://fmi.usm.md/wp-content/uploads/2026/09/calendar_fmi-_26-27-licenta-zi.pdf) specifies teaching through 13 December 2026 and is labelled `proiect`. It does not explicitly establish applicability to dual groups.
- [FMI schedules](https://fmi.usm.md/orar/) advertise 1 September–14 December 2026. The linked [first-year schedule](https://fmi.usm.md/wp-content/uploads/2026/09/orar1.pdf) includes `DU-IA2601`; the [second-year schedule](https://fmi.usm.md/wp-content/uploads/2026/09/orar2.pdf) includes `DU-IA2501`. These PDFs contain weekly lessons, without exact semester boundaries. The advertised end differs from the calendar's teaching end.
- [FMI's dual Applied Informatics study plan](https://fmi.usm.md/wp-content/uploads/2025/07/lic_dual_informatica_aplicata_2025.pdf), page 3, gives teaching months only: year I September–December / February–May; year II September–December / February–April; year III September–November / January–March. It separates teaching, examinations and internships. These month ranges cannot establish exact teaching dates or exclusions.

The maintainer confirmed on 2026-10-12 that the current DU date range is the same as frecvență zi. `SemesterIntervalProvider` now explicitly registers `DualZi` alongside Zi using the same grade-specific semester boundaries. The existing shared study-week and holiday configuration remains in use. This applicability comes from maintainer confirmation, rather than a dual-specific published PDF. `DualFr` remains unconfigured; missing ranges still report `missing-schedule-dates`, without a generic attendance-mode fallback.

## Group identity

`DualZi` retains the former `Dual` enum value (2). `DualFr` is separate. `IsZi()`, `IsFr()` and `IsDual()` classify attendance form and dual modality independently. Registry matching still requires the exact mode, program, intake, number and applicable language. Shared scheduled lessons may include ordinary and dual groups of the same attendance form.

`DU-<program>` carries dual modality without implying Applied Informatics. Schedule defaults explicitly map the observed legacy `DU2501` code to `DU-IA2501`, preserving its language. Other `DU` codes are not rewritten. Canonical names are retained in serialized schedule fixtures and snapshots.
