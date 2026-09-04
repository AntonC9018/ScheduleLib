# Task Management (Beads)

This project uses **bd** (beads) for issue tracking. Run `bd prime` for full workflow context.
Full CLI guidance: `.agents/skills/beads/SKILL.md`.

## Architecture

Issues live in a local Dolt database (`.beads/dolt/`); cross-machine sync uses
`bd dolt push/pull` (a git-compatible protocol), stored under `refs/dolt/data`
on the git remote — separate from `refs/heads/*` where code lives.
`.beads/issues.jsonl` is a passive export, not the wire protocol.

See [SYNC_CONCEPTS.md](https://github.com/gastownhall/beads/blob/main/docs/SYNC_CONCEPTS.md)
for the one-screen overview and anti-patterns (don't treat JSONL as the source
of truth; don't `bd import` during normal operation; don't reach for
third-party Dolt hosting before trying the default).

## Quick Reference

```bash
bd ready                # Find available work
bd show <id>            # View issue details
bd update <id> --claim  # Claim work atomically
bd close <id>           # Complete work
bd dolt push            # Push beads data to remote
```

## Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists.
- Run `bd prime` for detailed command reference and session close protocol.
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files.
- Do not use `bd edit`; it opens an interactive editor. Use `bd update` flags instead.

## Agent Context Profiles

The Beads workflow below is task-tracking guidance, not permission to override
repository, user, or orchestrator instructions.

- **Conservative (default)**: Use `bd` for task tracking. Do not run git commits, git pushes, or Dolt remote sync unless explicitly asked. At handoff, report changed files, validation, and suggested next commands.
- **Minimal**: Keep tool instruction files as pointers to `bd prime`; use the same conservative git policy unless active instructions say otherwise.
- **Team-maintainer**: Only when the repository explicitly opts in, agents may close beads, run quality gates, commit, and push as part of session close. A current "do not commit" or "do not push" instruction still wins.

## Session Completion

This protocol applies when ending a Beads implementation workflow. It is subordinate to explicit user, repository, and orchestrator instructions.

1. **File issues for remaining work** — create beads for anything that needs follow-up.
2. **Run quality gates** (if code changed) — tests, linters, builds.
3. **Update issue status** — close finished work, update in-progress items.
4. **Handle git/sync by active profile**:
   ```bash
   # Conservative/minimal/default: report status and proposed commands; wait for approval.
   git status

   # Team-maintainer opt-in only, unless current instructions forbid it:
   git pull --rebase
   bd dolt push
   git push
   git status
   ```
5. **Hand off** — summarize changes, validation, issue status, and any blocked sync/commit/push step.

**Critical rules:**
- Explicit user or orchestrator instructions override this Beads block.
- Do not commit or push without clear authority from the active profile or the current user request.
- If a required sync or push is blocked, stop and report the exact command and error.
