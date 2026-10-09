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
by at least five seconds, including login, the first request of a new session,
and retries. Reads of HTTP 502/503/504 retry at most once; all other failures,
401/403/429, redirects, and returned login pages stop the operation. Authentication
is never replayed. Do not run other legacy registry operations during a history
probe: those commands do not participate in this history-specific global lease.
After an access block, halt subsequent account probes as well; stopping one
session is not permission to immediately retry with another account.

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
`Index`, `Groups`, or `Lessons`) and numeric status code. Other failures show `Unknown`
and `HTTP unknown`. These fields describe the request that stopped the current
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
No live validation has been performed for this topic extraction implementation.
Do not invoke it during an access-rejection pause. It shares configured credential
resolution, the destination-wide global lease, and the five-second request gate
with `history`; it has no apply switch.

The registry-selected year determines current courses. Previous offered years
are ordered only when they use the observed `YYYY/YY` format; other values stop
with a generic markup error. The reader fetches current course/group/topic
snapshots and previous-year courses matching a current course in the same
semester. Matching ignores letter case, surrounding whitespace and trailing
commas. Diacritics, spelling and semantic aliases remain distinct. The latest
previous-year snapshot containing a nonempty topic is selected independently
for each course. Duplicate matching courses remain unresolved, including a
duplicate in a newer year even when an older year appears unambiguous.

Topic extraction validates the first table's `Data`, `Tema`, `Redactare` header
and reads only the first two cells of each lesson row. The observed registry
sequence number is stripped only when followed by whitespace. Date and lesson
type must match the observed formats; unsupported markup fails without printing
cell contents. Student and attendance tables, view/edit/add links and evaluation
links are never extracted or requested. The guard allows only numeric
`LessonAttendance/LessonsList/<id>` paths with no query or one numeric
`groupId` query parameter.

Downloads create unique private CSV files with the existing `name,lessonType`
columns, separately for every raw source group and lesson type. Rows retain
chronological ordering, repeated topics and blank topic positions. A separate
private candidate inventory records year/course/raw-group/source-URI/date
provenance, current topic snapshots and unresolved course matches. It explicitly
requires group mapping; faculty, language, attendance mode and grade are not
inferred. This inventory is not an import manifest. Authoritative Anton/Nichita
files and earlier downloads remain untouched. Semantic group mapping and a
reviewed import manifest remain coordinator decisions. Console output contains
only counts and fixed failure diagnostics. Synthetic tests verify extraction,
matching, CSV import compatibility, private permissions and blocked routes.
