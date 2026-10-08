# Historical Task Archive — 2026-10-08

GitHub Issues replaced the former task tracker in [issue #69](https://github.com/AntonC9018/ScheduleLib/issues/69).
This directory is a read-only migration/recovery archive, not an active task database.

- `issues.jsonl`: 107 original issue records with descriptions, notes, acceptance criteria, labels, priorities, timestamps, dependencies, and 141 comments; also the shared-memory record.
- `interactions.jsonl`: the original exported audit log.
- `memories.json`: the original shared technical memory, also carried forward into [development-notes.md](../../development-notes.md).
- `issue-map.json`: every legacy issue ID mapped to its GitHub issue number and URL.
- `migration-progress.json`: the checkpoint of migrated/finalized issue records.
- `relationships.json`: the verification record for GitHub parent and blocking relationships.

GitHub issue bodies retain the original discussion with author/timestamp attribution. GitHub's own issue creation and closing dates are migration dates; the original dates remain in the archived records and historical metadata. Descriptions and discussion references to legacy IDs are linked to the migrated issues.

A full local database backup (including Dolt history and working state) and a compressed copy of the former workspace were saved outside the repository at:

`/home/anton/.local/share/ScheduleLib/migrations/beads-2026-10-08/`

The portable JSONL export and issue map support recovery without the original database or its tooling. The full local backup is retained for deeper historical recovery. The former remote database ref is left intact as historical recovery material; active work uses GitHub exclusively.
