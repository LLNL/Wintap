# Wintap — Project Rules & Development Methodology

**This file is the standing rule set for every session.** It is loaded
automatically by Claude Code. It plays the role that `AGENTS.md` plays in the
OpenCode workflow. If this file and the code disagree, the code wins.

---

## Project Summary

Wintap is a researcher-first host-telemetry collection and analytics platform
(LLNL, `LLNL-CODE-837816`). Full-fidelity process / network / file / registry
telemetry, an MEF plugin architecture, an Esper CEP engine for live EPL queries,
and open output formats (Parquet, CSV) queried with DuckDB.

- **Stack:** .NET 8.0 | MEF | Esper CEP | DuckDB | TraceEvent | Parquet
- **Platforms:** Windows (stable), Linux (in development), macOS (experimental)
- **Unified data model:** all telemetry flows through `WintapMessage`.

---

## Repo Layout

| Path | Purpose |
|---|---|
| `Wintap.sln` | Solution root |
| `wintap/Wintap.csproj` | Windows agent (primary) |
| `wintap/Lintap.csproj` | Linux agent |
| `wintap/Mactap.csproj` | macOS agent |
| `shared/WintapAPI/` | Shared API library |
| `shared/ai/wintap_mcp_server/` | DuckDB-backed MCP server |
| `shared/Wintap-Workbench/` | Workbench tooling |
| `platform/windows/` | Windows-specific platform code |
| `documentation/` | Human-authored docs (developer guide, ETL flow, Esper) |
| `devtools/` | Python dev/analysis tooling |
| `tests/` | Test projects (xUnit) — see Verification |

---

## Build & Verification Commands

| Action | Command |
|---|---|
| Build (Release) | `dotnet build -c Release` |
| Build (Debug) | `dotnet build` |
| Run all tests | `dotnet test` |
| Run a unit's tests | `dotnet test --filter "Category=P1.1"` |

**Test framework: xUnit.** Tests live under `tests/` (e.g.
`tests/Wintap.Tests/Wintap.Tests.csproj`). Every test is tagged with an
xUnit trait so it can be filtered by unit:

```csharp
[Trait("Category", "P1.1")]
```

> **Standing up tests is unit P1.1** — the first instruction the Engineer
> should write. Until that project exists, the Developer's verification gate
> falls back to a passing `dotnet build -c Release` plus a documented manual
> run. After P1.1 lands, every unit with testable behavior must ship passing
> tests filtered by its category.

**Unit naming convention (2026-08-17).** Instruction units are named
`<feature-abbrev>-<nn>` with a descriptive slug in filenames — e.g. feature
`improve-windows-process-collection` declares abbreviation `wpc`, giving
instruction `developer_docs/instructions/wpc-01-sid-helper.md`, trait
`[Trait("Category", "wpc-01")]`, and audit
`developer_docs/audits/wpc-01-sid-helper.md`. Each feature declares its
abbreviation once in its implementation plan, so numbers never collide across
features. Run one unit with `dotnet test --filter "Category=wpc-01"`; run a
whole feature with `dotnet test --filter "Category~wpc"`. The bootstrap unit
**P1.1** (and its owed follow-up P1.2) predate this scheme and are
grandfathered — do not rename them.

---

## The Methodology — Three Roles

This project uses a role-separated workflow (adapted from the OpenCode
Architect / Engineer / Developer pattern and the Karpathy "LLM wiki" idea).
The persistent ecosystem wiki is the compounding project memory.

> **Wiki location.** The Wintap ecosystem wiki lives in
> `../Wintap-Analytics/wiki/`. The old per-developer `dave-wiki/` has been
> retired; see
> `../Wintap-Analytics/wiki/decision/consolidate-developer-wiki-into-analytics-wiki.md`.
> Collision avoidance now comes from feature-scoped work folders under
> `../Wintap-Analytics/wiki/work/<feature-slug>/` plus the low-volume shared
> `../Wintap-Analytics/wiki/log.md`.

### Architect — *you, the human, in the main session*
The decision-maker and the only approval gate. You drive the main Claude Code
session, decide direction, approve instruction documents, and dispatch the
Engineer and Developer subagents. You inform the subagents; they execute.

### Engineer — `.claude/agents/engineer.md`
Design collaborator and **maintainer of `../Wintap-Analytics/wiki/` for Wintap
ecosystem knowledge plus `developer_docs/` process artifacts in this repo**.
Explores options, writes ADRs, maintains wiki pages and `log.md`, and writes
self-contained instruction documents for the Developer. **Never writes source
code or tests** (enforced by tool limits: no Bash, plus instruction discipline).

### Developer — `.claude/agents/developer.md`
Implements **exactly one approved instruction document at a time**. Writes code
and tests, runs the verification command, and files one audit artifact per unit.
**Never edits `../Wintap-Analytics/wiki/` or `developer_docs/instructions/`.**

### The loop
1. Architect dispatches the **Engineer** to explore a problem and draft an
   instruction document.
2. Architect reviews and **approves** the instruction (the gate).
3. Architect dispatches the **Developer** to implement that one approved unit.
4. Developer reports full test/build output and files an audit artifact.
5. Architect dispatches the Engineer to fold results back into the wiki.

> Subagents run in isolated context windows and cannot pause for your approval
> mid-run. Approval happens in the main session between dispatches — never hand
> an unapproved instruction straight to the Developer.

---

## Directory Ownership

| Path | Engineer | Developer |
|---|---|---|
| `../Wintap-Analytics/wiki/**` | Read / Write | Read only |
| `developer_docs/instructions/**` | Read / Write | Read only |
| `developer_docs/design/**`, `features/**` | Read / Write | Read only |
| `developer_docs/audits/**` | Read only | Read / Write |
| Source dirs (`wintap/`, `shared/`, `platform/`) | Read only | Read / Write |
| `tests/**` | Read only | Read / Write |

These boundaries are enforced by subagent tool limits plus instruction
discipline, not by hard filesystem ACLs. Respect them.

---

## Source of Truth Order

When information conflicts, resolve in this order:

1. The codebase as it currently exists
2. The approved instruction document
3. Relevant decisions in `../Wintap-Analytics/wiki/decision/`
4. Prior audit artifacts in `developer_docs/audits/`
5. General standards in this file

If resolving a conflict would require an architectural decision, **stop and
raise it to the Architect.** Do not resolve architectural ambiguity unilaterally.

---

## Coding Standards

- Target .NET 8.0; match the style of surrounding code.
- Do not add NuGet dependencies without flagging them to the Architect first.
- Never store secrets, API keys, or connection strings in code — use
  environment variables or gitignored local config.
- Do not introduce abstractions, error handling, or features beyond what the
  active instruction requires.
