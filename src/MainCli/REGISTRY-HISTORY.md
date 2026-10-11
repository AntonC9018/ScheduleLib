`registry history` discovers the academic years offered by the registry and saves
raw course and group names, grouped by academic year and semester. It does not
match against the current schedule or interpret ambiguous group notation.

After the coordinator approves privacy and offline test readiness, a limited
read can use:

```sh
dotnet run --project src/MainCli -- registry history \
  --profile "configured teacher" --year current \
  --local-directory "$PWD/.registry-history" --json
```

Use `--year current` for the first probe: it reads only the academic year selected
by the registry's initial index page, without consulting the machine's date or
requesting historical years. The saved summary uses that actual selected value.
An exact value such as `--year "2024/25"` requests only that offered year.
Omit `--year` to discover every offered year after initial markup validation.
The configured application
`CredentialsResolver<BuiltRegistryConfig>` selects credentials; no credential
arguments, secret-file inspection or current schedule initialization are needed.
The existing password-login builder authenticates once, without token cache
reads/writes. Only the authentication POST and course-list/group-list GET routes are used
by this command. The shared guard also allows the exact read-only lesson-list
route for the separate `history-topics` command below. Grade, evaluation, edit,
create, delete, and reconciliation routes remain blocked. There is no apply switch.

A destination-wide local file lock covers the whole operation across accounts
and cooperating CLI processes. A shared process gate spaces complete responses
by at least the configured `RegistryConfig.httpCallDelay` (default five
seconds), including login, the first request of a new session,
and retries. Zero disables spacing; negative values are rejected when the
registry configuration is built. Reads of HTTP 502/503/504 retry at most once; all other failures,
401/403/429, redirects, and returned login pages stop the operation. Authentication
is never replayed. Do not run other legacy registry operations during a history
probe: those commands do not participate in this history-specific global lease.
After an access block, halt subsequent account probes as well; stopping one
session is not permission to immediately retry with another account.

Set the delay through the existing layered settings command, for example:

```bash
schedulelib config set RegistryConfig.httpCallDelay '"00:00:02"' --scope user --profile '<teacher>'
```

The value uses the .NET `TimeSpan` JSON format. `"00:00:00.5000000"` selects
half a second. An unset value inherits, falling back to five seconds. The same
setting applies to synchronization, history discovery, and history topic reads.

Private JSON summaries go into the explicitly supplied directory whose final
component must be `.registry-history`; that directory is ignored by Git. Each
success creates a new file and preserves prior files and authoritative topics.
Stdout reports only year/course/group counts. Account identifiers, names, links,
raw HTML, credentials, tokens, grades, students and topic contents are excluded
from console results. Raw pages remain in memory and are not saved. The private
summary contains only offered year values, raw course names, semester numbers,
and raw group names. Errors are generic and do not print provider exceptions.
Failures distinguish markup contracts, access rejection, unconfirmed authentication,
unavailable years and ignored year selection using fixed codes. When an HTTP
response is rejected, diagnostics also print only its fixed phase (`Login`,
`Index`, `Groups`, or `Lessons`) and numeric status code. Structural checks also report a fixed `RegistryHistoryDiagnostic` code such as
`CourseLayout`, `GroupRowLinks`, or `TopicTable`, without exception text.
Failures without a known request phase show `Unknown` and `HTTP unknown`. These fields describe the request that stopped the current
run; they do not identify earlier requests or establish why another account's
past request failed. Exception text, response content, request addresses, headers,
and credentials are never printed. Existing Unix output directories are
restricted to their owner; symbolic output directories are rejected before
credentials are resolved.

Offline evidence is `Features/Tests/OnlineRegistry/data/lessons.html`: the
`search` form has no method/action (HTML defaults to GET on the same page) and
uses `select#AnUniv[name=AnUniv]`, with values such as `2024/25`. Navigation uses
that actual form action/name/value, not a guessed historical path. Extra named
controls, POST actions, foreign actions and missing selectors fail explicitly.
Each year response must show the requested option selected before any course
reads. Course and group layouts follow the existing fixture selectors. No script
or external asset is loaded.
An offline check of the existing fixtures matched 10 offered year values,
5 course links and 2 group rows. These fixtures use different deployment roots
(`/edutrack` and `/studregistry`), so each was checked against its own address;
the implementation requires navigation to remain under the configured root.

