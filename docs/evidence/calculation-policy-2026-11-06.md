# Calculation policy adoption (2026-11-06)

Continues [#246][rollout] and the reviewed calculation cases under epic #269.

## Decision

Arithmetic and bitwise operator composition no longer triggers automatic staging.
This includes CR0302 standalone values and the operator-count branch of CR0300
argument checks. Simple formula inputs in calls, interpolation and constructions
are accepted. Pipeline composition shares these facts, so arithmetic-only
projections are accepted too. Nested calls, conditional values and computations
inside formatting retain their independent checks.

Consumed mutations such as `values[index++] = item` remain reported. Logical
conditions keep their CR02 rules. No diagnostic severity or warning-exception
configuration changes implement this decision; CR0302 remains enabled.

The shipped agent guidance requires a meaningful abstraction for a cohesive
calculation and meaningful names for separate intermediate concepts. Midpoint
and squared-length helpers can keep their formulas intact. Inclusive interval
length can already be represented by a property; date and Excel column counts
need appropriate range operations where possible. A recurrence gap count and
edit-distance alternatives explain separate concepts. Similar arithmetic alone
is not proof of shared domain policy.

This change establishes the policy and package. It does not complete the manual
semantic review or refactor the previously shown consumer formulas. The existing
inventories remain historical, and #246 stays open.

## Provenance

CodingRules commit `6f5f489e96b1c2bae5dcdd02dd7e0c2fd581dd9f` builds local-feed
package `0.1.0-local.20261106.1`, based on the prior CR0401-removal candidate.
[Analyzer PR #44][analyzer-pr] is stacked on the CR0401-removal PR #43.
Archive SHA-256: `633e9adaf0d19cf4afac1f65bfdfae90353b1b4de7dc33979a8e7bad091d64bb`.
The exported `.codingrules/agent-coding-rules.md` comes from this archive.

## Validation

- All 287 analyzer, 1,386 non-package fixer and eight package tests pass
  (1,681 total); no tests are skipped or retired for this decision.
- Runtime cases cover formulas across locals, arguments, construction, helpers
  and properties; lazy projection evaluation; and increment timing before a
  failed local-array or field-array store.
- Existing caller-information, mutable-receiver, nullable-flow, formatting and
  suppression-scope tests retain coverage using remaining nested-call/mutation
  findings where their old arithmetic finding is now accepted.
- The exact archive passes the computation, condition, predicate and agent-guidance
  consumer gates: accepted source stability, severity transport, runtime behavior,
  saved fixes, second passes and explicit guidance export are verified.
- Both normal restores succeed through the tracked local-feed mapping. All 38
  current projects resolve the exact archive hash and matching analyzer/fixer
  assets; exported guidance matches the archive byte for byte. Leftover asset
  files for two deleted projects are excluded from this current-project audit.
- Both clean Release builds (the solution and separate CLI tests) pass with zero
  errors. Arithmetic-only examples no longer produce CR0302 findings; remaining
  mutation findings stay visible. Consumer runtime code is unchanged.
- `git diff --check` passes in both repositories.

[rollout]: https://github.com/AntonC9018/ScheduleLib/issues/246

[analyzer-pr]: https://github.com/AntonC9018/CodingRules/pull/44
