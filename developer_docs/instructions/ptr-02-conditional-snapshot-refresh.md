# ptr-02 Conditional Snapshot Refresh

**Date:** 2026-08-22
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-23

> Reworked 2026-08-22: the Architect removed the `boot_session` metadata
> table from the design. The boot-session identity is now the seeded System
> row itself (ptr-04): its pid_hash is exactly recomputable from live PID 4
> StartTime, so a parallel identity copy is unnecessary. This unit's gate is
> now a System-row lookup, and there is no identity write step anywhere —
> the snapshot seeding IS the write.

## Purpose

Third-in-order unit of the `process-tree-recovery` feature. Gates the
unconditional `clearProcessDb()` call in
`WindowsProcessSensor.InitializeSnapshotRefresh()` on a boot-session
validity check: compute `genPidHash(4, livePid4StartFileTimeUtc)` from the
live probe and look for that pid_hash in the `process` table with
`exit_time IS NULL`. Present → same boot session → keep the tree and skip
ClearDB (the pid_hash upsert is idempotent). Absent, probe failure, or DB
unreadable → clear and rebuild (today's behavior) with explicit logging.

Design context: `../Wintap-Analytics/wiki/work/process-tree-recovery/design.md`
(mechanism 2, as revised 2026-08-22).

## Working Branch

**Implement on branch `process-tree-recovery`, branched off
`develop`.** Do not merge to `develop` in this unit.

**Sequencing: implementation order is ptr-01 → ptr-04 → ptr-02 → ptr-03.**
ptr-04 is a **hard prerequisite** of this unit: the gate looks up the
real-start System hash, which only exists once ptr-04 seeds the System row
from live PID 4 StartTime. Verify ptr-01 and ptr-04 changes are present on
the branch before starting.

## Scope

Implement in the `wintap` repository:

- Modify `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- Modify `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
  (one added log statement only)
- Unit tests under `tests/Wintap.Tests/`, tagged
  `[Trait("Category", "ptr-02")]`

Hard constraints for this feature, repeated in every ptr unit:

- **No `process` table schema changes; no new tables.**
- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **No new NuGet dependencies; TraceEvent stays at its current version.**
- **No Linux/macOS code — this feature is Windows-only.**

## Dependencies

- Prior units: `developer_docs/instructions/ptr-01-system-identity-probe.md`
  (probe exposure, `EventChannel.IsProcessRowOpen`) and
  `developer_docs/instructions/ptr-04-system-seed-real-start.md`
  (`probeSystemStartFileTimeUtc` seam; real-start System seeding), plus
  their audits.
- Read-only: `ProcessResolver.UpsertProcessStart` (the idempotency the keep
  path leans on: `ON CONFLICT(pid_hash) DO UPDATE` with
  `exit_time = COALESCE(process.exit_time, excluded.exit_time)`),
  `BootProcessTraceHelper.cs`, `WindowsSubscriptionManager.Start()` (~line
  38: `InitializeSnapshotRefresh()` runs before `pc.Start()` and boot-trace
  replay).

## Implementation Notes

### Config key

Add a kill switch read once in the `WindowsProcessSensor` constructor,
following the existing `ConfigManager` boolean pattern used by
`EventChannel.IsEnvEnabled` / `ProcessResolver.GetConfiguredBool`:

- Key: `WINTAP_BOOT_SESSION_RECOVERY_ENABLED`
- Default: `true` (missing/empty ⇒ enabled; `"0"`/`"false"` ⇒ disabled)

Expose it as an optional constructor parameter
`bool? bootSessionRecoveryEnabled = null` (null ⇒ read config) so tests
control it without config.

### New constructor seam

One new seam only (the probe seam already exists from ptr-04):

```csharp
Func<string, bool> isProcessRowOpen = null,
    // default: EventChannel.IsProcessRowOpen
```

### Single probe read

Hoist the PID 4 probe to one read per `InitializeSnapshotRefresh` call:
read `long? liveSystemStart = probeSystemStartFileTimeUtc()` (ptr-04's
seam, exception-safe) once at the top, use it for the gate below, and pass
it into `BuildSnapshotBatch` (change the private method to take the value
as a parameter instead of invoking the seam itself; ptr-04's fallback
logic — WMI + SENSOR HEALTH Warn when null — moves with it unchanged). One
read serves both the identity check and the seed anchor, so the kept or
reseeded System row and the gate can never disagree within a run.

### InitializeSnapshotRefresh gate

Replace the unconditional `clearProcessDb();` at the top of
`InitializeSnapshotRefresh()` with exactly this decision logic (inside the
existing try/catch; the method must still return `false` only on the
existing outer failure path):

```csharp
bool keepTree = false;
string degradeReason;
long? liveSystemStart = null;

if (!bootSessionRecoveryEnabled)
{
    degradeReason = "boot session recovery disabled";
}
else
{
    try
    {
        liveSystemStart = probeSystemStartFileTimeUtc();
        if (liveSystemStart == null)
        {
            degradeReason = "System start probe unavailable";
        }
        else
        {
            string systemPidHash = genPidHash(4, liveSystemStart.Value);
            if (isProcessRowOpen(systemPidHash))
            {
                keepTree = true;
                degradeReason = null;
            }
            else
            {
                degradeReason = "no open System row for this boot session";
            }
        }
    }
    catch (Exception ex)
    {
        keepTree = false;
        degradeReason = $"boot session check failed: {ex.Message}";
    }
}

if (keepTree)
{
    log("Windows process tree retained across restart (boot session match)", LogLevel.Info);
}
else
{
    clearProcessDb();
    log($"Windows process snapshot refresh rebuilt from snapshot, lineage degraded ({degradeReason})", LogLevel.Info);
}

LastRefreshRebuiltFromSnapshot = !keepTree;
```

Rules:

- There is **no identity write step and no identity clear step**. On the
  rebuild path, ptr-04's seeding of the System row from `liveSystemStart`
  IS the identity write; on the probe-failure rebuild path the WMI-fallback
  seed intentionally fails the next start's lookup (conservative rebuild).
- The gate compares by **exact pid_hash equality** via the lookup — no
  tolerance, no timestamp comparison in the sensor.
- `isProcessRowOpen` (ptr-01) is a pure read: no maintenance sweep, no side
  effects. Do not substitute `resolveProcessAtTime` (which triggers
  maintenance) for the gate check.
- When recovery is disabled, do not invoke the probe or the lookup at all —
  behavior is byte-for-byte today's path plus the disabled-reason log.
- The snapshot enumeration, synthetic seeds (ptr-04 semantics), ordering,
  dedup, and emission below the gate are **unchanged** in this unit.
- Add `internal bool LastRefreshRebuiltFromSnapshot { get; private set; }`
  (set as shown; also set to `true` in the existing catch block that
  returns `false`).
- Keep-path correctness rests on `UpsertProcessStart` idempotency plus the
  existing `IsDuplicateProcessInstance` suppression; synthetic seeds are
  always re-emitted and upsert onto the identical pid_hash (same anchor,
  post-ptr-04).

### Sensor-health log for an expected-but-absent boot trace

In `WindowsSubscriptionManager.Start()`, immediately after
`pc.InitializeSnapshotRefresh();`, add:

```csharp
if (enableBootProcessTrace && bootReplayPath == null && pc.LastRefreshRebuiltFromSnapshot)
{
    WintapLogger.Log.Append(
        "SENSOR HEALTH: boot process trace was expected but absent; early-boot process lineage may be incomplete",
        LogLevel.Warn);
}
```

Rationale (do not change): on a same-boot restart (keep path) the boot ETL
was already consumed at the previous start, so its absence is expected and
must not raise a health warning; on a rebuild after a fresh boot with the
setting enabled, absence is a sensor-health signal. Make no other change to
`WindowsSubscriptionManager.cs`.

### Log line contract

The two literal phrases below are grep contracts for operators and later
verification; use them verbatim inside the composed messages:

- `"retained across restart (boot session match)"`
- `"rebuilt from snapshot, lineage degraded"`

### Compatibility notes (include in the audit)

- **Upgrade boundary (self-healing):** a same-boot tree seeded before
  ptr-04 landed carries a WMI-hash System row, so the first post-upgrade
  start fails the lookup (`no open System row for this boot session`) and
  performs one extra clear-and-rebuild; the reseeded real-start System row
  then satisfies the lookup on every subsequent same-boot restart. One-time,
  expected, no action required.
- The same self-healing rebuild occurs after any event that closed or
  repaired the System row mid-run (e.g. a transient PID 4 probe failure
  during a maintenance sweep) — conservative by design.

## Tests

Add ptr-02 tests (e.g. extend `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
or a new `WindowsProcessBootSessionGateTests.cs`). No live ETW, no admin, no
dependence on the host process list; drive everything through the seams with
a fixture snapshot. Every test tagged `[Trait("Category", "ptr-02")]`.

Required tests:

1. **Open System row keeps tree:** probe returns X, `isProcessRowOpen`
   returns true ⇒ `clearProcessDb` seam not called; retained log line
   emitted; Refresh messages still emitted;
   `LastRefreshRebuiltFromSnapshot == false`; return value `true`.
2. **Lookup receives the exact hash:** capture the `isProcessRowOpen`
   argument and assert it equals `genPidHash(4, X)` (the same genPidHash
   seam the test injects).
3. **Absent/closed System row rebuilds:** lookup returns false ⇒
   `clearProcessDb` called exactly once and before the first emit; degraded
   log contains `"rebuilt from snapshot, lineage degraded"`;
   `LastRefreshRebuiltFromSnapshot == true`.
4. **Probe unavailable rebuilds:** probe returns null ⇒ lookup never
   invoked; `clearProcessDb` called; degraded log emitted; the snapshot
   seeds use the WMI fallback (ptr-04 behavior, asserted via the emitted
   seed messages).
5. **Probe/lookup throwing degrades safely:** seams throw ⇒ no exception
   escapes; rebuild path taken; method returns `true`.
6. **Single probe read:** the probe seam is invoked exactly once per
   `InitializeSnapshotRefresh` call, and on the keep path the emitted
   System seed's PidHash equals the gate's lookup hash (same X).
7. **Kill switch:** `bootSessionRecoveryEnabled: false` ⇒ probe and lookup
   seams never invoked; `clearProcessDb` called (today's behavior).
8. **Upsert idempotency backstop (resolver-level):** using a temp DuckDB
   and `EnsureEventStoreTables` + `UpsertProcessStart` (internal statics
   from ptr-01/existing code): upsert a Start row, set its `exit_time` via
   SQL, upsert the same pid_hash again ⇒ `exit_time` is preserved (COALESCE
   semantics). This pins the property the keep path depends on.
9. Existing wpc/ptr-01/ptr-04 tests still pass unchanged apart from
   optional constructor-argument additions.

## Acceptance Criteria

1. `InitializeSnapshotRefresh` implements the gate exactly as specified:
   single probe read shared with the seed anchor, exact-hash open-row
   lookup, degradation-first on every failure, no identity write/clear
   steps anywhere.
2. `WINTAP_BOOT_SESSION_RECOVERY_ENABLED` (default true) disables the whole
   mechanism, restoring today's behavior.
3. `LastRefreshRebuiltFromSnapshot` exposed and correct on all paths.
4. The sensor-health Warn line fires only for
   enabled-trace + null replay path + rebuilt tree.
5. Both grep-contract log phrases appear verbatim.
6. Snapshot batch construction (ptr-04 semantics), dedup, and emission
   order are unchanged apart from `BuildSnapshotBatch` taking the hoisted
   probe value as a parameter.
7. Hard constraints upheld (no schema/PidHash/NuGet/Linux changes;
   `ProcessResolver.cs` and `EventChannel.cs` unmodified in this unit).
8. The upgrade-boundary compatibility note is recorded in the audit.
9. Tests tagged `ptr-02` cover the required cases; build and tests pass.
10. Manual check (document result in the audit; requires an elevated
    Windows host running the service): restart the Wintap service and
    confirm the retained log line on the second start; then `shutdown /r`
    and confirm the degraded log line with reason
    `no open System row for this boot session` on the first post-reboot
    start.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-02"
```

Known-issue fallback (document in audit if used):

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-02"
```

## Out of Scope

- Do not create a `boot_session` table or any identity persistence (removed
  from the design 2026-08-22); do not add identity write/clear members or
  seams.
- Do not implement the eager reconcile of stale open rows, the heartbeat,
  or gap records (ptr-03) — but do not preclude them: the keep path must
  reach the end of emission normally.
- Do not modify `ProcessResolver.cs`, `IProcessResolver.cs`, or
  `EventChannel.cs` in this unit.
- Do not change `BootProcessTraceHelper.cs` or the boot-trace arm/replay
  lifecycle.
- Do not write any Linux or macOS code.
- Do not change ptr-04's seed semantics beyond the specified
  parameter-passing refactor of `BuildSnapshotBatch`.
- Do not change the `process` table schema, `WintapMessage`,
  `ProcessObject`, `ProcessHash`, or the PidHash formula.
- Do not add NuGet packages; do not add admin-only or reboot-dependent
  automated tests (the reboot check is manual).
