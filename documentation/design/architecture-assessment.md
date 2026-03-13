# Architecture Assessment - Wintap

## Purpose
This document captures incremental architecture review findings, decisions, and follow-up work.
It is intentionally append-only and is meant to be rerunnable as the codebase evolves.

## Primary Drivers (This Round)
- Multi-platform expansion: Linux support is being built via platform-specific sensors.
- Two-team development model: platform sensor work is being done by a separate team; core must expose clean, reliable, testable integration points.
- Testability as a first-class requirement: we currently have little/no automated testing; this assessment should identify where to introduce tests at each appropriate layer (especially at sensor/core boundaries).
- Scale/performance constraints: some sensors (e.g., file activity) can emit thousands of events/second; core attribution must avoid expensive work on the hot path.

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

### Flow: Core handling of incoming Process events (`EventChannel.Send`)
When `EventChannel.Send` receives `MessageType=Process`:
- It tags `AgentId`.
- It attempts to resolve and attach parent lineage (`Process.ParentPidHash`, `Process.ParentProcessName`) via `IProcessResolver.ResolveProcessAtTime(parentPid, eventTime)`.
- It does not resolve/normalize the process's own identity (it does not set `PidHash` or `ProcessName` for Process events).
- It forwards the event into Esper.
- It persists the process event by calling `_processResolver.RegisterProcess(streamedEvent)`.

Evidence:
- `wintap/core/infrastructure/EventChannel.cs` (`Send`)
- `wintap/core/infrastructure/ProcessResolver.cs` (`RegisterProcess`)

### Flow: Core handling of non-Process events (`EventChannel.Send`)
When `EventChannel.Send` receives a non-Process event:
- It tags `AgentId`.
- It resolves the owning process via `_processResolver.ResolveProcessAtTime(PID, EventTime)` and writes `PidHash` + `ProcessName` onto the event.
- If resolution fails, it falls back to `_processResolver.GetPidHash(PID, EventTime)` and sets `ProcessName="Unknown"`.
- It forwards the event into Esper.

Evidence:
- `wintap/core/infrastructure/EventChannel.cs` (`Send`)
- `wintap/core/infrastructure/ProcessResolver.cs` (`ResolveProcessAtTime`, `GetPidHash`)

### Field availability snapshot (current producers)
This table is descriptive of the current implementation and highlights contract gaps.

Producers:
- Windows Security log process events (4688/4689) via `ProcessSensor`
- Windows kernel process stop events via `KernelProcessSensor`
- Cross-platform OSQuery process events via `OSQuerySensor`
- Linux example process events via `ExampleProcessSensor`

Observed fields:
- Windows Security (4688/4689): sets `PidHash` using create time; sets `ProcessName/Path`, `Process.CommandLine`, `Process.User`; includes `ParentPID`.
- Windows kernel stop: emits Stop without setting `PidHash` or `ParentPID`; has image name + exit stats; has a create time payload but does not use it for identity.
- OSQuery: sets `PidHash` from parsed OSQuery timestamp for both Start and Stop; sets `ProcessName/Path`, `Process.CommandLine`, `Process.User`, `ParentPID`.
- Linux example: sets `PidHash` from `message.EventTime` (which is the event timestamp); sets `Process.Name/Path/CommandLine/User`, and `ParentPID`.

