# LSP over MCP for ScheduleLib

Research date: 2026-09-03. Question: can an agent working in this repo get real
C# code intelligence (definitions, references, diagnostics, rename) through an
LSP exposed as an MCP server, wired into ZCode and OpenAI Codex?

## TL;DR

- Yes, and there are two workable shapes: a **generic LSP→MCP bridge** wrapping
  a C# language server, or a **purpose-built Roslyn MCP server** that skips LSP
  and talks to Roslyn directly.
- The bridge ecosystem is thin for C#: the main generic bridge
  ([isaacphi/mcp-language-server](https://github.com/isaacphi/mcp-language-server))
  is Go, beta, tested against gopls/rust-analyzer/pyright/ts-ls/clangd/zls only —
  C# is untested, and building it here requires Go ≥ 1.24 (this machine has 1.21).
- The lowest-friction option on this machine today is
  [`dotnet-roslyn-mcp`](https://www.nuget.org/packages/dotnet-roslyn-mcp)
  (a `dotnet tool`, solution-aware via `DOTNET_SOLUTION_PATH`) — but it is
  early-stage (v0.0.3, Oct 2025).
- **ZCode has no native LSP support**, so MCP is the only route there
  (zcode-configuration-guide skill, §Plugins): plugin `lspServers`
  fields are "recorded but not executed".
- **Codex (v0.121.0+) has a better-than-MCP option**: Microsoft's official
  [`dotnet/skills`](https://github.com/dotnet/skills) plugin ships a C# LSP
  integration natively (`codex plugin marketplace add dotnet/skills`). In ZCode
  the same plugin's skills load, but its LSP part does not run.
- Because this repo builds with `TreatWarningsAsErrors` and targets preview
  `net11.0` (see `Directory.Build.props`), live diagnostics are the highest-value
  tool an LSP brings; navigation (find-references) is second.

## Background

- **LSP** (Language Server Protocol) is how editors talk to language tooling:
  a language server loads the solution/project, then answers definition,
  references, diagnostics, hover, rename requests over stdio/JSON-RPC.
- **MCP** (Model Context Protocol) is how agent clients expose tools to the
  model. An agent has no editor, so it can't speak LSP — hence bridges that run
  a language server as a child process and re-expose a handful of LSP requests
  as MCP tools.
- Why bother when the agent can already grep and read files: grep finds text,
  not symbols. `Find References` on `GroupPartitionKey` won't be fooled by comments,
  string literals, or same-named symbols in other namespaces, and rename via
  Roslyn is semantics-aware. Diagnostics catch build breaks without a full
  `dotnet build` round-trip.

## What each client supports

| Client | MCP servers | Native LSP | Notes |
|---|---|---|---|
| ZCode | yes — workspace `<repo>/.zcode/config.json` → `mcp.servers`, or user `~/.zcode/cli/config.json` | no | Plugin manifests may declare `lspServers`, but ZCode records the field without executing it. Tools surface as `mcp__<server>__<tool>`. |
| OpenAI Codex CLI/desktop | yes — `~/.codex/config.toml` `[mcp_servers.<name>]` (already used here by `node_repl`, `cua_repl`) | yes (v0.121.0+, via plugins) | Official Microsoft `dotnet` plugin provides the C# LSP through this path. |

Sources: ZCode — the bundled zcode-guide plugin skills
(`zcode-configuration-guide/SKILL.md` lines 32–37, 78; `diagnosing-mcp/SKILL.md`
§1–2) in `C:\Users\Anton\.zcode\cli\plugins\cache\zcode-plugins-official\zcode-guide\0.1.0\skills\`;
Codex — [official MCP docs](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)
(`developers.openai.com/codex/mcp` now 308-redirects there), plus the existing
`[mcp_servers.*]` tables in `C:\Users\Anton\.codex\config.toml`.

## Generic LSP→MCP bridges

| Bridge | Language | Tools | State (2026-09) | Fit here |
|---|---|---|---|---|
| [isaacphi/mcp-language-server](https://github.com/isaacphi/mcp-language-server) | Go | `definition`, `references`, `diagnostics`, `hover`, `rename_symbol`, `edit_file` | v0.1.1 (May 2026), ~1.6k stars, self-described beta. Flags: `--workspace <path> --lsp <server>`, args after `--` go to the server. | Needs Go ≥ 1.24 ([go.mod](https://raw.githubusercontent.com/isaacphi/mcp-language-server/main/go.mod)); this machine has Go 1.21. Release assets don't list a confirmed windows/amd64 binary. C# untested upstream. |
| [t3ta/mcp-language-server](https://github.com/t3ta/mcp-language-server) | TypeScript | multiple language servers in one workspace via a unified MCP interface | active fork lineage | `npx`-style; would need a per-language config incl. a C# server command |
| [STRd6/mcp-language-server](https://pkg.go.dev/github.com/STRd6/mcp-language-server) | Go (fork of isaacphi) | isaacphi's set + extras, stability fixes | community fork | Same Go-version caveat |
| [morrow-addref/LSP-MCP](https://glama.ai/mcp/servers/morrow-addref/LSP-MCP) | — | minimal bridge incl. `Microsoft.CodeAnalysis.LanguageServer.exe` | small project | directly targets the MS Roslyn server |
| Fannon/mcp-language-server | Node | — | **repo gone (404)** as of 2026-09-03 | dead end |

The earlier-generation flags some blog posts show (`--workspaceFolders`,
`--launchCommand`) belong to the pre-0.1.x Rust versions of isaacphi's project;
the current Go rewrite uses `--workspace`/`--lsp` (v0.1.0 renamed tools and
dropped codelens).

## Purpose-built Roslyn MCP servers (no LSP bridge needed)

| Server | Form | Tools | State | Fit here |
|---|---|---|---|---|
| [`dotnet-roslyn-mcp`](https://www.nuget.org/packages/dotnet-roslyn-mcp) ([src: brendankowitz/vs-ide-mcp](https://github.com/brendankowitz/vs-ide-mcp)) | `dotnet tool install --global dotnet-roslyn-mcp --version 0.0.3`; stdio; env `DOTNET_SOLUTION_PATH` | 18: `find_references`, `find_callers`, `find_implementations`, `get_type_hierarchy`, `search_symbols`, `get_diagnostics`, `find_unused_code`, `rename_symbol`, `get_code_fixes`, … | v0.0.3, updated 2025-10-27, ~601 downloads, targets net8.0, MIT | Best toolset and installs with the dotnet SDK already present. Early-stage; verify it loads a `net11.0` solution (tool runs on .NET 8 — the runtime must be installed or roll forward). |
| [carquiza/RoslynMcp](https://mcpservers.org/servers/carquiza/RoslynMcp) | standalone | code analysis/navigation | community | alternative candidate |
| VS-extension-based ([sailro RoslynMcp](https://lobehub.com/nl/mcp/sailro-roslynmcpextension), [MCP AI Server for VS](https://mcpservers.org/servers/ladislavsopko/mcp-ai-server-visual-studio)) | Visual Studio extension | 20 Roslyn tools | community | requires a running Visual Studio instance; wrong shape for headless agents |

Microsoft has **not** shipped an official standalone Roslyn MCP server;
the gap is actively discussed
([dotnet/roslyn discussion #82187](https://github.com/dotnet/roslyn/discussions/82187)).
What Microsoft did ship is the official
[`dotnet/skills`](https://github.com/dotnet/skills) agent plugin whose `dotnet`
plugin declares a C# LSP natively — see below.

## C# language server choice (if going the bridge route)

| Server | Verdict |
|---|---|
| **Roslyn LS** (`Microsoft.CodeAnalysis.LanguageServer`, tool name `roslyn-language-server`) | The official server behind the VS Code C# extension and modern Neovim setups. Distributed as a prerelease `dotnet tool` — installable from nuget.org or fresher from Microsoft's Azure DevOps feed ([seblj/roslyn.nvim](https://github.com/seblj/roslyn.nvim) documents both). Flags: `--stdio`, `--autoLoadProjects`. Solution-aware. |
| **csharp-ls** ([razzmatazz/csharp-language-server](https://github.com/razzmatazz/csharp-language-server)) | Roslyn-based lightweight server, `dotnet tool install --global csharp-ls`, .NET 8+, popular in Neovim/Zed. Some agent-integration friction reported ([claude-code #16360](https://github.com/anthropics/claude-code/issues/16360)). |
| **OmniSharp** | Legacy. Replaced as the VS Code C# extension default by Roslyn LS; still selectable but not recommended for new setups ([omnisharp-roslyn #2663](https://github.com/OmniSharp/omnisharp-roslyn/issues/2663)). |

Microsoft's own launch recipe, from the official plugin's
[`lsp.json`](https://raw.githubusercontent.com/dotnet/skills/main/plugins/dotnet/lsp.json)
— useful no matter which client wraps it:

```json
{
  "lspServers": {
    "csharp": {
      "command": "dnx",
      "args": ["roslyn-language-server", "--yes", "--prerelease", "--", "--stdio", "--autoLoadProjects"],
      "fileExtensions": { ".cs": "csharp", ".razor": "aspnetcorerazor", ".cshtml": "aspnetcorerazor" },
      "warmupTimeoutMs": 120000
    }
  }
}
```

Two details worth stealing: the 120 s warmup timeout (solution load is slow)
and `dnx` (an npx-style one-shot dotnet tool runner) to avoid pinning an
installed version.

## Recommended setup for this repo

Machine facts used below: dotnet 11.0.100-preview.6, Node 24, Go 1.21.1,
cargo 1.76; repo `D:\coding\ScheduleLib` with `All.sln`; ZCode present;
Codex desktop installed with `~/.codex/config.toml`.

### Option A (recommended): `dotnet-roslyn-mcp` in both clients

Install once (tool lands in `%USERPROFILE%\.dotnet\tools`):

```bash
dotnet tool install --global dotnet-roslyn-mcp
```

**ZCode** — create `D:\coding\ScheduleLib\.zcode\config.json` (workspace scope,
auto-connected; strict schema — unknown keys silently drop the server; no
`${...}` template expansion, use absolute paths):

```json
{
  "mcp": {
    "servers": {
      "csharp": {
        "type": "stdio",
        "command": "C:\\Users\\Anton\\.dotnet\\tools\\dotnet-roslyn-mcp.exe",
        "env": { "DOTNET_SOLUTION_PATH": "D:\\coding\\ScheduleLib\\All.sln" },
        "timeoutMs": 180000
      }
    }
  }
}
```

`timeoutMs` matters: ZCode's default is 30 000 ms and Roslyn needs well over
that on first solution load. Tools then appear as `mcp__csharp__find_references`
etc.; verify under **Settings → MCP** (source: `diagnosing-mcp/SKILL.md` §2–4).

**Codex** — append to `C:\Users\Anton\.codex\config.toml` (shape matches the
existing `node_repl`/`cua_repl` tables; keys per the
[official docs](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) —
`startup_timeout_sec` default 10 and `tool_timeout_sec` default 60 are both too
low for a cold Roslyn workspace):

```toml
[mcp_servers.csharp]
command = 'C:\Users\Anton\.dotnet\tools\dotnet-roslyn-mcp.exe'
args = []
startup_timeout_sec = 120
tool_timeout_sec = 120

[mcp_servers.csharp.env]
DOTNET_SOLUTION_PATH = 'D:\coding\ScheduleLib\All.sln'
```

Equivalent one-liner: `codex mcp add csharp --env DOTNET_SOLUTION_PATH=D:\coding\ScheduleLib\All.sln -- C:\Users\Anton\.dotnet\tools\dotnet-roslyn-mcp.exe`
(then add the two timeout keys by hand). Verify with `codex mcp list` or `/mcp`
in the TUI.

### Option B: generic bridge (isaacphi) + csharp-ls

Choose this if you want the same LSP the editor world uses, or distrust an
0.0.x Roslyn wrapper. Prerequisites: upgrade Go to ≥ 1.24
(`go install github.com/isaacphi/mcp-language-server@latest`) and
`dotnet tool install --global csharp-ls`. Then:

```json
{
  "mcp": {
    "servers": {
      "csharp": {
        "type": "stdio",
        "command": "mcp-language-server",
        "args": ["--workspace", "D:\\coding\\ScheduleLib", "--lsp", "csharp-ls"],
        "timeoutMs": 180000
      }
    }
  }
}
```

```toml
[mcp_servers.csharp]
command = 'mcp-language-server'
args = ['--workspace', 'D:/coding/ScheduleLib', '--lsp', 'csharp-ls']
startup_timeout_sec = 120
tool_timeout_sec = 180
```

Caveats: the bridge's tested-server list does not include C#, Windows is not
called out in its releases, and `csharp-ls` has a known agent-adjacent
capability bug (above). Expect to be the first to hit integration edges.

### Option C (Codex only): official Microsoft plugin

Codex v0.121.0+ supports plugins; Microsoft's
[`dotnet/skills`](https://github.com/dotnet/skills) ships a `dotnet` plugin
whose `lsp.json` declares the C# LSP (launched through `dnx` as shown above):

```
codex plugin marketplace add dotnet/skills
```

then install the `dotnet` plugin from the `/plugins` tab. This is the
best-maintained C#-in-agents path anywhere right now — but it is Codex-only:
in ZCode the plugin's skills load while `lspServers` is inert, and ZCode
marketplace plugin manifests follow the same recorded-but-not-executed rule
(zcode-configuration-guide §Plugins).

A reasonable end state is **A for ZCode + C for Codex** (or A in both for
symmetry).

## Verification steps

1. Run the server command by hand first (both clients just show "failed"
   otherwise): `dotnet-roslyn-mcp` with `DOTNET_SOLUTION_PATH` set should stay
   alive waiting on stdio; check it resolves `All.sln`.
2. ZCode: restart the session, open **Settings → MCP**, confirm `csharp` is
   connected; in a session, tools must be listed as `mcp__csharp__<tool>`. If
   missing: JSON syntax, unknown key (strict schema), or user-scope same-named
   server shadowing the workspace one (user overrides workspace).
3. Codex: `codex mcp list`, then `/mcp` in the TUI.
4. First real call should be something cheap like `health_check` /
   `get_project_structure`; expect the first call to be slow (solution load),
   later calls fast.

## Pitfalls and limitations

- **Warm-up latency.** Roslyn loading `All.sln` takes tens of seconds to ~2 min
  (Microsoft budgets 120 s in its own plugin config). Without raised timeouts
  the server will be killed mid-load: ZCode `timeoutMs`, Codex
  `startup_timeout_sec`/`tool_timeout_sec`.
- **One instance per workspace.** These servers are stateful and hold the
  solution in memory; the bridge/wrapper is launched per MCP client. Running
  ZCode and Codex simultaneously means two Roslyn workspaces — noticeable RAM.
- **Windows paths.** ZCode needs `command` as a string pointing at the actual
  `.exe`/`.cmd` (pitfall "spawn ENOENT"); LSP results come back as
  `file:///D:/...` URIs that must be mapped back to `D:\...` when reading files.
- **Token cost.** Every MCP server's tool schemas ride along in every session's
  context. Codex can trim with `enabled_tools` / `disabled_tools` and
  `tools.<tool>.output_token_limit`; ZCode has no documented per-tool filter,
  so keep the server list lean there.
- **Staleness.** The wrapper loads the solution once; edits made by the agent
  between LSP queries are picked up through file-watching, but generated files
  (`EmitCompilerGeneratedFiles` is on in `Directory.Build.props`) can confuse
  symbol indexes until a restore/build refreshes them.
- **Early-stage risk.** `dotnet-roslyn-mcp` is v0.0.3 with ~1 download/day;
  isaacphi's bridge is beta and C#-untested. Budget for falling back to plain
  grep/read (which remains fine for most tasks).
- **Security.** Workspace-scoped MCP servers auto-connect in ZCode on project
  open — only open workspaces you trust, and never commit secrets into
  `.zcode/config.json` (zcode-configuration-guide, "Choosing where to
  configure"). The same applies to a committed `.codex/config.toml` at repo
  scope (Codex docs: trusted-projects only).

## When it's not worth it

For quick "where is X used" questions in a repo this size, grep usually wins on
latency alone. The LSP pays off when correctness of *references/rename* matters,
when `TreatWarningsAsErrors` diagnostics should surface before a build cycle,
or when navigating the generated/ intertwined parts of the schedule model where
text search misleads.

## Sources

- isaacphi/mcp-language-server — [repo](https://github.com/isaacphi/mcp-language-server), [releases](https://github.com/isaacphi/mcp-language-server/releases), [go.mod](https://raw.githubusercontent.com/isaacphi/mcp-language-server/main/go.mod)
- t3ta/mcp-language-server — [repo](https://github.com/t3ta/mcp-language-server); STRd6 fork — [pkg.go.dev](https://pkg.go.dev/github.com/STRd6/mcp-language-server); morrow-addref/LSP-MCP — [glama.ai](https://glama.ai/mcp/servers/morrow-addref/LSP-MCP)
- dotnet-roslyn-mcp — [NuGet](https://www.nuget.org/packages/dotnet-roslyn-mcp), [source](https://github.com/brendankowitz/vs-ide-mcp)
- Microsoft dotnet/skills — [repo](https://github.com/dotnet/skills), [dotnet plugin](https://github.com/dotnet/skills/tree/main/plugins/dotnet), [lsp.json](https://raw.githubusercontent.com/dotnet/skills/main/plugins/dotnet/lsp.json)
- Roslyn LS distribution and flags — [seblj/roslyn.nvim](https://github.com/seblj/roslyn.nvim); OmniSharp status — [omnisharp-roslyn #2663](https://github.com/OmniSharp/omnisharp-roslyn/issues/2663); "why Roslyn MCP" — [dotnet/roslyn #82187](https://github.com/dotnet/roslyn/discussions/82187)
- csharp-ls — [razzmatazz/csharp-language-server](https://github.com/razzmatazz/csharp-language-server), [issue #212](https://github.com/razzmatazz/csharp-language-server/issues/212), [claude-code #16360](https://github.com/anthropics/claude-code/issues/16360)
- Codex MCP config — [official docs](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) (redirect target of developers.openai.com/codex/mcp)
- ZCode MCP config — local skills: `C:\Users\Anton\.zcode\cli\plugins\cache\zcode-plugins-official\zcode-guide\0.1.0\skills\zcode-configuration-guide\SKILL.md` and `...\skills\diagnosing-mcp\SKILL.md`
- Local state — `C:\Users\Anton\.codex\config.toml` (existing `[mcp_servers.*]` tables), `D:\coding\ScheduleLib\Directory.Build.props` (net11.0, TreatWarningsAsErrors), installed toolchains (dotnet 11 preview, Node 24, Go 1.21, cargo 1.76)
