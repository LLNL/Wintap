# Test Project Structure and First Test Target

**Date:** 2026-06-30
**Status:** Accepted

## Context
Wintap had no test project. The standing methodology (`CLAUDE.md`) names
standing up xUnit as unit **P1.1** — the first instruction the Engineer writes
and the verification gate the Developer relies on thereafter.

Two open questions blocked P1.1 (recorded in the 2026-06-30 bootstrap log
entry):

1. **Placement** — where should the xUnit project(s) sit relative to the
   multi-target agents (`Wintap.csproj` Windows, `Lintap.csproj` Linux,
   `Mactap.csproj` macOS)?
2. **First behavior** — what is the first behavior worth covering?

A structural wrinkle complicates the answer. The Architect chose **per-target
test projects** (one per agent). But the chosen first behavior is a real test
against `WintapMessage`, which lives in **cross-platform `shared/WintapAPI`**
(`net8.0`, no OS-specific dependencies). The agent projects themselves are
OS-bound: `Wintap.csproj` drags Windows-only packages, so a test project that
references the Windows agent can only run `dotnet test` on Windows. The first
behavior therefore does not require any OS-specific agent assembly at all.

## Decision
1. **Structure (Open Question 1):** Adopt **per-target test projects** under
   `tests/`, mirroring the multi-target agent layout:
   `tests/Wintap.Tests/`, `tests/Lintap.Tests/`, `tests/Mactap.Tests/`. This is
   the convention going forward; agent-specific tests live in the sibling that
   matches their agent.

2. **First behavior (Open Question 2):** A **real test against
   `WintapMessage`** — construct it via its public constructor and assert on a
   real member (the `MessageType` property reflects the `MessageTypeEnum` value
   passed to the constructor). Not a pure smoke/`Assert.True(true)` test.

3. **Reconciliation (placement of the first test):** P1.1 stands up **only the
   Windows `tests/Wintap.Tests/` project now** and places the first
   `WintapMessage` test there. The `Lintap.Tests` and `Mactap.Tests` siblings
   are **deferred to a follow-up unit (P1.2)** and are not created in P1.1.
   - The `Wintap.Tests` project references **`shared/WintapAPI`** (where
     `WintapMessage` actually lives), **not** the Windows `Wintap.csproj`
     agent. This keeps the first test free of Windows-only package drag while
     still establishing the per-target naming convention the Architect chose.

## Options Considered
- **(A) Single shared test project** (Engineer's original recommendation) —
  one `tests/Wintap.Tests/` covering all targets. Rejected by the Architect in
  favor of per-target projects that mirror the agent structure.
- **(B) Stand up all three per-target projects now** — `Wintap.Tests`,
  `Lintap.Tests`, `Mactap.Tests` in P1.1. Rejected: Lintap/Mactap are
  in-development/experimental, would add empty scaffolding with no behavior to
  cover yet, and the first behavior targets cross-platform `WintapMessage`
  which needs none of them. Larger, lower-value first unit.
- **(C) Stand up the per-target structure but reference `shared/WintapAPI`
  from each, placing the first test in whichever project runs cross-platform.**
  Reasonable, but still creates three projects up front for one shared test.
- **(D, CHOSEN) Stand up only `tests/Wintap.Tests/` now, referencing
  `shared/WintapAPI`, with Lintap/Mactap siblings deferred to P1.2.** Smallest
  correct first unit that honors the per-target convention, exercises a real
  shared behavior, and runs on the primary (Windows) platform without OS-only
  package drag.

## Tradeoffs
- Establishes the per-target naming convention while only realizing one of the
  three siblings now — the convention is set by precedent, not by all three
  projects existing. P1.2 must follow to complete the mirror.
- `tests/Wintap.Tests/` references `shared/WintapAPI`, not the `Wintap.csproj`
  agent. The project name implies "the Windows agent's tests," but its first
  test exercises shared code. This is acceptable for P1.1 (the Windows agent
  itself has no covered behavior yet) but should be revisited when the first
  genuinely Windows-agent-specific test lands — at which point `Wintap.Tests`
  will additionally reference `Wintap.csproj` and `dotnet test` for it becomes
  Windows-only.
- The legacy non-SDK `.sln` format requires both a `Project(...)` block and
  explicit `ProjectConfigurationPlatforms` entries to add a project.

## Consequences
- **Easier:** the Developer has a concrete, runnable first unit; the
  verification gate (`dotnet test --filter "Category=P1.1"`) becomes real.
- **Harder / follow-up:** P1.2 (Lintap/Mactap test siblings) is now owed.
  When agent-specific behavior is tested, the cross-platform-vs-OS-bound
  reference question for each sibling must be settled.
- The trait convention `[Trait("Category","P1.#")]` is now the project standard
  for unit-filterable tests.

## Supersedes / Superseded By
None.
