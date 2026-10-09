# CLI implementation tickets

Implementation breakdown for [#71](https://github.com/AntonC9018/ScheduleLib/issues/71).
GitHub issues are the source of truth for completion and dependencies; these
checklists preserve the agreed acceptance criteria rather than current status.
The implemented CLI uses CommandDotNet typed argument models. See the
[user guide](../README.md) and [verification evidence](cli-verification.md).

## 1. [#186 — Run a portable CommandDotNet lesson query](https://github.com/AntonC9018/ScheduleLib/issues/186)

**Blocked by:** None.

Run query lessons on Windows and Linux using existing schedule sources, with help and invalid arguments handled before initialization.

- [ ] Use CommandDotNet with typed IArgumentModel inputs and reusable models where applicable; command arguments replace the embedded room/day/parity/time example.
- [ ] Run a representative DOCX/XLSX fixture on Linux; isolate Windows Word conversion and preserve originals; retain the existing code resource/configuration path without changing working directory.
- [ ] Provide readable output, a clean versioned JSON result, stderr diagnostics, specified exits and Ctrl+C cancellation; no unrelated teacher identity or browser prompts are required.
- [ ] Keep required portability/dependency cleanup narrow and checks enabled; use an isolated or bypassed cache until the shared-cache slice is available.

## 2. [#187 — Export teacher workbooks with isolated outputs and a shared cache](https://github.com/AntonC9018/ScheduleLib/issues/187)

**Blocked by:** #186.

Generate the existing teacher workbook while simultaneous CLI runs safely share schedule caches and keep their output separate.

- [ ] Expose export teachers-excel with existing latest-period selection and artifact verification; report output paths and deliberate omissions.
- [ ] Default to unique run directories with ownership manifests; explicit destinations never get recursively cleared and collisions with unrelated files fail clearly.
- [ ] Implement cache-dir and no-cache arguments; cache identity accounts for source inputs and relevant configuration, and rebuilds publish complete files atomically.
- [ ] Two processes cannot read partial caches or race for an explicit destination; cancellation/failure retains a usable previous cache and reports partial local output.

## 3. [#188 — Inspect layered JSON settings and existing teacher profiles](https://github.com/AntonC9018/ScheduleLib/issues/188)

**Blocked by:** #186.

Use config show/get/validate/profiles to see effective settings and their sources without loading schedules or accessing providers.

- [ ] Reuse the existing layer engine and supported C# configuration; preserve code-defined academic/teacher/source data and desktop persistence compatibility.
- [ ] Implement base → user → project → selected teacher profile → CLI precedence, including code/user/project profile overlays and one logical profile per teacher.
- [ ] Discover project settings upward or through explicit project selection; resolve JSON paths against their defining file and argument paths against invocation location.
- [ ] Validate a readable versioned JSON envelope, unsupported keys/versions and teacher selection; redact secrets and demonstrate a resolved typed value through the CLI.

## 4. [#189 — Save typed configuration overrides and restore inheritance](https://github.com/AntonC9018/ScheduleLib/issues/189)

**Blocked by:** #188.

Set or unset a value in an explicitly selected user/project scope, then observe the result from a fresh invocation.

- [ ] Deliver config set/unset with required scope and optional existing-teacher selection; validate typed JSON and supported keys before changing any file.
- [ ] Set changes only the requested scope; unset removes its override and restores inheritance; absent JSON files do not disable existing code configuration.
- [ ] Coordinate concurrent editors and atomically replace complete settings files; failed/interrupted writes preserve previous valid settings.
- [ ] Ordinary settings files do not receive credentials/tokens, inspection remains redacted, and roundtrip tests verify resulting behavior rather than only serialization.

## 5. [#190 — Persist inherited setting removal and collection clearing](https://github.com/AntonC9018/ScheduleLib/issues/190)

**Blocked by:** #189.

Remove an inherited block or keyed item, or explicitly clear a collection, and retain that choice across invocations.

- [ ] Deliver config remove/clear with explicit scope using existing block update behavior and registered collection keys.
- [ ] Persist removal/reset/clear actions explicitly; null still means inheritance and an empty ordinary list still follows registered merge behavior.
- [ ] Unsetting the local suppression restores inheritance; unknown item keys and unsupported operations produce useful errors.
- [ ] Demonstrate block suppression and keyed-list removal/clearing after reload while keeping existing desktop serialized files compatible.

## 6. [#191 — Generate PDF and ICS schedule exports](https://github.com/AntonC9018/ScheduleLib/issues/191)

**Blocked by:** #187.

Invoke export pdf or export ics to generate the current all-group/partition/teacher artifacts into a safe run destination.

- [ ] Use CommandDotNet argument models and common output/result contracts; preserve latest-period selection and existing export breadth.
- [ ] Validate representative PDF and ICS artifacts and report missing-date skips; global export does not require an unrelated teacher profile.
- [ ] Propagate cancellation, produce clean JSON results and no automatic Explorer/editor launch.
- [ ] Keep the scalar elective workaround; neither merge PDF PR #68 nor implement new cohorts under this ticket.

## 7. [#192 — Query free hours and export free-room workbooks](https://github.com/AntonC9018/ScheduleLib/issues/192)

**Blocked by:** #187.

Supply groups to query free-hours and generate the existing free-room workbook through export free-rooms.

- [ ] Replace hardcoded query groups with supplied names and resolve/validate them before execution.
- [ ] Preserve both parities, current partition-inclusion modes and all-weekly-period behavior; explain those defaults in help.
- [ ] Use isolated output/manifests for the workbook, text/JSON result contracts for queries, and propagate cancellation.
- [ ] Validate fixture intervals and workbook contents; legitimate empty query results succeed and unknown groups fail clearly.

## 8. [#193 — Generate teacher deadlines and configured pre-defense workbooks](https://github.com/AntonC9018/ScheduleLib/issues/193)

**Blocked by:** #187, #188.

Select a teacher for export lab-deadlines and run export pre-defense when existing commission configuration is supplied.

- [ ] Use resolved teacher-layer settings while retaining existing code-based configuration and academic/source settings.
- [ ] Demonstrate a deadlines workbook and a pre-defense workbook using focused synthetic teacher/commission data.
- [ ] Empty/missing commission data reports a prerequisite instead of claiming a successful empty export; surface existing mixed-partition limits.
- [ ] Use safe output paths, cancellation and structured outcomes without changing real teacher registry data or discovering new commission data.

## 9. [#194 — Generate local website schedule and thesis bundles](https://github.com/AntonC9018/ScheduleLib/issues/194)

**Blocked by:** #187.

Run export website-schedules or export website-theses to produce per-teacher files and a ZIP locally.

- [ ] Preserve current schedule period, thesis inputs, teacher enrichment and slug mapping behavior; no website publication occurs.
- [ ] Report missing-slug and other deliberate omissions in results, with an artifact manifest and ZIP verification.
- [ ] Keep command-result JSON distinct from website JSON artifacts and redirect legacy progress messages to stderr.
- [ ] Use fake website/slug providers or captured non-sensitive fixtures for checks; preserve sources and cancellation/output contracts.

## 10. [#195 — Make Google login explicit for teacher accounts](https://github.com/AntonC9018/ScheduleLib/issues/195)

**Blocked by:** #188.

Use Google auth login/status/logout to provision credentials, while ordinary operations never initiate browser consent.

- [ ] Only explicit login initiates consent; existing user-secrets/client configuration remains compatible.
- [ ] Status and local logout work without loading schedules; teacher/account token stores use coordinated complete-file writes in user state.
- [ ] Ordinary credential resolution uses existing/refreshable authorization or reports exit 4 with the required login command; credentials stay redacted.
- [ ] Fake consent/token-provider tests prove noninteractive operational behavior; record real consent smoke testing as requiring user participation.

## 11. [#196 — Authenticate Microsoft explicitly and download curricula](https://github.com/AntonC9018/ScheduleLib/issues/196)

**Blocked by:** #187, #188.

Provision Microsoft authentication through explicit login and download curricula without unexpected browser consent.

- [ ] Deliver Microsoft auth login/status/logout and curricula download using supported authentication handling rather than private SDK reflection.
- [ ] Keep existing coded source owner/path settings and show resolved inputs; move token state out of invocation directories and coordinate writes.
- [ ] Honor safe output/cancellation contracts and verify portable DOCX downloading with fake Graph responses.
- [ ] Stage legacy conversion to preserve originals; unsupported Linux DOC conversion reports capability prerequisites, and successful Word conversion requires Windows/Word verification.

## 12. [#197 — Preview and apply registry lesson synchronization](https://github.com/AntonC9018/ScheduleLib/issues/197)

**Blocked by:** #188.

Inspect the lesson/topic/attendance reconciliation plan, then execute it only when registry sync receives apply.

- [ ] Use configured teacher credentials without prompts and existing positional matching/extra-lesson policy; preserve coded teacher data.
- [ ] Preview identifies destination and affected creates/updates/deletes and proves zero registry writes using fake navigators.
- [ ] Apply recomputes current state, coordinates the actual local account/destination, and records per-action outcomes.
- [ ] Logged/swallowed submission failures become explicit partial failures; cancellation and uncertain-create outcomes are surfaced without speculative retries.

## 13. [#198 — Preview and apply Moodle grade imports](https://github.com/AntonC9018/ScheduleLib/issues/198)

**Blocked by:** #188.

Inspect mapped grades for a supplied quiz ID, then submit registry forms only through an explicit apply path.

- [ ] Use required quiz-id argument models and existing teacher/Moodle/registry credential and semester configuration.
- [ ] Preserve current mapping/rounding behavior, report omitted/unmatched entries and separate derivation from submission.
- [ ] Fake-provider checks prove preview performs zero writes and apply recomputes and coordinates the destination.
- [ ] Report individual form results, partial failure, unsupported mappings and cancellation; do not change real registry grades during checks.

## 14. [#199 — Preview and apply Google Calendar synchronization](https://github.com/AntonC9018/ScheduleLib/issues/199)

**Blocked by:** #195.

Inspect the actual calendar replacement plan, then perform it only with calendar sync apply.

- [ ] Preview identifies account/calendar, existing deletion/replacement and desired events/ID changes without modifying remote state.
- [ ] Apply uses provisioned Google auth, recomputes, and locks the actual account/destination locally.
- [ ] Preserve current replacement semantics and primary-calendar prohibition; report per-action failures and partially created calendars/events.
- [ ] Fake-provider checks cover no-write preview, apply, cancellation and uncertain creates; do not add an incremental algorithm or claim rollback.

## 15. [#200 — Preview and apply the generated Drive publication bundle](https://github.com/AntonC9018/ScheduleLib/issues/200)

**Blocked by:** #191, #192, #195.

Generate the existing workbook/PDF/ICS bundle, inspect its Drive create/update/delete plan, and publish only with apply.

- [ ] Use the teacher workbook slice plus PDF/ICS and free-room exports; Drive publish remains one operation, not arbitrary command chaining.
- [ ] Preview identifies actual account/folder and remote deletions with zero Drive writes; ownership/result manifests are excluded from published schedule files.
- [ ] Apply recomputes state, coordinates the actual destination locally, and reports create/update/delete action outcomes and partial failures.
- [ ] Fake Drive verification covers cancellation and uncertain creates; preserve current synchronization semantics without promising transactional rollback.

## 16. [#201 — Verify and document the complete CLI across platforms](https://github.com/AntonC9018/ScheduleLib/issues/201)

**Blocked by:** #190, #193, #194, #196, #197, #198, #199, #200.

Deliver a documented, buildable CLI whose complete command surface and agent workflows satisfy the agreed contract.

- [ ] Verify all fifteen original tasks plus configuration/auth operations are reachable with applicable typed argument models and accurate help.
- [ ] Run representative portable artifacts, concurrency checks, no-write previews, clean JSON/error contracts and provider failure cases without live publication.
- [ ] Build publishable Windows/Linux artifacts; record unavailable Windows execution/Word and live consent checks as explicit verification limits rather than claiming success.
- [ ] Provide practical usage and handoff documentation, retain code configuration and deferred scope, and resolve independent review findings before considering the implementation complete.

## Verification boundary

The CLI retains coded academic/teacher/source settings and deferred cohorts/API
scope. Verification uses synthetic fixtures and fake providers without live
publication. Windows execution/Word conversion and user-driven Google/Microsoft
consent require an appropriate environment. Existing NU1510 and DOCX snapshot
observations remain visible; they do not justify blanket suppression or
unreviewed snapshot acceptance.
