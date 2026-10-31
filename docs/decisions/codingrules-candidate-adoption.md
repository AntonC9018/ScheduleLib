# Local CodingRules candidate adoption

- **Status:** accepted; implemented by `build/use-codingrules-candidate.py`.
- **Scope:** consuming an unpublished `Anton.CodingRules` archive ([#244][244]).

## Decision

Pack once in the CodingRules checkout, then reuse that exact archive:

```sh
dotnet pack Package/CodingRules.Package.csproj -c Release --output <candidate-directory>
python3 build/use-codingrules-candidate.py <candidate-directory>/Anton.CodingRules.0.1.0.nupkg
```

- The script never packs, publishes, switches branches, edits another checkout or modifies the
  machine's global NuGet cache.
- It creates an ignored root `NuGet.Config` and a feed/cache under ignored
  `tmp/codingrules-<sha256>`. Exact-ID source mapping restricts `Anton.CodingRules` to that feed;
  other feeds stay available. It refuses to overwrite a hand-written root `NuGet.Config`.
- Consumer version stays 0.1.0; the archive's repository commit and hash distinguish a candidate
  from the published 0.1.0.
- After restore it compares each project's restored nupkg and all 26 analyzer/fix/configuration/
  guidance assets with the archive across the project-reference graph, explicitly runs
  `ExportCodingRulesAgentGuidance` and compares the exported bytes. The exported
  `.codingrules/agent-coding-rules.md` is committed and `AGENTS.md` directs agents to read it;
  ordinary build/restore does not export guidance.
- `tmp/codingrules-<sha256>/candidate.json` is written only after verification and export.
- Rollback: the bytes or absence of root `NuGet.Config` and `.codingrules/agent-coding-rules.md`
  are snapshotted before the first write and restored on any later failure; the original failure
  is re-raised with cleanup errors attached. This is not an atomic checkout restore: project
  `obj/` assets, the ignored feed/cache and any partial candidate record remain, so a retry must
  force a restore (adoption always passes `--force`).
- To return to normal release restore, remove only the generated root config and restore again.
  Never commit the archive, cache or config.
- Validation keeps NU1902/NU1903 advisories visible with a command-scoped exception:
  `WarningsNotAsErrors='NU1902;NU1903'`. No permanent audit exception or dependency change is
  part of adoption.

## Links

- [Adoption validation](../evidence/codingrules-adoption-validation.md) and
  [candidate history](../evidence/codingrules-candidates.md).

[244]: https://github.com/AntonC9/ScheduleLib/issues/244
