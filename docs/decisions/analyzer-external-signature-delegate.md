# External-signature marker for the QuestPDF border delegate (CR1000)

- **Status:** accepted and implemented (2026-10-25/26).
- **Scope:** `BorderDelegate` in `GroupColumnScheduleTableDocumentGenerator.cs`.

## Decision

The private delegate `BorderDelegate(IContainer element, float top, float bottom, float left,
float right)` binds a nonpublic QuestPDF `BorderExtensions` method through
`CreateDelegate<BorderDelegate>()`. It declares
`[ExternalSignature(typeof(BorderExtensions), "Border")]`, which keeps the reflection-imposed
signature intact.

## Rationale

Grouping the arguments would change the required reflection signature. The marker is specific to
this delegate: there is no blanket delegate exemption or suppression, and it does not change the
grouping policy for owned contracts.