Evidence:
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs` (security log conversion)
- `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs` (process stop)
- `wintap/core/sensor/OSQuerySensor.cs` (OSQuery conversion)
- `wintap/platform/linux/sensor/ExampleSensor.cs` (example)

### Candidate Canonical Contract: Process lifecycle + attribution (cross-platform)
This contract is intended to support Linux platform sensors, high-volume event attribution, and deterministic testing.

Process events (`MessageType=Process`)
- `ActivityType=Start` required:
  - `PID`
  - `EventTime` = process start time (preferred) or closest available proxy
  - `Process.Path` or `Process.Name` (at least one)
  - `Process.ParentPID` if known
- `ActivityType=Stop` required:
  - `PID`
  - `EventTime` = stop time (if known)
  - exit code if available

Identity ownership
- Canonical identity key for a process instance is `(PID, start_time)`.
- Core owns derivation of `PidHash` from the canonical key and may ignore/override sensor-provided `PidHash` if inconsistent.

Attribution rules (hot path)
- Core maintains an in-memory process table of active instances keyed by `PID`.
- For non-Process events, core must attribute from memory first (populate `PidHash` and `ProcessName`) without a DB query.
- DB lookups are a cold fallback only for cache misses and should be observable (metrics/log sampling).

Stop event mapping
- If a Process Stop event arrives without a stable process instance identity, core maps it to the currently active instance for that `PID`.
- If no active instance exists, core emits the Stop event as unattributed using an explicit sentinel identity.

Unattributed sentinel identity (proposed)
- Goal: ensure ETL-required fields are always present while making unattributed events easy to filter/measure.
- `PidHash`: a deterministic, stable value that does not require DB access.
  - Proposed: `ProcessHash.GenPidHash(-1, 0)` (i.e., PID = -1, start_time = 0 as FileTimeUtc)
- `ProcessName`: a deterministic string.
  - Proposed: `unattributed`
- `ProcessPath`: optional; if set, use a constant placeholder (e.g., `unattributed`).

Testability requirements
- The contract must be testable with synthetic event sequences (no OS dependencies): ordering, PID reuse, missing starts, and mixed-source events.

Contract invariants (candidate)
- INV-001 (Non-Process attribution): after a Process Start for PID X has been observed, all subsequent non-Process events with PID X must have a non-empty `PidHash` matching the active instance, without requiring a DB query.
- INV-002 (Start/Stop identity): Process Start and Process Stop events for the same process instance must share the same `PidHash`.
- INV-003 (PID reuse): if PID X is reused after a Stop, the new Process Start must produce a different `PidHash` than the prior instance.
- INV-004 (Unattributed behavior): if attribution fails (no active instance and no cold fallback), events must be marked unattributed deterministically (sentinel identity) and must not crash downstream ETL.
- INV-005 (Ordering tolerance): out-of-order arrival (non-Process events preceding the Process Start) must not crash; attribution may be delayed, best-effort, or marked unattributed, but behavior must be deterministic and observable.

### Candidate minimal interface boundaries (no code changes yet)
Goal: introduce clean, testable seams between sensors, attribution, and persistence without prescribing implementation details.

Candidate boundary A: Attribution component
- Responsibility: assign `PidHash` + `ProcessName` to events using memory-first logic and explicit unattributed behavior.
- Inputs: `WintapMessage` (Process Start/Stop/Refresh and non-Process events)
- Outputs: enriched `WintapMessage` (identity fields set deterministically)
- Test surface: pure synthetic sequences; no OS, no DuckDB, no Esper required.

Candidate boundary B: Persistence component
- Responsibility: durable storage of Process instance records (for restart/cold lookup/forensics).
- Not on hot path by default.

Candidate boundary C: Pure identity function
- Responsibility: deterministic `PidHash` derivation from canonical identity key `(PID, start_time)`.
- Test surface: pure unit tests.

Candidate boundary D: Esper/ETL compatibility layer
- Responsibility: ensure events entering Esper satisfy ETL-required fields (`PidHash`, `ProcessName`) including explicit unattributed behavior.
- Test surface: unit/component tests that validate flattened outputs for representative events.

### Incremental staging plan (assessment output)
Goal: introduce testable seams and scale-safe attribution while keeping the system working throughout.

Stage 0: Document and lock the contract
- Adopt the canonical contract + invariants (INV-001..INV-005) as an explicit internal spec.
- Define the unattributed sentinel behavior (hash + process name) and when it is used.

Stage 1: Add contract tests first (no OS dependencies)
- Create a small test suite that runs synthetic `WintapMessage` sequences through attribution logic.
- Encode invariants as tests; these become the safety net for subsequent refactors.

Stage 2: Introduce an attribution component (memory-first)
- Implement a core-owned attribution component behind an interface (candidate `IProcessAttribution`).
- Initially, delegate to existing DB-backed lookup to preserve behavior, but instrument cache-hit/miss and DB lookup counts.

Stage 3: Make `EventChannel.Send` use attribution component
- Route both Process and non-Process events through attribution before Esper.
- Ensure ETL-required fields are always populated (including sentinel identity).

Stage 4: Separate persistence from attribution
- Restrict DuckDB-backed storage to Process event persistence/historical queries (`IProcessStore`).
- Remove DB usage from the non-Process hot path (except cold fallback).

Stage 5: Platform sensor integration hardening (Linux team enablement)
- Provide a reference implementation + contract checklist for Linux process sensors.
- Add contract tests that run against emitted events (schema/required fields) without OS dependencies.

Stage 6: Observability + performance validation
- Add counters/metrics for attribution cache hit rate, DB fallback rate, and unattributed rate.
- Add a non-CI microbenchmark for high-volume event streams.

## Findings Log (Append-Only)

### AA-004 - Core assumes Process events arrive with stable identity fields already populated
Type: Finding
Layer: Sensor/Core boundary (Process events)
Context:
- For `MessageType=Process`, `EventChannel.Send` enriches parent lineage but does not compute/validate the process's own `PidHash` or `ProcessName`.
- Persistence (`RegisterProcess`) uses `message.PidHash` as the primary key.

Evidence:
- `wintap/core/infrastructure/EventChannel.cs` (Process branch does not set `PidHash`/`ProcessName`)
- `wintap/core/infrastructure/ProcessResolver.cs` (`pid_hash` is PRIMARY KEY; `RegisterProcess` uses `message.PidHash`)

Impact:
- Linux platform sensors must implement identity derivation perfectly (PidHash + Name/Path) or downstream enrichment/persistence becomes inconsistent.
- In a two-team model, this increases integration risk and makes contract testing harder.

Recommendation (directional):
- Decide ownership of `PidHash` and core-required fields for Process events (ties to OQ-001/OQ-002).
- Consider moving PidHash derivation/validation into core (or adding a core validator that rejects/repairs malformed Process events).
Validation:
- Contract test: given a Process Start without `PidHash`, core behavior must be deterministic (reject with clear log OR derive a hash and persist).

### AA-005 - `IProcessResolver.GetPidHash` naming/behavior mismatch affects fallback paths
Type: Finding
Layer: Process identity contract
Context:
- `IProcessResolver.GetPidHash(pid, time)` sounds like a pure generator.
- Current implementation queries the `process` table and returns an existing `pid_hash` (or null if not found).

Evidence:
- `wintap/core/infrastructure/IProcessResolver.cs` (`GetPidHash` summary says "Generate")
- `wintap/core/infrastructure/ProcessResolver.cs` (`GetPidHash` uses `SELECT pid_hash FROM process ...`)

Impact:
- `EventChannel.Send` fallback behavior for unresolved processes can still produce null/unstable identity.
- This makes it harder to define reliable Linux sensor/core integration requirements (especially ordering of Process vs non-Process events).

Recommendation (directional):
- Make semantics explicit: split into `TryGetPidHash(...)` (lookup) vs `ComputePidHash(...)` (pure function), or redefine `GetPidHash` to always return a deterministic value.
Validation:
- Contract test: for a non-Process event with no prior Process Start in DB, core must still produce a non-null PidHash (or explicitly mark event unattributed).

### AA-006 - Process attribution performs per-event DB lookups on the hot path
Type: Finding
Layer: Core event routing + attribution
Context:
- High-volume sensors (e.g., file activity) can emit thousands of events/second.
- `EventChannel.Send` performs `_processResolver.ResolveProcessAtTime` for every non-Process event.

Evidence:
- `wintap/core/infrastructure/EventChannel.cs` (non-Process branch calls `ResolveProcessAtTime`)
Impact:
- Adds latency/jitter and creates a throughput ceiling tied to DuckDB performance and contention.
- Couples "ability to ingest telemetry" to "ability to query persistence" (operational fragility).
Recommendation (directional):
- Introduce an in-memory attribution path (cache/process table) updated by Process Start/Stop events; use DB as durable backing store or cold fallback only.
Validation:
- Add an attribution microbenchmark (non-CI-gating) and a deterministic contract test suite for attribution.

### AA-007 - Unattributed events can propagate null/empty `PidHash` and be dropped downstream
Type: Finding
Layer: Attribution -> ETL boundary
Context:
- Some ETL serializers require `PidHash` to be present; `Serializer.Save` throws if it is missing.

Evidence:
- `wintap/core/etl/extract/Serializer.cs` (`Save` throws `NULL_PIDHASH`)
- `wintap/core/infrastructure/EventChannel.cs` (fallback `GetPidHash(...)` can return null)
Impact:
- When process resolution fails (ordering, missing Process events, DB issues), non-Process events risk being dropped/errored in ETL.
Recommendation (directional):
- Make "unattributed" a first-class state (e.g., sentinel hash + explicit flag) OR guarantee deterministic hash computation without DB dependence.
Validation:
- Contract test: non-Process event with no prior process info must not crash ETL; it should be marked unattributed deterministically.

### AA-008 - Some Process Stop producers cannot provide stable `PidHash` without an attribution cache
Type: Finding
Layer: Sensor/Core boundary (Process lifecycle events)
Context:
- Some process stop events do not include process creation time, and some producers do not set `PidHash`.
Evidence:
- `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs` (Stop event: no `PidHash` assignment)
- `wintap/core/sensor/OSQuerySensor.cs` (Stop `PidHash` derived from stop timestamp)
Impact:
- Without a core-owned in-memory map (PID -> active instance), Stop events may not match the Start instance identity.
- This undermines process lifecycle modeling and prevents eliminating DB lookups from hot paths.
Recommendation (directional):
- Make core responsible for mapping Stop events to the correct instance `PidHash` using an in-memory process table; DB becomes cold fallback only.
Validation:
- Contract test: Start(pid=123,t0) -> Stop(pid=123,t1) results in identical `PidHash` for both events.

### AA-009 - Process identity inputs are inconsistent across producers (create-time vs event-time)
Type: Finding
Layer: Sensor/Core boundary (Process identity)
Context:
- Stable `PidHash` appears to require process creation time.
- Current producers compute `PidHash` from different timestamps depending on source.

Evidence:
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs` (PidHash derived from process create time)
- `wintap/core/sensor/OSQuerySensor.cs` (PidHash derived from OSQuery timestamp for both start/stop)
- `wintap/platform/linux/sensor/ExampleSensor.cs` (PidHash derived from event timestamp)

