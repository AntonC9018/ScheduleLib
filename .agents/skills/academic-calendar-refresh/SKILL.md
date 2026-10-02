---
name: academic-calendar-refresh
description: Refresh ScheduleLib's academic-year date configuration from official USM calendar PDFs, verify semester ranges, study-week parity, and holiday exclusions, and optionally republish authorized Google calendars. Use when a new academic calendar is published or calendar synchronization produces missing or out-of-term events.
---

# Academic Calendar Refresh

Update the calendar as one coherent unit. `SemesterIntervalProvider`, `StudyWeeks`, and
`HolidayPeriods` all influence generated events; changing only one can leave calendar sync
silently empty or inaccurate.

## Find the official calendars

1. Open the live [USM academic calendar page](https://usm.md/?page_id=731). Discover the
   current PDF link from the page instead of guessing a year-specific asset URL.
2. Check the relevant faculty page for reduced-attendance calendars. Do not apply another
   faculty's detailed calendar to FMI merely because its PDF is available.
3. Download each relevant PDF under `tmp/academic-calendar/`.
4. Use the PDF skill to render every relevant page to PNG. Inspect the rendered pages visually
   and compare them with extracted text. Treat the PDF, not search snippets or filenames, as the
   source of truth.

Record these facts for each study year and semester:

- teaching start and inclusive teaching end;
- teaching week count;
- vacation start and inclusive end;
- separate practice, exam, and thesis blocks;
- whether a graduating year has a shorter teaching interval.

## Map the PDF into ScheduleLib

Edit `src/ScheduleDefaults/Config.cs`:

1. In `SemesterIntervalProvider`, map PDF year I/II/III to `Grade(1/2/3)`. Configure only the
   instruction or lesson interval; do not widen it to cover practice or exams.
2. In `StudyWeeks`, include the Monday containing the first teaching day. Preserve continuous
   odd/even numbering across the academic year: after a 15-week fall term, the first spring week
   is even. The semester interval filters any leading days before the official start date.
3. In `HolidayPeriods`, remember that `EndExclusive` is exclusive. Convert an inclusive PDF end
   date by adding one day. Keep statutory or local no-class days distinct from PDF vacations and
   verify them separately when changing them.
4. A reduced-attendance calendar usually contains multiple short blocks and does not fit the
   current single-range model. If no authoritative FMI mapping exists, update only the broad
   academic-year envelope and retain a comment explaining the limitation.

## Prove the refresh

Update `AcademicCalendarYYYY_YYYYMatchesPublishedDates` in
`src/ScheduleLib/Tests/ScheduleFromDoc/DateProviderTests.cs`. Cover all full-time grades and both
semesters, the first/last Monday and parity boundary of each term, and every holiday interval.

Run the focused test before editing configuration and confirm it fails on the old year. Run it
again after the edit, then run the full `ScheduleFromDoc.Tests` project. Existing dependency or
preview-SDK warnings are not calendar failures, but report them if they remain.

## Republish only when authorized

Google calendar sync deletes and recreates the target non-primary calendar. Run it only when the
user has explicitly authorized calendar publication, and use the `schedule-document-import`
skill for the exact `MainCli` workflow. Verify each requested teacher independently. Calendar
publication clears the configured `output/` directory, so preserve unrelated source changes and
mention that side effect in the handoff.
