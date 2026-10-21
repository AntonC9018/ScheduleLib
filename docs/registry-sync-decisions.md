# Registry synchronization decisions

Owner decisions agreed on 2026-10-14. GitHub issues are the authoritative task
and implementation status records; this document preserves the agreed behavior.

## Lecture destinations (#234, #235)

- An omitted registry language suffix means Romanian, not every language.
- Derive routing automatically from schedule audiences and registry lists.
  Prefer shared lecture coverage within the correct language, suppressing
  individual writes for students already covered. For partial shared coverage,
  use the shared list plus individual destinations for the remaining audience.
- RVA Romanian lectures for IA2401 and IA2402 belong in `IA24(RVA)`.
  Russian lectures for IA2403 and IA2404 belong in `IA2403(RVA)ru` and
  `IA2404(RVA)ru`. Those Russian lists retain their lectures. The earlier
  proposal to put both languages in the unsuffixed list is superseded.
- SAWM Romanian lectures belong in the joint `I_IA24(SAWM)` list. Russian
  lectures use `IA24(SAWM)ru` and the appropriate I-faculty Russian lists by
  audience. No coordinator or roster confirmation remains necessary for this
  routing decision; #234 already records the teacher confirmation.
- Validate the routing with no cross-paired dates or duplicate audience
  coverage. RVA topic backfill remains separate in #239 and uses the historical
  mining result, not the incorrectly paired live registry text.

## Attendance and teaching changes (#232)

- Match attendance columns positionally in lesson order when time/type is
  absent, including multiple lessons of the same type on one day and mixed
  Curs/Lab lessons. Available date, type, time and sheet context constrain
  matching. Do not reject a column solely because several lessons share its
  date; the proposed ambiguity warning was superseded by positional matching.
- For Curs attendance, prefer a matching shared sheet; otherwise use individual
  group sheets. Do not add support for sheet names listing multiple groups.
- An attendance date with no matching lesson produces a warning and remains
  unbound. Keep valid dates usable. Suggest possible missing original lessons
  and a teacher-config correction; do not infer a skipped lesson from missing
  attendance alone or automatically confirm a move.
- Support three distinct teacher-config actions: skip, extra and reschedule
  (`from` to `to`). A reschedule removes the original slot and adds its
  replacement; an extra lesson adds a slot without replacing another.
- Select existing lessons with the smallest supplied filter in the current
  teacher's schedule. One match succeeds; zero is an error; several require
  refinement or explicit `matchMultiple`, which selects all matches. Extra
  lessons can use a selected lesson as a template plus a new date/overrides.
- Bulk reschedules preserve original times. To change times, reschedule each
  lesson individually. The proposed list of replacement times was withdrawn.
- Assign topics in actual teaching order within each topic sequence after
  applying the changes. Skips consume no topic, extras consume the next topic,
  and reschedules change slot order rather than carrying the original topic.
  Topics beyond the final available slot remain pending.
- A confirmed matching extra lesson or reschedule resolves the unmatched-date
  warning. Suggestions with multiple plausible originals remain suggestions.

The design is settled. Implementation follow-up [#245](https://github.com/AntonC9018/ScheduleLib/issues/245)
covers automatic mixed-sheet binding, the associated teacher-change behavior
and regression coverage;
the existing workbook settings in [attendance-import.md](attendance-import.md)
describe current behavior, not all of this target behavior.

## Topic manifest partitions (#233)

Keep the existing schema. Use separate manifest entries referencing the same CSV
when scopes differ, or omit all partition filters when every partition should
match. No new additional-partitions flag or `partitions` list is required.

Omitting all partition filters covers both unrestricted (`Any`) lessons and
specializations. Specifying `specialization: DezvoltareaAplicatiilor` excludes
unrestricted lessons; `wholeGroupOnly: true` excludes specialization lessons.
Removing only the specialization field while retaining `wholeGroupOnly` does
not broaden the entry to every partition. Other course/group/semester filters
still apply. General manifest repetition remains a separate concern in #218.

The local SAWM manifest was consolidated to one entry without partition filters
after confirming that its schedule only used whole-group and DA partitions.
The original verification reported successful configuration validation and
1,818 identical before/after topic lookups. This was a private local data
change, not a repository change; its data is not committed here.

## Handoff and validation boundaries

The working branch was clean and already pushed when this decision recovery
started. There was no uncommitted source work from the discussion to recover.
This commit records decisions; it does not implement the remaining features or
apply registry/CSV changes.

- #234/#235 still require implementation and preview validation of the agreed
  routing. The design decision itself is no longer a blocker.
  `CombinedMissingLanguageCanResolveRomanianParents` already provides partial
  combined-label coverage. `RegistryLessonRouting.Prioritize` still sends
  non-Lab lessons to every eligible destination, so shared-lecture suppression
  remains an implementation gap.
- #232's planning acceptance is satisfied; implementation is tracked separately.
  #233's proposed schema extension is declined in favor of existing behavior.
- #239's deferred data changes, #231's source authoring, #238's Unity coverage
  and #236's owner-deferred triage remain separate unfinished work under #240.
- The discussion's analyzer result (2,652 warnings, zero errors) was historical,
  not a fresh build result. Subsequent adoption and validation are documented
  in [codingrules-validation.md](codingrules-validation.md); do not reopen
  completed adoption based on the older result. Remaining coverage, manual
  fixes and policies are tracked in #229, #230 and #244. #228 tracks the
  separately known registry error-logging failure.

No live registry apply, topic CSV reconciliation, package publication, or new
feature implementation is performed by this documentation handoff.
