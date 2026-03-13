# Architecture Assessment - Wintap

## Purpose
This document captures incremental architecture review findings, decisions, and follow-up work.
It is intentionally append-only and is meant to be rerunnable as the codebase evolves.

## Primary Drivers (This Round)
- Multi-platform expansion: Linux support is being built via platform-specific sensors.
- Two-team development model: platform sensor work is being done by a separate team; core must expose clean, reliable, testable integration points.
- Testability as a first-class requirement: we currently have little/no automated testing; this assessment should identify where to introduce tests at each appropriate layer (especially at sensor/core boundaries).

## Scope
- Current focus: process identity + lineage + persistence (Windows-first, cross-platform implications noted)
- Out of scope (for now): feature work, refactors, code changes, performance tuning unless directly tied to stability/correctness

## Integration Point Quality Bar
For each core <-> platform boundary we identify, we aim for:
- Explicit contracts: clear interfaces/data contracts; minimal surface area; no hidden global state requirements.
- Lifecycle clarity: ownership of start/stop/dispose; deterministic shutdown; no "fire and forget" background threads without supervision.
- Threading/backpressure: clear rules for concurrency, ordering guarantees (if any), and failure isolation.
- Observable behavior: structured logs/metrics and health signals for critical pipelines.
- Test harnessability: ability to run core logic against synthetic events without requiring OS-level facilities (ETW, Security log, root privileges).

## Review Methodology (Rerunnable)

### Principles
- Prefer evidence over intuition (cite files/functions/flows)
- Separate observations from recommendations
- Record risks/assumptions explicitly
- Keep changes holistic: avoid "fix one layer, break another" refactors

### Workflow
1. Pick a layer boundary and list its responsibilities (as implemented today).
2. Trace 1-2 concrete end-to-end flows through that boundary.
3. Identify mismatches: responsibility leaks, implicit contracts, lifecycle/threading assumptions, error handling gaps.
4. Propose a short, testable recommendation.
5. Record as an entry only after explicit approval.

### Entry Types
- Observation: factual, evidence-backed
- Finding: an observation with impact/risk
- Recommendation: proposed change direction (no code changes here)
- Decision: accepted direction (why), with tradeoffs

### Approval Gate
Each entry is proposed in chat and added to this document only after explicit approval:
- approve: add as-is
- revise: suggest edits
- reject: do not record

## Testability Methodology (Incremental)
As findings are recorded, we also record the smallest viable test that would prevent regressions:
- Contract tests at the sensor/core boundary (inputs/outputs, ordering, error handling).
- Deterministic unit tests for pure transforms (message normalization, process identity derivation).
- Component tests for persistence/resolution (DuckDB interactions) using temp DB paths and seeded data.
- End-to-end smoke tests (optional) that can run in CI where OS support exists, with clear skips otherwise.
Each test proposal includes: scope, dependencies, and how it will run in CI (Windows-only vs cross-platform).

## Architecture Map (Current)

### Flow: Conceptual Linux Process Sensor -> Core Pipeline
1. Linux process sensor constructs a `WintapMessage` with:
   - `MessageType = Process`
   - `ActivityType = Start/Stop/Refresh` (as applicable)
   - `PID` and `Process` fields (`ParentPID`, `Name`, `Path`, `CommandLine`, `User`, etc.)
2. Sensor computes `PidHash` (current pattern uses `ProcessHash.GenPidHash(pid, message.EventTime)`).
3. Sensor calls `EventChannel.Send(message)` as the integration point into core routing/enrichment/persistence/Esper.

Evidence:
- `wintap/platform/linux/sensor/ExampleSensor.cs`
- `wintap/core/sensor/OSQuerySensor.cs`
- `wintap/core/infrastructure/EventChannel.cs` (`Send`)
- `wintap/core/shared/ProcessHash.cs`

## Findings Log (Append-Only)

(entries below)

## Decisions Log (Append-Only)
(entries below)

## Open Questions

### OQ-001 - Who owns `PidHash` generation: sensors or core?
Context:
- Linux Example sensor generates `PidHash` before calling `EventChannel.Send`.
- `ProcessHash` includes `StateManager.AgentId` and `Environment.MachineName`, implying host-level identity inputs.
Why it matters:
- Two-team model: requiring every platform sensor to implement hashing consistently is error-prone.
- Testability: hashing rules belong in one place if they are part of the event contract.
Candidate directions:
- Core-owned: `EventChannel.Send` (or a dedicated core component) derives `PidHash` for Process events.
- Sensor-owned: sensors must provide `PidHash`, core validates and logs if missing/invalid.
Evidence:
- `wintap/core/shared/ProcessHash.cs`
- `wintap/platform/linux/sensor/ExampleSensor.cs`
- `wintap/core/infrastructure/EventChannel.cs`

### OQ-002 - What is the minimal required field set for a Process event at the sensor/core boundary?
Goal:
- Define a stable contract so platform teams can implement sensors independently and tests can assert correctness.

Proposed minimum for `ActivityType=Start`:
- `PID`
- `EventTime` (must be comparable/monotonic enough to disambiguate PID reuse)
- `Process.Name` and/or `Process.Path`
- `Process.ParentPID` (if known)
- `Process.CommandLine` (optional but strongly preferred)
- `Process.User` (optional; may require privilege)

Proposed minimum for `ActivityType=Stop`:
- `PID`
- `EventTime`
- exit status if available (platform-dependent)

Evidence:
- `shared/WintapAPI/WintapMessage.cs` (ProcessObject fields)
- `wintap/platform/linux/sensor/ExampleSensor.cs`
- `wintap/core/sensor/OSQuerySensor.cs`
