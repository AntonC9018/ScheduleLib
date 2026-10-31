# Tuple API migration to record structs (CR0900)

- **Status:** accepted 2026-10-26; implemented in ScheduleLib `b1c9aed`.
- **Scope:** the original 94 CR0900 tuple locations.

## Decision

The migration intentionally changes tuple-based signatures. In-repository callers migrate
together; external callers constructing tuple literals need the corresponding records. No
implicit tuple conversions preserve the removed shape.

`UnparsedModifierGroup` keeps mutable fields because its lexer is advanced through a ref local.

## Links

- [Rollout evidence](../evidence/analyzer-rollout.md); `b1c9aed` lists the introduced record structs.
