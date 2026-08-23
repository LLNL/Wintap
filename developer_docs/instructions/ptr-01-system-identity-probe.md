# ptr-01 System Identity Probe And Precondition

**Date:** 2026-08-22
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-23

> Reworked 2026-08-22: the Architect removed the `boot_session` metadata
> table from the design (single source of truth — with ptr-04, the System
> row's pid_hash is exactly recomputable from live PID 4 StartTime, so the
> System row IS the boot-session identity). This unit no longer persists
> anything. Supersedes the earlier draft `ptr-01-boot-session-identity.md`.

## Purpose

First unit of the `process-tree-recovery` feature (abbreviation `ptr`,
declared here). Provides the two read-only primitives later units use to
treat the seeded System row as the boot-session identity:

1. the live PID 4 start-time probe (exposure of the existing helper), and
2. a narrow "is this process row open" lookup ptr-02 calls with
   `genPidHash(4, livePid4Start)`.

It also refactors the event-store DDL into a testable helper (consumed by
ptr-03's new tables) and performs the feature's blocking precondition
verification (Step 0).

This unit does **not** change any startup behavior and creates **no** new
tables. Nothing calls the new members in production yet.

**Implementation order for the feature: ptr-01 → ptr-04 → ptr-02 → ptr-03.**

Design context: `../Wintap-Analytics/wiki/work/process-tree-recovery/design.md`
(mechanism 1, as revised 2026-08-22).

## Working Branch

All ptr units are implemented on branch `process-tree-recovery`,
branched off `develop`. Create the branch from `develop` if it does not
exist yet; otherwise check it out.

## Step 0 — Precondition Verification (do this FIRST; STOP on failure)

The feature's mismatch policy is "clear the DuckDB process table and rebuild
from snapshot". That is only safe if the DuckDB store is purely a resolver
cache — i.e. process events are made durable (Parquet raw_sensor output) by
a pipeline that does **not** read from `event_store/main.duckdb`.

Verify, read-only:

1. Read `wintap/core/etl/` (in particular the load/serialization path, e.g.
   `RawSensorWriter.cs`, `CacheManager.cs`, and however Esper serializer
   queries write parquet) and `wintap/core/infrastructure/EventChannel.cs`.
2. Confirm that process `WintapMessage` events reach the durable Parquet
   output from the live event stream (Esper/serializer path), not by reading
   rows back out of the ProcessResolver DuckDB store.
3. Confirm nothing else treats `event_store/main.duckdb` `process` rows as
   the only durable record (search for readers of `GetAllProcesses`,
   `GetProcessHistory()` (no-arg), and direct `main.duckdb` consumers such as
   `shared/ai/wintap_mcp_server/`; note what they are used for).

Runtime read dependencies do **not** fail the precondition: other sensors'
pid_hash lookups (`GetPidHash`/`ResolveProcessAtTime`) use the store as a
live resolution cache and their enriched events reach Parquet from the
stream — deleting prior-boot rows loses nothing already serialized. Only
durability counts.

**If the precondition FAILS** (the DuckDB `process` table is the only
durable record of prior process events, or Parquet output is fed from the
DuckDB store): **STOP.** Do not implement anything in this unit. File
`developer_docs/audits/ptr-01-system-identity-probe.md` documenting exactly
what you found (files, line references, the data flow), and report back. The
Architect must revisit the deletion policy before ptr units proceed.

**If the precondition HOLDS:** record the confirming evidence (files and
line references for the parquet write path) in the audit, then proceed.

## Scope

Implement in the `wintap` repository:

- Modify `wintap/core/infrastructure/ProcessResolver.cs`
- Modify `wintap/core/infrastructure/IProcessResolver.cs`
- Modify `wintap/core/infrastructure/EventChannel.cs`
- Add unit tests under `tests/Wintap.Tests/`, tagged
  `[Trait("Category", "ptr-01")]`

Hard constraints for this feature, repeated in every ptr unit:

- **No `process` table schema changes; no new tables in this unit.**
- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **No new NuGet dependencies; TraceEvent stays at its current version.**
- **No Linux/macOS code — this feature is Windows-only (Architect decision
  2026-08-22). Do not touch `ProcessRundownSensor.cs` or any
  `platform/linux/` file.**

## Dependencies

- Project rules: `CLAUDE.md`
- Test project: `tests/Wintap.Tests/Wintap.Tests.csproj` (exists; internals
  are visible to it — follow the existing pattern if a new
  `InternalsVisibleTo` is needed)
- Feature design: `../Wintap-Analytics/wiki/work/process-tree-recovery/design.md`
- Read-only grounding: `EventChannel.cs` (ClearProcessDB passthrough
  pattern, ~line 420); `ProcessResolver.TryGetProcessRowStateLocked`
  (existing private row-state query the lookup wraps);
  `UpsertProcessStart` (existing internal-static test-seam pattern to
  follow).

## Implementation Notes

### Probe exposure

In `ProcessResolver`:

- Change the existing
  `private static long? TryGetWindowsProcStartFileTimeUtc(int pid)` to
  `internal static` (no logic change).
- Add:

```csharp
internal static long? TryGetSystemStartFileTimeUtc()
```

returning `TryGetWindowsProcStartFileTimeUtc(4)` when
`OperatingSystem.IsWindows()`, else `null`. Never throws (the underlying
helper already swallows exceptions and returns null). This is the default
for the `probeSystemStartFileTimeUtc` seam that ptr-04 and ptr-02 share.

Do **not** add any identity persistence: no table, no kind constant, no
write/read/clear members. The System row seeded by the snapshot (ptr-04) is
the identity's only storage.

### Open-row lookup

Static helper (connection-taking, mirroring `UpsertProcessStart`):

```csharp
internal static bool IsProcessRowOpen(DuckDBConnection connection, string pidHash)
// SELECT exit_time IS NULL FROM process WHERE pid_hash = '<escaped>' LIMIT 1
// true only when a row exists AND exit_time IS NULL; false otherwise.
// Reuse the EscapeSql pattern; null/empty pidHash returns false.
```

(Implementation may reuse/refactor the query inside the existing private
`TryGetProcessRowStateLocked`; do not change that method's behavior for its
existing callers.)

Interface member on `IProcessResolver`:

```csharp
bool IsProcessRowOpen(string pidHash);
```

`ProcessResolver` implementation: take `_dbLock`, call the static helper.
**Do not call `MaybeRunMaintenanceLocked` in this member** — ptr-02 uses it
as the startup gate check and it must be a pure read with no side effects
(no sweep, no reconcile, no retention deletes triggered by the gate).
Error containment: catch any exception, log Warn
(`"ProcessResolver open-row lookup failed: {ex.Message}"`), return `false`
(degradation-first: a failed lookup means rebuild).

`EventChannel` null-safe passthrough following the `ClearProcessDB` pattern:

```csharp
public static bool IsProcessRowOpen(string pidHash)
// returns false if _processResolver is null
```

### DDL refactor (serves ptr-03)

Refactor the DDL execution in `InitializeDatabase()` into:

```csharp
internal static void EnsureEventStoreTables(DuckDBConnection connection)
```

with `InitializeDatabase()` calling it; behavior otherwise unchanged and
**no new tables added here**. Rationale: ptr-03 extends this helper with its
`collection_heartbeat` / `collection_gap` DDL and its tests need to run the
DDL against a temp database; ptr-01/ptr-02 tests also use it to build
fixture stores.

### Tests

Add `tests/Wintap.Tests/SystemIdentityProbeTests.cs` (or extend an existing
resolver test file). Every test tagged `[Trait("Category", "ptr-01")]`.
Tests must not require administrator privileges, must not touch
`Env.FileDataRoot`, and must not instantiate `ProcessResolver` (its
constructor binds the production DB path). Use a temp-file DuckDB connection
plus `EnsureEventStoreTables`.

Required coverage:

1. `EnsureEventStoreTables` creates the existing tables (`process`,
   `process_retention_telemetry`) and is idempotent when called twice.
2. `IsProcessRowOpen` returns true for an open row: insert via
   `UpsertProcessStart`, assert true for its pid_hash.
3. `IsProcessRowOpen` returns false after the row's `exit_time` is set via
   SQL.
4. `IsProcessRowOpen` returns false for a pid_hash with no row, for an
   empty string, and for null.
5. `IsProcessRowOpen` with a pid_hash containing a single quote does not
   throw and returns false (escaping check).
6. `TryGetWindowsProcStartFileTimeUtc(Environment.ProcessId)` returns
   non-null on Windows and round-trips through `DateTime.FromFileTimeUtc`
   to within 2 seconds of
   `Process.GetCurrentProcess().StartTime.ToUniversalTime()`.
7. `TryGetWindowsProcStartFileTimeUtc(-1)` returns null.

Do not unit-test PID 4 directly (access can vary by test-host privilege);
the ptr-02 manual check covers it.

## Acceptance Criteria

1. Step 0 verification performed first; findings with file/line evidence are
   in `developer_docs/audits/ptr-01-system-identity-probe.md`. If the
   precondition failed, nothing else was implemented and this unit stopped.
2. `TryGetSystemStartFileTimeUtc()` and the internal visibility change
   exist; no logic change to the underlying probe; no identity persistence
   of any kind exists (no new table, no write members).
3. `IsProcessRowOpen` exists in all three forms (static helper, interface
   member, EventChannel passthrough) with the no-maintenance, never-throw,
   false-on-failure semantics specified.
4. `EnsureEventStoreTables` exists, is called by `InitializeDatabase()`, and
   adds no new tables.
5. No production code path calls the new members yet (no behavior change);
   `ClearDB`, `RegisterProcess`, and the `process` table are untouched.
6. Hard constraints upheld (no schema/PidHash/NuGet/Linux changes).
7. Tests tagged `ptr-01` cover the required cases; build and unit tests
   pass.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-01"
```

If the repository-root commands fail with the known `MSB4249`
website-project issue, use the documented project-scoped fallbacks and note
the deviation in the audit:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-01"
```

## Out of Scope

- Do not create a `boot_session` table or any identity-persistence members
  (removed from the design 2026-08-22).
- Do not modify `WindowsProcessSensor.cs`, `WindowsSubscriptionManager.cs`,
  or any startup sequencing (ptr-04/ptr-02).
- Do not add heartbeat, gap-record, or reconcile members (ptr-03).
- Do not gate or remove any `ClearDB` / `ClearProcessDB` call.
- Do not write any Linux or macOS code; do not touch
  `platform/linux/sensor/ProcessRundownSensor.cs`.
- Do not change the `process` table schema, `WintapMessage`,
  `ProcessObject`, `ProcessHash`, or the PidHash formula.
- Do not use WMI `LastBootUpTime` / `StateManager.MachineBootTime` anywhere
  in this unit.
- Do not add NuGet packages or change TraceEvent.
- Do not add admin-only, live-ETW, or reboot-dependent tests.
