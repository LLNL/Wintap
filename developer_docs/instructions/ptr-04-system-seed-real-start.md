# ptr-04 System Seed Real Start

**Date:** 2026-08-22
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-23

## Purpose

Defect-fix unit of the `process-tree-recovery` feature, found during ptr-01
review. The synthetic System seed in
`WindowsProcessSensor.BuildSnapshotBatch()` uses WMI-derived
`machineBootTimeUtc()` (`StateManager.MachineBootTime`, WMI `LastBootUpTime`,
subject to NTP drift) for PID 4's create time and pid_hash. The true
kernel-recorded start is available live via the same mechanism as
`TryGetWindowsProcStartFileTimeUtc` (ptr-01). Consequences of WMI seeding
today:

1. `ProcessResolver.ReconcileStaleOpenRowsLocked` evaluates the open System
   row every sweep (PID 4 passes the `process_id > 0` filter). The live hash
   `GenPidHash(4, realStart)` never equals the stored
   `GenPidHash(4, wmiBootTime)`. Within the 2 s tolerance,
   `RepairLiveProcessPidHashLocked` rewrites System's pid_hash — dangling
   every `parent_pid_hash` reference to the old hash; outside tolerance, the
   System row is closed with an exit time (marked dead).
2. Under ptr-03, WMI drift across same-boot restarts can make the retained
   System row absent from the new snapshot's live set → eagerly closed, plus
   a duplicate System root → split lineage.
3. The ETW boot trace cannot supply System's true start (PID 4 predates the
   GlobalLogger session; it appears only as a DCStart rundown record stamped
   at trace start), so the live PID 4 read is the only exact source.

**Implementation order for the feature: ptr-01 → ptr-04 → ptr-02 → ptr-03.**

Design context:
`../Wintap-Analytics/wiki/work/process-tree-recovery/design.md`
(§System-seed defect).

## Working Branch

Implement on branch `process-tree-recovery` (off `develop`). ptr-01
must already be present on the branch; ptr-02/ptr-03 come after this unit.

## Scope

Implement in the `wintap` repository:

- Modify `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- Unit tests under `tests/Wintap.Tests/`, tagged
  `[Trait("Category", "ptr-04")]`

Hard constraints for this feature, repeated in every ptr unit:

- **No `process` table schema changes.**
- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes** (the formula is unchanged; only the
  timestamp fed to it for the synthetic seeds changes).
- **No new NuGet dependencies; TraceEvent stays at its current version.**
- **No Linux/macOS code — this feature is Windows-only.**

## Dependencies

- Prior unit: `developer_docs/instructions/ptr-01-system-identity-probe.md`
  (provides internal `ProcessResolver.TryGetSystemStartFileTimeUtc` /
  `TryGetWindowsProcStartFileTimeUtc`).
- Read-only grounding: `ProcessResolver.ReconcileStaleOpenRowsLocked` /
  `TryGetLiveProcessIdentity` / `RepairLiveProcessPidHashLocked` (the sweep
  whose stability this unit restores);
  `WindowsProcessSensor.BuildSnapshotBatch` / `CreateSystemSnapshotProcess` /
  `BuildParentPidHashes`; `ReplayBootTrace` /
  `HandleReplayedProcessTraceData` / `HandleReplayedStart`;
  `developer_docs/instructions/wpc-07-boot-etl-coverage.md` and its audit
  (replay design context).

## Implementation Notes

### New constructor seam

Following the existing optional-parameter style, add:

```csharp
Func<long?> probeSystemStartFileTimeUtc = null,
    // default: ProcessResolver.TryGetSystemStartFileTimeUtc (ptr-01)
