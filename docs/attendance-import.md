# Attendance workbook settings

Each `LessonAttendanceConfig.sources` entry can configure its workbook layout:

| Setting | Default | Purpose |
| --- | --- | --- |
| `studentNameColumn` | Automatic | Optional one-based override. With headers enabled, recognize the student-name header, skip number headers (`Nr.`) and fully empty columns, then fall back to the first populated column. With `Nothing`, default to column A. |
| `headerFormat` | `Auto` | Select `Date` to require date headers. `LessonType`, `Nothing`, and `IgnoreHeader` retain their existing behavior. |
| `dateMode` | `Ignore` | Select `ValidateAndBind` to match attendance to scheduled lesson dates. The default preserves legacy column-order matching. |
| `worksheetNames` | `{}` | Map worksheet shorthand to a schedule lesson name, including course, group, lesson type, and subgroup when needed. |

For a numbered workbook, configure a source like this (replace the course,
group, file path, and worksheet names with the actual schedule values):

```json
{
  "filePath": "attendance.xlsx",
  "headerFormat": "Date",
  "dateMode": "ValidateAndBind",
  "worksheetNames": {
    "IA2601 shorthand": "Course name IA2601 (lab)"
  }
}
```

Set the source collection using `schedulelib config set LessonAttendanceConfig.sources
'<JSON array>' --scope user --profile '<teacher>'`. Sources are keyed by file path;
setting a matching source updates it through the usual layered configuration.
An omitted alias map inherits; a supplied alias map replaces the previous map.
Worksheet aliases must identify an unambiguous lesson in the selected schedule.
Group-only sheet names can remain unmapped when the group identifies one lesson;
course/subgroup shorthand can use aliases without renaming the workbook.

Worksheets with no meaningful content are skipped before header validation or
lesson lookup, including formatting-only, whitespace-only, and empty table
worksheets. Worksheets with attendance headers still retain their header data.

Dates may be native Excel dates or text such as `02.09`, `02.09.2026`,
`02/09/2026`, or `2026-09-02`. Native dates and dates with a year match exactly;
text without a year matches month and day. `07.09 curs` and `curs 05.10` also
specify the lesson type. Unannotated date columns use the worksheet's matched
lesson type. Mixed lecture/lab columns are separated by lesson type before date
matching, so a lecture and lab on the same date can have distinct attendance.

`ValidateAndBind` requires dates throughout the attendance header. Leading
metadata columns and fully empty columns are ignored. Blank cells in a dated
attendance column still mean present; they do not remove that column. Without
recognized attendance headers, blank attendance columns retain their existing
meaning as days when everyone is present. Only existing
scheduled lessons receive attendance: extra dates do not create lessons. A
missing date or duplicate date within the same lesson type returns no attendance
for that occurrence, leaving registry values unchanged. Duplicate dates within
one lesson type need distinct source data before attendance can be applied
unambiguously.

The importer accepts empty cells for present, `a`/`np` for absent, `am` for
motivated absence, and `na` for not applicable. Existing `cellValueFormat` and
`repeatedCourseBehavior` settings remain available per source.

The regression fixture `src/Features/Tests/OnlineRegistry/data/numbered-attendance.xlsx`
preserves a 13-sheet numbered workbook's dates, styles, blank rows, and mixed
headers, with synthetic student names and cleared document metadata.

Registry course labels can be mapped separately from worksheet names using
`RegistryConfig.courseNames`. This map runs before automatic registry course
lookup and can be scoped to one teacher. For example:

```sh
schedulelib config set RegistryConfig.courseNames '{"Registry label":"Schedule course name"}' --scope user --profile '<teacher>'
```

An explicit alias with an unknown schedule target is reported as unmatched;
it does not fall back to the original label. Supplying a map replaces the map
from earlier layers; omitting it inherits the earlier map. Course aliases do not
change group or specialization matching, or override teacher assignments.

Registry course matching parses trailing `(BaDe)`, `(FrDe)`, and `(BaDe&FrDe)`
elective classifications separately from the course name. Exact configured
aliases take precedence; otherwise course lookup falls back to punctuation,
spacing, case, and diacritic normalization. Longer names (at least 12 normalized
characters) may differ by at most `RegistryConfig.courseNameMaxDistance`
characters (default 2), capped at 10% of the input length. Matching requires one
unique course within the tolerance, including all its known aliases. Set the
maximum distance to zero to disable edit-distance matching. Unknown annotations
are not discarded by fuzzy matching.

Calibration against the current schedule's 110 aliases for 66 courses found no
cross-course pairs inside that tolerance. The observed UI/UX spelling difference
has normalized distance zero; the Node.js/server-side wording differs by eight
characters and requires an explicit semantic alias. These measurements support
the conservative tolerance, not unrestricted similarity matching.

`RegistryConfig.skipAttendance` can exclude a registry audience such as
`FrecventaRedusa` before its lesson list is fetched. Configure
`RegistryLessonFilterConfig.skipAttendance` as well to retain the lesson-level
filter.

Intake-wide registry audiences must not collect laboratory sessions. Labs require
an explicit parent-group destination and its subgroup/specialization partition,
even when an intake label currently resolves to only one parent. If such a
destination would receive the selected teacher's labs, synchronization emits an
actionable warning and skips that entire destination before reading or reconciling
its lesson columns, including under explicit apply. Other destinations continue
synchronizing normally; this warning alone does not cause a failed exit code.
This also prevents positional reconciliation from rewriting existing lab columns
as lectures. Correct the registry assignments before retrying; existing records
are left unchanged. Explicit per-group lab projections remain supported.

To remove labs previously published into these destinations, use the separate
cleanup operation. It selects the current semester's unsafe destinations with
matching labs for the selected teacher, preserves other lesson types and explicit
group destinations, and does not load attendance workbooks or topic sources:

```bash
schedulelib registry cleanup-intake-labs --profile "Teacher Name" --json
schedulelib registry cleanup-intake-labs --profile "Teacher Name" --apply --json
```

The preview lists every deletion and counts recorded attendance marks that it
would remove. Apply rereads each lab's date, type, topic, and attendance before
deleting it and confirms that its column disappeared. If the registry leaves the
column unchanged, cleanup reports the refused deletion as omitted and continues.
Changed columns or failed confirmation reads stop cleanup; successful earlier
deletions remain applied. Cleanup uses the same registry account lock as
synchronization and never retries a deletion automatically. It does not recreate
labs in corrected sections.

`RegistryConfig.extraLessonInstanceAction = "Delete"` is the existing policy for
surplus lessons after ordinary positional reconciliation. Its default is
`"LeaveAlone"`; `"DeleteWithoutDataLoss"` is unsupported. This policy does not
clean skipped intake destinations and is unnecessary for the targeted command.

Genuinely shared lectures can compose dated attendance from individual parent
sheets when no combined sheet exists. A missing or ambiguous date leaves that
parent's attendance unchanged; separate headerless sheets do not provide enough
information for shared-audience ordinal matching.

`RegistryConfig.studentNames` maps explicit registry student-name spellings to
attendance-sheet names for the selected teacher. Matching still uses the existing
name comparer; unlisted students and aliases whose target has no attendance row
remain unchanged. Supplied maps replace earlier-layer maps, as with course aliases.
