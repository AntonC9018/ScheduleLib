# Local CodingRules candidate

The ticket 18 candidate is `Anton.CodingRules.0.1.0.nupkg`, freshly packed from
CodingRules `070dbf9568783497286c953deef89cc8b5dda5d3`. Its SHA-256 is
`cd255dca5aaa7cdec9eb28dd99bb70dc6e3d30a039c97f35b3d39f3f3a55e300`.
Nothing was published. The consumer version remains 0.1.0; the archive's
repository commit and hash distinguish this candidate from the older published
0.1.0 and machine caches.

Pack once in the CodingRules checkout, then reuse that exact archive:

```sh
dotnet pack Package/CodingRules.Package.csproj -c Release --output <candidate-directory>
```

From ScheduleLib:

```sh
python3 build/use-codingrules-candidate.py <candidate-directory>/Anton.CodingRules.0.1.0.nupkg
```

The adoption script never packs, publishes, switches branches, edits another
checkout, or modifies the machine's global NuGet cache. It creates an ignored
root `NuGet.Config` and a feed/cache under ignored `tmp/codingrules-<sha256>`.
The initial cache is empty. Exact-ID source mapping restricts Anton.CodingRules
to that feed; enabled machine feeds remain available for application dependencies.
Ordinary builds and formatting snapshots then use the same configuration.
The script refuses to overwrite a hand-written root NuGet.Config. To return to
normal reviewed release restore, remove only the generated root config and
restore the projects again; do not commit the candidate archive, cache, or config.

After restore it traverses the project-reference graph and compares each
project's restored nupkg and all 26 analyzer/fix/configuration/guidance assets
with the supplied archive. It explicitly invokes
`ExportCodingRulesAgentGuidance` and compares exported bytes. The exported
`.codingrules/agent-coding-rules.md` is committed, and root AGENTS.md directs
agents to read it. Guidance does not export during ordinary build/restore.

Adoption snapshots the bytes and existence of the root `NuGet.Config` and
`.codingrules/agent-coding-rules.md` before writing either file. If configuration
writing, restore, archive/asset verification, graph verification, guidance export,
guidance comparison or the final candidate-record write fails, both files are
restored to their previous bytes or removed if previously absent. The original
failure is re-raised; if filesystem errors prevent rollback, each file's rollback
is still attempted and those errors are attached to the original failure.
On success, `tmp/codingrules-<sha256>/candidate.json` is written only after all
verification and guidance export/comparison complete.

This rollback covers those two files, not an atomic restore of the checkout.
Project `obj/` assets may have been partially or fully rewritten, and the ignored
hash-specific feed/cache (including any partial candidate record) remains. An
export-created directory may also remain empty. After a failure, rerun adoption
or restore with `dotnet restore --force` under the restored configuration before
relying on project assets; adoption always passes `--force` on every attempt.
Process termination or filesystem failures that prevent cleanup cannot guarantee
rollback. The cache and restored project assets are not transaction records.

The fresh-cache restore also exposed existing NU1902/NU1903 advisories in
ScheduleLib's application dependencies (`System.Security.Cryptography.Pkcs`
and `System.Security.Cryptography.Xml` 10.0.10). Validation keeps those warnings
visible with this **command-scoped** exception; no permanent audit exception or
dependency change is part of adoption:

```sh
WarningsNotAsErrors='NU1902;NU1903' python3 build/use-codingrules-candidate.py <candidate-directory>/Anton.CodingRules.0.1.0.nupkg
```

New CR and selected upstream findings remain visible as warnings alongside the
previous legacy warnings; other compiler/analyzer warnings retain the existing
policy. A clean build or formatting pass is not evidence that manual findings
or unresolved policies are complete. See the ticket 18 coverage ledger.
