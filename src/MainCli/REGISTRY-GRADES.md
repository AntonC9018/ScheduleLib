`registry import-grades --quiz-id ID --profile TEACHER` previews grade form
changes. Add `--apply` to recompute Moodle attempts and registry forms under the
same local actual-login/base-destination lock used by `registry sync`. Preview
never fills or submits registry grade forms. No preview is saved or replayed.
Both providers use existing configured credentials, without consent or prompts;
the registry session and initialized scoped settings are reused. The academic
semester and teacher data remain configured in C#.

Quiz IDs must be positive decimal IDs. Common source/settings/result arguments
are supported, including `--project`, `--profile`, `--data-dir`, `--no-cache`,
`--cache-dir` and `--json`. There is no output directory argument.

The existing handler derives the course, qualification, study year and test
number from the Moodle breadcrumb, and matches registry students by names with
its existing diacritic/patronymic tolerance. It swaps Moodle first/last name
order, keeps the last graded attempt for a repeated name, and rounds with
`Math.Round` (nearest even). It consumes a matched Moodle grade once across
registry groups. It does not introduce cohort or semantic matching. Expelled
students are omitted, including two-part names followed by `exmatr`. Missing
grades, replaced attempts, unparseable names, unsupported paths/groups/test
anchors and unmatched students appear as notices. Forms without mapped grades
are omitted. Unmapped form fields retain their existing values.

The Moodle report uses the existing page-size request of 2000 and does not page
through larger reports; the command warns about that limitation. Inspect the
preview, notices and report completeness before applying. Unsupported mappings
are deliberate omissions, so an empty preview/apply can succeed with notices.
Malformed required form markup fails planning before any registry submission.

Schema-1 JSON includes the actual registry target, quiz ID, notices and per-form
outcomes, with student source/rounded/previous grades and form destinations.
Text also lists these grades. Diagnostics use stderr. Do not treat results as
anonymous data: they intentionally contain student names and grades, but no
passwords or tokens.

Outcomes are `planned`, `completed`, `failed`, `uncertain`, `cancelled` or
`not-attempted`. Application stops at the first failed form and never retries.
A rejected submission is failed; an unconfirmed transport response is uncertain.
Successful submission requires a fresh read with matching saved grade values.
Grade POSTs bypass authentication replay, including after HTTP 401; other
legacy requester retry behavior is preserved.
Ctrl+C cancels reads and execution. AngleSharp submission cannot be natively
cancelled, so cancellation stops awaiting the response and disposes the session;
an already-started submission is uncertain. Inspect the registry before any
manual retry. Completed and unattempted form results are retained.

Exits follow the shared CLI contract: 0 successful preview/apply; 1 unexpected
failure; 2 invalid
arguments; 3 invalid/missing configuration/input; 4 configured registry/Moodle
authentication failure; 5 failure before submission; 6 attempted/partial
application failure; 7 conflicting local account operation; 8 unsupported
capability; 130 cancellation. JSON is emitted once after session/lock cleanup,
including cleanup failures. Locks coordinate this machine only.

Verification uses fake providers, in-memory production HTTP adapters and portable
schedule/HTML fixtures only. Moodle login requires a successful response with
a logout link; rejected login exits 4 before report scraping. Login cancellation
stops waiting, disposes its owned session and releases the registry account lock.
Windows and real Moodle/registry authentication/submission remain unverified.
