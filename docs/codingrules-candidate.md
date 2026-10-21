# Local CodingRules candidate

The ticket 18 candidate is `Anton.CodingRules.0.1.0.nupkg`, freshly packed from
CodingRules `4749125e707a114e265bb06a19a6474134a540dd`. Its SHA-256 is
`88addc649ea0ccd9a0b6d5c52150e3e916a12958d634b0bb7f31a4c7abdfb81e`.
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

After restore it compares the restored nupkg and all 26 analyzer/fix/configuration/
guidance assets with the supplied archive. It explicitly invokes
`ExportCodingRulesAgentGuidance` and compares exported bytes. The exported
`.codingrules/agent-coding-rules.md` is committed, and root AGENTS.md directs
agents to read it. Guidance does not export during ordinary build/restore.

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
