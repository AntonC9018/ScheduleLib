# The formatter retains CleanupCode alongside package fixes

- **Status:** accepted; implemented by `build/format.sh` (ticket 18, [#244][244]).
- **Scope:** `build/format.sh --style`.

## Decision

- ScheduleLib uses SDK 11.0.100-preview.4.26230.115 and CleanupCode 2026.2.3.1 (runtime 10.0.13).
  The package-only analyzer + whitespace workflow proves A1/A2/A3/A12 but does not add
  control-statement braces or trailing commas, split same-line statements, or wrap long calls
  (`build/tests/fixtures/Formatting/`). CleanupCode and its syntax-only solution host are
  therefore retained.
- Package fixes run in a separate snapshot using the real consumer project/reference context,
  not the syntax-only project.
- The opt-in `--style` pass allows only CR0800/CR0801/CR0802/RCS0042/RCS1016 (later also required
  braces, cast spacing and CR0805, see [formatting](../formatting.md)). Explicit upstream
  severities and single-line-block preservation are committed in `.editorconfig`.
- The isolated analyzer process sets `ScheduleLibFormatting=true` to omit the legacy
  `WarningsNotAsErrors` list so the standard CLI does not filter those IDs; ordinary builds keep
  their warning policy.
- The wrapper repeats layout/style rounds to a byte fixed point before copying source back, with
  a four-round limit and an explicit no-write failure on nonconvergence. Preflight and per-write
  concurrent-edit protection remain.
- Use `build/format.sh --project <project> --path <changed-file> --style` for routine scoped
  work; do not run a whole-solution semantic cleanup on every compilation.

## Links

- [Formatter cost and timings](../evidence/analyzer-tooling-costs.md).

[244]: https://github.com/AntonC9/ScheduleLib/issues/244