Before expanding live discovery, the coordinator needs only these non-private
facts from one paced probe: whether the live year control still has the same
GET method/action and field name; which year values are offered and selected;
whether changing one year selects that value in the response; whether historical
course/group containers retain the observed layout; and HTTP status/authentication
success. No cookies, credentials, student rows, topic contents or raw pages need
to be shared. Changed markup is an explicit implementation gap, not permission
to guess another route.

`registry history-topics --profile "configured teacher" --local-directory "$PWD/.registry-history" --json`
adds topic acquisition after the coordinator authorizes resuming live requests.
Serial live validation succeeded for seven configured accounts in October 2026,
producing 72 private CSV candidates and seven inventories. Repeated current
course titles still require group mapping before activating downloaded sources.
Do not invoke it during an access-rejection pause. It shares configured credential
resolution, the destination-wide global lease, and the configured
`RegistryConfig.httpCallDelay` request gate
with `history`; it has no apply switch.

The registry-selected year determines current courses. Previous offered years
are ordered only when they use the observed `YYYY/YY` format; other values stop
with a generic markup error. The reader fetches current course/group/topic
snapshots and previous-year courses matching a current course in the same
semester. Matching ignores letter case, surrounding whitespace and trailing
commas. Diacritics, spelling and semantic aliases remain distinct. The latest
previous-year snapshot containing a nonempty topic is selected independently
for each course. Older group/topic requests stop once that course has a usable
latest snapshot or a newer ambiguity. Repeated current course titles remain
unresolved, but all matching historical snapshots from the newest year with
topics are retained for private review. A blank newest year falls back to older
years. Duplicate matching courses remain unresolved, including a duplicate in
a newer year even when an older year appears unambiguous.

Topic extraction validates the first table's `Data`, `Tema`, `Redactare` header
and reads only the first two cells of each lesson row. The observed registry
sequence number is stripped only when followed by whitespace. Date and lesson
type must match the observed formats; unsupported markup fails without printing
cell contents. Student and attendance tables, view/edit/add links and evaluation
links are never extracted or requested. The guard allows only numeric
`LessonAttendance/LessonsList/<id>` paths with no query or one numeric
`groupId` query parameter.

Downloads create unique private CSV files with the existing `name,lessonType`
columns, separately for every raw source group and lesson type. Unresolved
repeated titles also export the latest historical snapshots as candidates marked
`courseMapping: "ambiguous-course"`; this does not authorize importing them. Rows retain
chronological ordering, repeated topics and blank topic positions. A separate
private candidate inventory records year/course/raw-group/source-URI/date
provenance, current topic snapshots and unresolved course matches. It explicitly
requires group mapping; faculty, language, attendance mode and grade are not
inferred. This inventory is not an import manifest. Authoritative Anton/Nichita
files and earlier downloads remain untouched. Semantic group mapping and a
reviewed import manifest remain coordinator decisions. Console output contains
only counts and fixed failure diagnostics. Synthetic tests verify extraction,
matching, CSV import compatibility, private permissions and blocked routes.

### Atestare dates and regular topics

Topic CSV exports and imports exclude Romanian `Atestare`/`Atestări` labels.
The registry sync preserves existing assessment topics and does not consume a
regular topic for those timetable occurrences. Attendance continues to use the
original lesson occurrence index.

A teacher profile can override dates separately for each course, parent group,
and lesson type, with multiple assessments:

```json
{
  "RegistryConfig": {
    "assessments": [
      {
        "course": "Synthetic course",
        "group": "IA2601",
        "lessonType": "Curs",
        "dates": ["2026-10-12", "2026-11-23"]
      }
    ]
  }
}
```

Place this data inside the teacher profile's existing data node. Course and
group names must match the selected schedule. Rules default to whole-group
lessons; set `subGroup`, `specialization`, or `alternative` for an exact partition. Dates must identify exactly one
scheduled lesson of the configured type. Existing assessments and configured
dates are paired chronologically. Extra existing assessments remain in place;
extra configured dates create numbered `Atestare` topics. Changing a date moves
the assessment topic to that occurrence and lets regular topics fill the freed
occurrence; attendance stays on its original dates. With no matching rule,
existing assessments retain their date and text, including entries outside the
selected timetable. Moving an assessment whose original lesson is outside the
selected timetable stops with an error rather than leaving an orphaned entry.
Use `registry sync` without `--apply` to review the resulting plan first.

When a configured scope has no imported regular topics, a date override flows
the existing registry regular topics around the new assessment dates. If an
imported sequence runs out at an occurrence freed by a moved assessment, planning
stops rather than retaining a duplicate assessment there.