Impact:
- Cross-sensor correlation and lifecycle modeling become fragile, especially for Stop events.
- Makes the sensor/core contract harder to implement correctly for Linux without central guidance.

Recommendation (directional):
- Define canonical identity inputs (prefer: PID + process start time).
- If a source does not provide start time for Stop events, core must map Stop to the active instance using an in-memory process table.
Validation:
- Contract test: mixed-source Start and Stop events for same PID map to the same `PidHash` when representing the same process instance.

### AA-010 - Canonical sensor/core contract is not explicitly defined for process identity + attribution
Type: Finding
Layer: Sensor/Core boundary (cross-platform contract)
Context:
- Platform-specific sensors are being developed by a separate team (Linux support).
- Core currently relies on implicit behaviors (who sets `PidHash`, what `EventTime` means) that vary by producer.

Evidence:
- `wintap/platform/linux/sensor/ExampleSensor.cs` (sensor computes `PidHash`)
- `wintap/core/infrastructure/EventChannel.cs` (expects `PidHash` and does per-event lookups)
- `wintap/core/infrastructure/ProcessResolver.cs` (`pid_hash` is the primary key)

Impact:
- Integration risk: sensors can appear to work but produce inconsistent identity/lineage at scale.
- Testability suffers: no stable contract means no meaningful contract tests.