```

Note this is the same underlying API the ProcessResolver reconcile sweep
uses to compute the live identity of PID 4
(`TryGetLiveProcessStartFileTimeUtc` → `TryGetWindowsProcStartFileTimeUtc`),
which is precisely why seeding from it makes the sweep stable.

### Seed anchor timestamp

In `BuildSnapshotBatch()`, replace the single WMI anchor with:

```csharp
long? systemStartFileTimeUtc = probeSystemStartFileTimeUtc();
DateTime anchorUtc;
if (systemStartFileTimeUtc != null)
{
    anchorUtc = DateTime.FromFileTimeUtc(systemStartFileTimeUtc.Value);
}
else
{
    anchorUtc = machineBootTimeUtc().ToUniversalTime();
    log("SENSOR HEALTH: PID 4 start time unavailable; System seed falling back to WMI boot time (pid_hash stability degraded)", LogLevel.Warn);
}
```

Rules:

- `anchorUtc` is used as `CreateTimeUtc` for **all three** synthetic seeds
  (PID 4 System, PID 0 Idle, PID -1 Unknown), exactly as the single WMI
  timestamp is today. Because `BuildParentPidHashes` computes each synthetic
  row's parent anchor as `genPidHash(4, ownCreateTime.ToFileTimeUtc())`, the
  Idle/Unknown parent anchors automatically follow the System row's new
  identity — no change to `BuildParentPidHashes` logic.
- Do not call `machineBootTimeUtc()` at all when the probe succeeds (keeps
  the WMI/COM call off the happy path); call it only on fallback.
- The probe seam must be wrapped so an exception behaves as `null`
  (fallback), never propagates.
- `CreateSystemSnapshotProcess` signature/fields are otherwise unchanged;
  seed names, paths, `ParentPid = 4`, `User = "SYSTEM"`, and
  `IsSynthetic = true` stay exactly as they are.

### Boot-trace replay handling (investigated; specify as follows)

The replay path (`ReplayBootTrace` → `HandleReplayedProcessTraceData` →
`HandleReplayedStart`) **synthesizes no System row itself** — it re-emits
whatever ProcessStart/ProcessDCStart records the boot ETL contains. PID 4
never has a true ProcessStart in the ETL (it predates the GlobalLogger
session); it can appear only as a **DCStart rundown record whose timestamp
is the trace-session start**, not System's true start. Whether the existing
2 s `BootReplayMatchTolerance` dedup catches such a record against the seed
is timing-dependent (trace start may be more than 2 s after the true PID 4
start), so it must not be relied on.

Therefore add an explicit guard at the top of `HandleReplayedStart`:

```csharp
if (pid == 4 || pid <= 0)
{
    return null; // synthetic seed anchors own these identities; a DCStart
                 // rundown timestamp is not a true start time
}
```

- The guard suppresses emission and must **not** increment
  `boot_replay_count`.
- No other replay behavior changes; genuine early-boot processes
  (smss/csrss/wininit era) still replay exactly as wpc-07 specified.

### Compatibility notes (include in the audit verbatim or equivalent)

- **System pid_hash values change relative to prior datasets.** Previously
  `GenPidHash(4, wmiBootTime)`; now `GenPidHash(4, truePid4Start)`. The
  Idle (PID 0) and Unknown (PID -1) seed hashes and all three seeds'
  `parent_pid_hash` anchors change the same way. Any downstream join of the
  System root or top-level `parent_pid_hash` values across datasets recorded
  before/after this unit will not line up. Within a single dataset the tree
  remains internally consistent; no migration is performed.
- Datasets also become **stable across same-boot restarts** for the System
  root (that is the point of the fix), so post-ptr-04 datasets are more
  self-consistent than pre-ptr-04 ones.
- The fallback path (PID 4 unreadable) reproduces today's WMI behavior,
  including its drift defects — acceptable as degradation; flagged by the
  SENSOR HEALTH log line.

## Tests

Add ptr-04 tests (extend `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
or a new file). No live ETW, no admin, no dependence on the host process
list — drive via the seams with fixture snapshots. Every test tagged
`[Trait("Category", "ptr-04")]`.

Required tests:

1. **Real-start seeding:** with `probeSystemStartFileTimeUtc` returning a
   known FileTime X, run `InitializeSnapshotRefresh` with the existing wpc
   seams (at ptr-04 time the ptr-02 gate does not exist yet — the method
   still clears unconditionally; assert on the emitted messages): all three
   synthetic seeds have
   `CreateTimeUtc == DateTime.FromFileTimeUtc(X)`; the PID 4 message's
   `PidHash == genPidHash(4, X)`; the PID 0 and PID -1 messages'
   `Process.ParentPidHash == genPidHash(4, X)`.
