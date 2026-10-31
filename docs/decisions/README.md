# Analyzer and formatter decisions

Durable ScheduleLib-specific decisions, one per file. Each states the decision, rationale and
boundaries; measurements and history live in [evidence](../evidence/README.md).

General analyzer rules, their boundaries and accepted cases belong to CodingRules: see its
[specification][spec] and the exported [agent guidance](../../.codingrules/agent-coding-rules.md).

- [Analyzer diagnostics stay visible](analyzer-diagnostics-stay-visible.md)
- [Typed exception hierarchies (CR1600)](analyzer-exception-context-cr1600.md)
- [External-signature marker for the QuestPDF border delegate
  (CR1000)](analyzer-external-signature-delegate.md)
- [Overload pairs required by external interfaces (CR1001,
  CR1500)](analyzer-overload-builders-cr1001.md)
- [Temporary suppressions pending design audits](analyzer-temporary-suppressions.md)
- [Tuple API migration to record structs (CR0900)](analyzer-tuple-record-structs.md)
- [Local CodingRules candidate adoption](codingrules-candidate-adoption.md)
- [The formatter retains CleanupCode alongside package fixes](formatter-retains-cleanupcode.md)
- [Google token context, credential request and timestamp helper (CR1000,
  CR1300)](google-token-context.md)

[spec]: https://github.com/AntonC9018/CodingRules/blob/main/docs/spec.md