Recommendation (directional):
- Adopt and enforce a canonical contract for Process Start/Stop semantics and attribution rules (see candidate contract in Architecture Map).
Validation:
- Create a contract test suite using synthetic WintapMessage sequences to validate attribution and lifecycle invariants.

### AA-011 - Current core behavior violates or cannot guarantee candidate contract invariants
Type: Finding
Layer: Core event routing + ETL boundary
Context:
- Candidate invariants INV-001..INV-005 define expected, testable behavior.
- Current implementation has no explicit in-memory attribution layer and depends on DB lookups per event.

Evidence:
- `wintap/core/infrastructure/EventChannel.cs`
  - Non-Process attribution uses `_processResolver.ResolveProcessAtTime` (DB lookup) for every event.
  - Fallback uses `_processResolver.GetPidHash` which is also a DB lookup and may return null.
- `wintap/core/infrastructure/IProcessResolver.cs` (`GetPidHash` described as generation but implemented as lookup)
- `wintap/core/etl/extract/Serializer.cs` (throws `NULL_PIDHASH` when `PidHash` missing)

Invariant gaps (mapping)
- INV-001: not met (attribution is DB-based, not memory-first)
- INV-002: not guaranteed (Stop events can lack stable identity; core does not reconcile)
- INV-004: not met (unattributed events can trigger `NULL_PIDHASH` downstream)
- INV-005: behavior for out-of-order events is not explicitly defined/tested

Recommendation (directional):
- Introduce a core-owned attribution component that provides deterministic identity assignment and supports memory-first attribution.
- Make unattributed behavior explicit and non-fatal to ETL.
Validation:
- Implement contract tests directly against the attribution component, using synthetic sequences (PID reuse, out-of-order, missing starts).

### AA-012 - `IProcessResolver` combines hot-path attribution needs with durable persistence concerns
Type: Finding
Layer: Core infrastructure API design
Context:
- `EventChannel.Send` uses `IProcessResolver` for both high-frequency lookups (attribution) and persistence (`RegisterProcess`).
- Current `GetPidHash` behavior is a DB lookup, not a deterministic generator.