2. **Reconcile-stability property:** with the probe returning X, the seeded
   System row's pid_hash equals the hash the ProcessResolver reconcile sweep
   would compute for live PID 4 given the same probe value —
   `genPidHash(4, X)`. (Both production defaults call the same
   `TryGetWindowsProcStartFileTimeUtc(4)`; assert the hash equality so any
   future divergence of the two paths fails this test.)
3. **Fallback path:** probe returns null ⇒ seeds use
   `machineBootTimeUtc()` exactly as today (assert seed create time and
   hashes against the seam's boot time) and the SENSOR HEALTH Warn line is
   logged.
4. **Probe exception ⇒ fallback:** probe throws ⇒ identical to test 3; no
   exception escapes.
5. **Happy path skips WMI:** probe returns X ⇒ the `machineBootTimeUtc`
   seam is never invoked.
6. **Replay guard:** `HandleReplayedStart` with pid 4 returns null, emits
   nothing, and leaves `BootReplayCount` unchanged; same for pid 0 and a
   negative pid; a normal pid (resolver miss) still emits and increments
   `BootReplayCount`.
7. Existing wpc and ptr tests still pass (constructor-argument additions
   only).

## Acceptance Criteria

1. All three synthetic seeds anchor on live PID 4 start time when readable;
   `machineBootTimeUtc()` is consulted only on fallback.
2. The probe seam defaults to
   `ProcessResolver.TryGetSystemStartFileTimeUtc` (ptr-01) and never lets an
   exception escape.
3. Fallback to WMI boot time preserves today's behavior byte-for-byte and
   logs the specified SENSOR HEALTH Warn line.
4. `HandleReplayedStart` guards pid 4 and pid ≤ 0 without counting them as
   replays; no other replay semantics change.
5. `BuildParentPidHashes` logic is unchanged; Idle/Unknown parent anchors
   follow the System identity via the shared anchor timestamp.
6. No PidHash formula, schema, NuGet, TraceEvent, or Linux changes.
7. Compatibility note (System pid_hash change vs prior datasets) recorded in
   the audit.
8. Tests tagged `ptr-04` cover the required cases; build and tests pass,
   including `Category~wpc` regression and `Category~ptr`.
9. Manual check (document in audit; elevated Windows host): after service
   start, query the event store — exactly one open PID 4 row; across two
   maintenance sweeps (>10 min) the System row's pid_hash is unchanged and
   no `live_hash_repaired` or `reconciled_closed` telemetry rows reference
   PID 4.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-04"
dotnet test --filter "Category~ptr"
dotnet test --filter "Category~wpc"
```

Known-issue fallback (document in audit if used):

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-04"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~ptr"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"
```

## Out of Scope

- Do not change `ProcessResolver.cs` (the reconcile sweep, repair, and
  tolerance logic stay exactly as they are; this unit makes the seed
  compatible with them, not vice versa).
- Do not implement the conditional gate (ptr-02). Note for context
  (updated 2026-08-22): the `boot_session` table was removed from the
  design — the System row this unit seeds IS the boot-session identity, and
  ptr-02 will hoist this unit's `probeSystemStartFileTimeUtc` read so one
  probe value serves both the gate lookup and the seed anchor. Design the
  seam accordingly (no caching or hidden state inside it).
- Do not "fix" PID 4's self-parent seed semantics or the seed field values.
- Do not add migration/backfill for prior datasets' System pid_hash values.
- Do not change `BootProcessTraceHelper`, arm/disarm, or replay dedup
  tolerances.
- Do not write any Linux or macOS code.
- Do not change `WintapMessage`, `ProcessObject`, `ProcessHash`, the PidHash
  formula, Esper EPL, the `process` table schema, or downstream analytics
  models.
- Do not add NuGet packages; no admin-only or reboot-dependent automated
  tests (manual checks cover those).
