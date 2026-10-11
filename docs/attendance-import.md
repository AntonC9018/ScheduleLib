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