Evidence:
- `wintap/core/infrastructure/EventChannel.cs` (per-event calls to `ResolveProcessAtTime` / `GetPidHash`)
- `wintap/core/infrastructure/IProcessResolver.cs` (mixed responsibilities)
- `wintap/core/infrastructure/ProcessResolver.cs` (DuckDB-backed implementation)

Impact:
- Makes it difficult to build a scale-safe attribution path (memory-first) without changing or bypassing this interface.
- Makes unit testing harder (DuckDB becomes entangled with core logic).

Recommendation (directional):
- Split responsibilities into separate contracts, e.g.:
  - `IProcessAttribution` (memory-first, deterministic, test-focused)
  - `IProcessStore` (durable persistence + historical queries)
- Rename methods to reflect semantics (lookup vs compute), e.g. `TryGetPidHash` vs `ComputePidHash`.
Validation:
- Contract tests run against `IProcessAttribution` without DuckDB; persistence tests run separately against `IProcessStore`.

### AA-013 - ETL serializers assume `PidHash` and `ProcessName` are always present
Type: Finding
Layer: Esper -> ETL boundary
Context:
- ETL serializers extract `PidHash` and `ProcessName` from Esper events.
- Base `Serializer.Save` throws if `PidHash` is missing.

Evidence:
- `wintap/core/etl/extract/DefaultSerializer.cs` (reads `sensorEvent["PidHash"]` and `sensorEvent["ProcessName"]`)
- `wintap/core/etl/extract/FileSerializer.cs` (reads `sensorEvent["PidHash"]` and `sensorEvent["ProcessName"]`)
- `wintap/core/etl/extract/Serializer.cs` (`Save` throws `NULL_PIDHASH`)

Impact:
- Attribution is a hard prerequisite for ETL stability; failures propagate as exceptions/drops.
- Any redesign must define deterministic unattributed behavior compatible with ETL expectations.

Recommendation (directional):
- Align ETL expectations with the canonical contract:
  - either guarantee a non-empty `PidHash` (including an explicit sentinel identity), or
  - update ETL to accept an unattributed marker without throwing.
Validation:
- Contract test: unattributed non-Process events do not crash ETL and are tagged consistently.

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

### OQ-003 - How do we attribute high-volume events without DB lookups on the hot path?
Context:
- `EventChannel.Send` currently calls `ResolveProcessAtTime(pid, eventTime)` for non-Process events.
- Some sensors (e.g., file activity) can emit thousands of events/second.
Why it matters:
- Per-event DB lookups create a scaling ceiling and introduce latency/jitter.
- Linux support increases the need for deterministic, testable attribution independent of OS facilities.
Candidate directions:
- In-memory process table (keyed by PID + create time) updated by Process Start/Stop; DB becomes durable backing store only.
- Multi-tier cache: memory first, DB fallback, with bounded TTL/size and explicit eviction.
- Separate "attribution pipeline" from "durable persistence" so sensors can push events without blocking on storage.
Evidence:
- `wintap/core/infrastructure/EventChannel.cs` (`Send`)
- `wintap/core/infrastructure/ProcessResolver.cs` (`ResolveProcessAtTime`)

### OQ-004 - What are the semantics of `EventTime` for Process Start vs Stop across platforms?
Context:
- `PidHash` stability appears to depend on process creation time, but some sources only provide an event timestamp (which may be stop time).
Why it matters:
- If Stop events cannot be mapped back to the same process instance identity as Start, lineage and attribution become inconsistent.
Evidence:
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs` (PidHash uses process create time)
- `wintap/core/sensor/OSQuerySensor.cs` (PidHash derived from parsed OSQuery timestamp for both start/stop)

### OQ-005 - What should the core in-memory attribution contract look like?
Goal:
- Avoid DB lookups on hot paths while keeping correct PID reuse handling and cross-platform support.

Candidate contract:
- Core maintains an in-memory process table updated by Process Start/Stop/Refresh.
- Non-Process events are attributed from memory first (PidHash + ProcessName), with DB as fallback only for cache misses.
- Process Stop events are assigned `PidHash` from the in-memory table when sensors cannot provide it.

Validation:
- Deterministic sequence tests covering PID reuse:
  - Start(pid=10,t0), File(pid=10,t0+Delta) -> attributed to instance A
  - Stop(pid=10,t1) -> still instance A
  - Start(pid=10,t2) -> instance B (new hash), File(pid=10,t2+Delta) -> instance B
Evidence:
- `wintap/core/infrastructure/EventChannel.cs` (attribution currently happens here)
- `wintap/core/infrastructure/ProcessResolver.cs` (DB model)
