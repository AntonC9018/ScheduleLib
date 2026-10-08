# Task Management (GitHub Issues)

[GitHub Issues](https://github.com/AntonC9018/ScheduleLib/issues) is the single
source of truth for project tasks, blockers, decisions, and shared work status.
Use the authenticated `gh` CLI. Local plans can organize a turn but must not
replace issues for work that needs to survive a handoff.

## Start work

```bash
gh auth status
gh issue list --repo AntonC9018/ScheduleLib --state open --limit 100
gh issue view NUMBER --repo AntonC9018/ScheduleLib --comments
```

Read the issue, discussion, parent, and blocking issues before starting. A
parent/sub-issue relationship groups work; it does not make the parent a blocker.
Work is ready when its actual blockers are closed and no other agent owns the
current implementation.

Before starting implementation, make sure the issue exists and matches the
user's scope. Check assignments and recent comments, then record the claim:

```bash
gh issue edit NUMBER --repo AntonC9018/ScheduleLib --add-assignee @me --add-label status:in-progress
gh issue comment NUMBER --repo AntonC9018/ScheduleLib --body-file /tmp/work-claim.md
```

A claim comment should identify the agent/thread, its scope, and any branch or
worktree. Assignment and labels alone are not an atomic lock: concurrent agents
may share a GitHub account. Re-read ownership before editing, coordinate with
existing owners, and use separate worktrees for overlapping implementation.

## Create and relate work

Search existing issues before creating another one. Write multiline content to
a file and pass it through `--body-file`; do not use an interactive editor.

```bash
gh issue create --repo AntonC9018/ScheduleLib --title "Concrete task" --body-file /tmp/task.md
gh issue edit CHILD --repo AntonC9018/ScheduleLib --parent PARENT
gh issue edit NUMBER --repo AntonC9018/ScheduleLib --add-blocked-by BLOCKER
```

Use native GitHub sub-issues and blocking relationships, and explain any
non-obvious ordering in the description. A follow-up discovered during work
should link back to its source issue.

Migrated priorities use `priority:P0` through `priority:P4` (P0 is highest).
`status:in-progress` marks active work. Original labels and authors/timestamps
are preserved on migrated issues. Preserve existing labels when updating work.

## Finish or hand off

Record what changed, relevant validation, limitations, and remaining work.
Close the issue only when its requested scope is actually complete:

```bash
gh issue comment NUMBER --repo AntonC9018/ScheduleLib --body-file /tmp/result.md
gh issue edit NUMBER --repo AntonC9018/ScheduleLib --remove-label status:in-progress
gh issue close NUMBER --repo AntonC9018/ScheduleLib --reason completed
```

For unfinished work, leave the issue open and record a concrete handoff. Release
an active claim if work is no longer underway. Do not close an issue merely
because this turn ends.

Do not commit, push, merge, deploy, or publish without authority from the active
user request or repository policy. Report uncommitted changes at handoff.

## Technical knowledge and migration archive

Keep durable technical facts in [development-notes.md](development-notes.md) or
the relevant domain document. Record pending decisions in GitHub issues.

The [2026-10-08 archive](archive/beads-2026-10-08/README.md) preserves the former
tracker's issue export, comments, audit records, and old-ID-to-GitHub mapping.
It is historical recovery material, not an active task store. No task-tracker
database, synchronization service, or session/git hook is required for the
GitHub workflow.
