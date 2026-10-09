# Drive publication

`drive publish --profile "Curmanschii Anton"` generates the existing bundle and
previews Drive changes. Add `--apply` to generate again, reread remote state and
apply. Common source/cache/project/output/JSON arguments use the existing CLI
contracts. Only `auth login google` opens consent; ordinary publication resolves
provisioned Google credentials and reports authorization failures with exit 4.

The all-teacher workbook, group/partition/teacher PDFs and ICS calendars retain
latest-period selection and existing full breadth. Free-room occupancy retains
all weekly periods. A profile selects authorization/configuration, not an export
filter. Missing ICS date ranges are reported as skips. Coded configuration and
the scalar elective workaround remain supported.

Local generation uses one `RunOutput` lease and atomically publishes artifacts.
Only schedule files generated in this invocation enter the plan; ownership
manifests, lock files, unrelated caller files and stale prior artifacts never
enter uploads. The output manifest describes local generation, independently of
the remote publication result.

Preview reports actual Google permission/account ID, email (when provided),
folder name/ID and each create/update/delete. Matching remains case-insensitive:
every matching remote file (including duplicate names) is updated, and every
unmatched remote file is deleted, including unrelated content in this folder.
This is the existing synchronization behavior, not a minimum-change algorithm.
The first untrashed folder matching the configured name is selected; no folder
is created. A missing folder is a required-input failure.

Apply resolves actual account/folder IDs and acquires their local lock before
rereading folder selection and listing contents. A changed folder selection is
refused. Aliases resolving the same account/destination conflict. These locks
coordinate this machine only, not other machines or external Drive clients.

Actions execute sequentially: delete, create, then update. Completed actions
retain their returned IDs. A rejection is failed; a cancelled or lost response
once a mutation starts is uncertain. Processing stops and remaining actions are
reported as not-attempted. Only caller cancellation returns 130; an HTTP timeout
while reading returns 5, and a timeout after starting a mutation is uncertain and
returns 6. Rejection before remote
application returns 5 (401 returns 4), and completed/uncertain application returns
6. Completed local artifacts are retained. No automatic rollback or retry is
provided; inspect Drive before applying again after uncertainty.

The production adapter bypasses legacy `GoogleApiHelper1` mutation retries and
batch deletion callbacks (which only log individual failures). SDK HTTP retries
and redirects are disabled. Credentials may refresh before sending, but 401
responses do not invoke credential refresh or replay the request. Failed pre-send
OAuth refresh and known 401 rejections report authentication failure with login
guidance before any action completes; earlier completed actions retain a partial
application result. Invalid explicit directory arguments fail with exit 2 before schedule initialization/authorization;
planning transport failures report sanitized provider errors with exit 5.
Uploads use one multipart request instead of resumable chunk recovery and require a returned
file ID. Non-success responses and missing identities cannot report success.
This favors explicit uncertain outcomes over retrying large uploads. Tests use
real local schedule artifacts and an in-memory HTTP transport, never live Drive.
Real Google publication/consent and Windows execution remain unverified.
