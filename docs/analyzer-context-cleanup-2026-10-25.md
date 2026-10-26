# Typed exception context and explicit guard cleanup

The follow-up to the [first review](analyzer-review-2026-10-25.md) implements
its exception-context migration and the approved CR0200–CR0203 control-flow
rewrites. [Remaining examples](analyzer-manual-examples-2026-10-25.md) group tuple
shapes and present decision functions, fluent layout, owned contracts and the
single repeated timestamp-formatting case. Those examples are review proposals.
Command inheritance (CR1400) is unchanged.

## Exception behavior

Meaningful named exception subtypes expose failure data through typed get-only
properties. Existing domain exception factories retain their base hierarchy;
distinct failure variants have named concrete types. Templates render eagerly
inside constructors, preserving the original text and culture. Mutable lists,
span-backed parser names and PDF-cell groups are snapshotted. Existing tests
now assert the intended concrete subtype, rather than weakening their checks
to arbitrary exceptions. Additional regression cases check message text,
base catches and failure-time snapshots.

`InvalidDataException` is sealed. ZIP-content and pre-defense member-name
failures keep that exact framework type and its existing inner exception
behavior. Each attaches a named immutable context value to `Exception.Data`,
keyed by its context type. Their two factory constructions have narrowly scoped
CR1600 suppressions documenting this alternative; no family is disabled.

## Control flow and reflection

Validation chains use separate guards at the original site. Matching predicates
use block lambdas with early returns, and catch filters use named local
functions evaluated inside the filter. The RegistryHistory URL checks retain
their order, same-origin policy, rejection message and diagnostic classification.

The private QuestPDF `BorderDelegate` now declares
`[ExternalSignature(typeof(BorderExtensions), "Border")]`. Its externally
imposed signature remains intact for reflection. The marker is specific to this
delegate and does not change the grouping policy for owned contracts.

## Candidate and validation provenance

The candidate is CodingRules `7fda0ebdfc3980da4fec43b5448a3d00d9ff842e`.
`Anton.CodingRules.0.1.0.nupkg` has SHA-256
`79029420d6889484b8eb4f0dc5f243813941e52ee5d0df5a4dcdf99f363468a2`.
AWS validation passed all 1,446 analyzer/code-fix/package tests and all 11
consumer gates, including the reflection-signature consumer fixture.

The local candidate feed is configured with that exact archive. Local restore
could not be verified: the installed Nix .NET 11 runtime fails to load CoreCLR
because its `libcoreclr.so` requires `GLIBC_2.44` while the loaded `libm.so.6`
comes from glibc 2.42. Application builds/tests run on the existing AWS worker;
this task does not alter the machine's Nix configuration.

Formatting runs after source changes stabilize, scoped to the changed files.
[Formatting guidance](formatting.md) records its measured cost and the absence
of a persistent semantic result cache.

The initial changed-file layout/style apply took 301.3 seconds for 112 solution
files and 87.1 seconds for five CLI test files. After the final corrections,
unchanged checks took 100.5 and 46.4 seconds respectively, with zero source
changes. The successful named-argument passes took 62.5 and 18.8 seconds. These
measurements describe different source selections during this cleanup; they
are not a controlled speedup comparison or evidence of incremental caching.

## Final validation — 2026-10-26

ScheduleLib source commit `7d235fe` contains the verified C# snapshot.
Both Release rebuilds pass with zero errors. The unfiltered solution has 715
cases and the CLI suite 770; their 11 and three failures match the recorded
baseline exactly. All other cases pass when the known failing tests tracked
by #248 are excluded. No new test failure is introduced.

The combined inventory falls from 370 to **180 source findings**; the original
pre-cleanup inventory had 7,642. CR1600, CR0200–CR0203 and CR0500 are clear.
Two context-preserving sealed-framework factories use the narrow suppressions
described above; no diagnostic family is broadly disabled. The remaining
findings are:

- 94 tuple-data locations, grouped in the review document.
- 31 computation-step findings (CR0300–CR0302).
- 21 decision-function findings (CR0303).
- 22 fluent-layout findings: 13 assertion-text-sensitive sites and nine
  interpolation sites.
- Three owned Google contracts and one repeated calendar-timestamp formatter.
- Eight deferred inheritance findings. Seven are the existing review cases;
  the eighth is RegistrySubmissionRejectedException, now a base for the typed
  operation-rejection subtype so existing base catches keep working. Its
  abstract-base/general-subtype design remains part of the deferred review.

The 55 dependency-advisory rows are retained. The final source archive was
copied back only after validation and byte-compared with the saved AWS output.
The candidate remains unpublished and the commits remain local.
