# CodingRules candidate history

All candidates are local, unpublished `Anton.CodingRules.0.1.0.nupkg` archives of version 0.1.0;
the repository commit and SHA-256 distinguish them. Latest first. Adoption procedure:
[decision](../decisions/codingrules-candidate-adoption.md).

- **`97de786`** (2026-10-28), SHA-256
  `ea2022c5760ba614e40003ab4d933d0c48b0fa3921e6f1d5e93ad0936509da7c`:
  1,488 analyzer/fixer/package cases and 11 consumer gates; single-construction allowance.
- **`2abd37a`**: reviewed candidate after CodeRabbit follow-up, SHA-256
  `b87e5d8e8d5c87a7ade0dbc3583c15a27ce354c48e2cfb1c64f457bddb81b50a`; 1,475 solution tests, 11
  gates.
- **`f2902d4`** (2026-10-27), SHA-256
  `79972b6df1af48368f3e6e9cff36c01e5dbb858a82c8ba5d59b654a78be7edd6`: 1,470 tests, 11 gates.
- **`7fda0eb`** (2026-10-26), SHA-256
  `79029420d6889484b8eb4f0dc5f243813941e52ee5d0df5a4dcdf99f363468a2`: 1,446 tests, 11 gates
  including the reflection-signature fixture.
- **`9d24f23`** (2026-10-25), SHA-256
  `c3291f06d0dd381455e1328f552c10d01a88c2a31191d48de09453376702eeb5`: 1,444 tests (262
  analyzer, 1,182 code-fix/package), 11 gates.
- **`0f02bab`** (2026-10-25), SHA-256
  `e0268e2bc377add6b2f5265fcb4fcc162c8ea4f276511a2ffb071690b978caca`: 1,401 tests, ten gates.
- **`dfd239d`** (2026-10-24), SHA-256
  `359943fba8b92147885fe1905ad3799f6b3ea7812de1bba73c74f5bd7a951145`: 1,395 tests, ten gates;
  CR0301 `nameof` fix. Predecessors: `d8803f6` (unbound-generic CR0500 crash) and `a78e907`
  (first adopted candidate), SHA-256
  `b309e17db91f1c8bfd2fc043dd480d0532bc4d63b83d75ffb6b3a5b8b4f560ec`, 1,372 tests, nine gates,
  verified across the 24-project CLI test graph.
- **`070dbf9`** (ticket 18 adoption), SHA-256
  `cd255dca5aaa7cdec9eb28dd99bb70dc6e3d30a039c97f35b3d39f3f3a55e300`: see
  [adoption validation](codingrules-adoption-validation.md). It supersedes `4749125`.

`7fda0eb` also covers the exception-context migration; `0f02bab` and later are stamped in the
[rollout record](analyzer-rollout.md). Every candidate passed on AWS; restoring locally remains
unverified (see [tooling](analyzer-tooling-costs.md)).
