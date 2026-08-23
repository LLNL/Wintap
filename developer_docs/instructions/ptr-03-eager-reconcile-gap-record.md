# ptr-03 Eager Reconcile And Gap Record

**Date:** 2026-08-22
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-23

## Purpose

Third and final unit of the `process-tree-recovery` feature. On the
keep-tree path added by ptr-02, eagerly closes open process rows that are
absent from the live snapshot (preventing PID-reuse misattribution), records
each close via the existing telemetry mechanism with the distinct metric
`startup_reconciled_closed` (Architect decision 2026-08-22: telemetry-only
provenance, no `process` schema change), and writes a queryable gap record
bounded by a last-write heartbeat watermark. The heartbeat rides the
existing maintenance sweep plus a clean-shutdown watermark in `Stop()`
(Architect decision 2026-08-22: no new timer).

Design context: `../Wintap-Analytics/wiki/work/process-tree-recovery/design.md`
(mechanisms 3 and 4).

## Working Branch

Implement on branch `process-tree-recovery` (off `develop`); ptr-01,
ptr-04, and ptr-02 must already be present on the branch (implementation
order ptr-01 → ptr-04 → ptr-02 → ptr-03). ptr-04 is a hard prerequisite: it
makes the retained System row's pid_hash match the new snapshot's seed hash,
so this unit's eager reconcile does not close the System root and split
lineage.

## Scope

Implement in the `wintap` repository:

- Modify `wintap/core/infrastructure/ProcessResolver.cs`
- Modify `wintap/core/infrastructure/IProcessResolver.cs`
- Modify `wintap/core/infrastructure/EventChannel.cs`
- Modify `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- Unit tests under `tests/Wintap.Tests/`, tagged
  `[Trait("Category", "ptr-03")]`

Hard constraints for this feature, repeated in every ptr unit:

- **No `process` table schema changes** (provenance is telemetry-only).
- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **No new NuGet dependencies; TraceEvent stays at its current version.**
- **No Linux/macOS code — this feature is Windows-only.**

## Dependencies

- Prior units and audits: ptr-01 (`EnsureEventStoreTables` refactor,
  probe/lookup plumbing — note: the `boot_session` table was removed from
  the design 2026-08-22; this unit's two tables are the first additions to
  `EnsureEventStoreTables`), ptr-04 (real-start System seed), ptr-02 (gate,
  `LastRefreshRebuiltFromSnapshot`, seams).
- Read-only grounding: `ProcessResolver.ReconcileStaleOpenRowsLocked`
  (existing sweep this unit's startup variant mirrors),
  `RecordTelemetryEvent` / `process_retention_telemetry`,
  `MaybeRunMaintenanceLocked`, `StateManager.SessionId`.

## Implementation Notes

### Table DDL (extend `EnsureEventStoreTables` from ptr-01)

```sql
CREATE TABLE IF NOT EXISTS collection_heartbeat (
    last_write TIMESTAMP,
    session_id VARCHAR
);

CREATE TABLE IF NOT EXISTS collection_gap (
    gap_start TIMESTAMP,
    gap_end TIMESTAMP,
    session_id VARCHAR,
    prior_session_id VARCHAR,
    reason VARCHAR
);
```

`collection_heartbeat` holds at most one row (delete-then-insert on every
update). `collection_gap` is append-only.

### ProcessResolver static helpers (connection-taking, test-seam pattern)

```csharp
internal static void UpdateHeartbeat(DuckDBConnection connection,
    DateTime lastWriteUtc, string sessionId)
// DELETE FROM collection_heartbeat; INSERT the single row.

internal static bool TryReadHeartbeat(DuckDBConnection connection,
    out DateTime lastWriteUtc, out string sessionId)
// false when no row exists.

internal static void WriteCollectionGap(DuckDBConnection connection,
    DateTime gapStartUtc, DateTime gapEndUtc, string sessionId,
    string priorSessionId, string reason)

internal static List<(string PidHash, int ProcessId, string ProcessName)>
    ReconcileStartupOpenRows(DuckDBConnection connection,
        IReadOnlyCollection<string> livePidHashes, DateTime exitTimeUtc)
```

`ReconcileStartupOpenRows` semantics (mirror the existing sweep's SQL style,
including `EscapeSql` for concatenated values):

- Select rows with `exit_time IS NULL AND process_id > 0` (the `> 0` filter
  matches the existing sweep and skips synthetic PID 0/-1 seeds).
- Close (`SET exit_time = TIMESTAMP '<exitTimeUtc>'` with the existing
  `yyyy-MM-dd HH:mm:ss` invariant format, guarded by `AND exit_time IS NULL`)
  every selected row whose `pid_hash` is **not** in `livePidHashes`.
- Rows whose pid_hash **is** in the set are untouched. Rows already exited
  are untouched.
- Return the closed rows' (pid_hash, pid, name) for telemetry.
- An empty `livePidHashes` set is valid (closes all open real rows); a null
  set is an `ArgumentNullException`.

### ProcessResolver instance members + interface + passthroughs

Add to `IProcessResolver` (and implement in `ProcessResolver`, each taking
`_dbLock`, with the same never-throw containment style as ptr-01 — catch,
log Warn, return default):

```csharp
void UpdateCollectionHeartbeat();
    // UpdateHeartbeat(connection, DateTime.UtcNow, StateManager.SessionId.ToString())
bool TryReadCollectionHeartbeat(out DateTime lastWriteUtc, out string sessionId);
int ReconcileStartupOpenRows(IReadOnlyCollection<string> livePidHashes, DateTime gapEndUtc);
    // calls the static helper with exitTimeUtc = gapEndUtc;
    // per closed row: RecordTelemetryEvent("startup_reconciled_closed", processName, pidHash);
    // logs one Info summary: "ProcessResolver startup reconcile closed {n} stale open rows (restart recovery)";
    // returns the closed-row count.
void WriteCollectionGap(DateTime gapStartUtc, DateTime gapEndUtc,
    string priorSessionId, string reason);
    // session_id = StateManager.SessionId.ToString()
```

Add the metric-name constant next to the existing ones:

```csharp
private const string StartupReconciledClosedMetricName = "startup_reconciled_closed";
```

Note: `RecordTelemetryEvent` queues to `_pendingTelemetry`, flushed by the
maintenance sweep — that is acceptable and matches the existing reconcile
metric; do not force an immediate flush.

Hook the heartbeat into the sweep: at the end of the successful body of
`MaybeRunMaintenanceLocked` (with the other locked maintenance calls), call
`UpdateHeartbeat(connection, nowUtc, StateManager.SessionId.ToString())`.

Add null-safe `EventChannel` passthroughs following the `ClearProcessDB`
pattern:

```csharp
public static void UpdateCollectionHeartbeat();
public static bool TryReadCollectionHeartbeat(out DateTime lastWriteUtc, out string sessionId);
public static int ReconcileStartupProcesses(IReadOnlyCollection<string> livePidHashes, DateTime gapEndUtc);
public static void WriteCollectionGap(DateTime gapStartUtc, DateTime gapEndUtc, string priorSessionId, string reason);
```

### WindowsProcessSensor changes

**New seams** (existing optional-parameter style):

```csharp
internal delegate bool TryReadHeartbeatDelegate(out DateTime lastWriteUtc, out string sessionId);

TryReadHeartbeatDelegate readHeartbeat = null,
    // default: EventChannel.TryReadCollectionHeartbeat
Func<IReadOnlyCollection<string>, DateTime, int> reconcileStartupOpenRows = null,
    // default: EventChannel.ReconcileStartupProcesses
Action<DateTime, DateTime, string, string> writeCollectionGap = null,
    // default: EventChannel.WriteCollectionGap
Action updateHeartbeat = null,
    // default: EventChannel.UpdateCollectionHeartbeat
```

**Live-set construction.** Refactor duplicate detection so the matched
record is available:

```csharp
private bool TryGetDuplicateProcessInstance(int pid, DateTime createTime,
    TimeSpan tolerance, out ProcessRecord existing)
```

(`IsDuplicateProcessInstance` delegates to it.) In the
`InitializeSnapshotRefresh` emission loop build
`var livePidHashes = new HashSet<string>(StringComparer.Ordinal)`:

- for a **suppressed duplicate**, add `existing.PidHash` — the
  resolver-registered hash, which can differ from the snapshot-computed hash
  by create-time skew; adding the snapshot hash instead would let the
  reconcile close a live process's row;
- for an **emitted** message (synthetic seeds included), add
  `message.PidHash`.

**Keep-path recovery block.** After the emission loop and the
`snapshotCount` exchange, when the tree was kept (ptr-02's
`LastRefreshRebuiltFromSnapshot == false`), run:

```csharp
DateTime restartUtc = utcNow().ToUniversalTime();
bool hasHeartbeat = readHeartbeat(out DateTime gapStartUtc, out string priorSessionId);
if (!hasHeartbeat)
{
    gapStartUtc = restartUtc;
    priorSessionId = string.Empty;
}

string reason = hasHeartbeat ? "restart_same_boot" : "restart_same_boot_no_heartbeat";
writeCollectionGap(gapStartUtc, restartUtc, priorSessionId, reason);
int closed = reconcileStartupOpenRows(livePidHashes, restartUtc);
log($"Windows process startup reconcile closed {closed} stale open rows; collection gap [{gapStartUtc:O} .. {restartUtc:O}] recorded", LogLevel.Info);
```

Rules:

- Runs **only** on the keep path; the rebuild path already starts from an
  empty table and must not write a gap record or reconcile.
- The bounded exit time is the gap end (`restartUtc`) — consistent with the
  existing sweep's close-at-now semantics; the gap record is what bounds the
  uncertainty window for downstream consumers.
- Wrap the block in try/catch: on any exception, log Warn
  (`"Windows process startup reconcile failed; stale open rows will be
  closed by the maintenance sweep: {ex.Message}"`) and continue — the
  existing `ReconcileStaleOpenRowsLocked` sweep is the safety net; do not
  fall back to ClearDB here (the tree was already kept and events may not
  have flowed yet).
- This block must complete before `Start()` subscribes ETW handlers; the
  existing `WindowsSubscriptionManager` ordering
  (`InitializeSnapshotRefresh()` then `pc.Start()`) already guarantees this —
  make no call-site change.

**Clean-shutdown watermark.** In `WindowsProcessSensor.Stop()`, before the
existing teardown, call `updateHeartbeat()` inside try/catch (Debug log on
failure). This tightens gap bounds for clean restarts.

## Tests

Every test tagged `[Trait("Category", "ptr-03")]`. No live ETW, no admin, no
host-process-list dependence. Resolver-level tests use a temp DuckDB file +
`EnsureEventStoreTables`; sensor-level tests use the seams.

Resolver static helpers:

1. `UpdateHeartbeat` twice ⇒ one row, latest values; `TryReadHeartbeat`
   round-trips; false on empty.
2. `WriteCollectionGap` persists all five fields.
3. `ReconcileStartupOpenRows`: fixture with (a) open row in set, (b) open
   row not in set, (c) exited row not in set, (d) open row with
   `process_id = 0` ⇒ only (b) is closed, with `exit_time == exitTimeUtc`;
   the returned list contains exactly (b).
4. **PID-reuse regression:** open row hash A for pid N; live set contains
   hash B (same pid N, different create time) but not A ⇒ A is closed, and a
   row inserted under hash B (via `UpsertProcessStart`) remains open.
5. Empty set closes all open real rows; null set throws.

Sensor keep-path behavior (seams):

6. Keep path ⇒ `writeCollectionGap` then `reconcileStartupOpenRows` invoked;
   captured live set contains the emitted messages' PidHash values,
   including the three synthetic seeds' hashes.
7. Suppressed duplicate ⇒ captured live set contains the resolver record's
   `PidHash` (fixture resolver returns a record whose PidHash differs from
   the snapshot-computed hash but is within create-time tolerance) and not
   the snapshot-computed hash.
8. Heartbeat present ⇒ gap args `[heartbeat, restartUtc]`, reason
   `restart_same_boot`, prior session id passed through. Heartbeat absent ⇒
   `gapStart == gapEnd == restartUtc`, reason
   `restart_same_boot_no_heartbeat`.
9. Rebuild path ⇒ neither gap nor reconcile seam invoked.
10. Reconcile seam throwing ⇒ `InitializeSnapshotRefresh` still returns
    true; Warn logged.
11. `Stop()` invokes the heartbeat seam; a throwing seam does not propagate.

Telemetry:

12. Instance-level `ReconcileStartupOpenRows` records one
    `startup_reconciled_closed` telemetry event per closed row — test at
    whatever level the existing telemetry tests use; if no precedent exists,
    assert via the static helper's returned list plus a direct
    `process_retention_telemetry` query after invoking the flush path is NOT
    required — returned-list coverage is sufficient, note it in the audit.

## Acceptance Criteria

1. `collection_heartbeat` and `collection_gap` DDL added to
   `EnsureEventStoreTables`; heartbeat is single-row, gap append-only.
2. Static helpers, instance members, `IProcessResolver` additions, and
   null-safe `EventChannel` passthroughs exist as specified with never-throw
   containment.
3. Heartbeat updates ride `MaybeRunMaintenanceLocked` and
   `WindowsProcessSensor.Stop()`; **no new timer** anywhere.
4. Keep path writes exactly one gap record per restart and closes exactly
   the stale open rows (live-set rules above, including the
   suppressed-duplicate resolver-hash rule); bounded exit time = gap end.
5. Closed rows are recorded with the distinct
   `startup_reconciled_closed` telemetry metric; the `process` table schema
   is unchanged.
6. Rebuild path performs no reconcile and writes no gap record.
7. Reconcile/gap failures degrade with a Warn log and never fail startup or
   trigger ClearDB.
8. Hard constraints upheld (no schema/PidHash/NuGet/Linux changes).
9. Tests tagged `ptr-03` cover the required cases; build and tests pass;
   whole-feature filter `dotnet test --filter "Category~ptr"` passes.
10. Manual check (document in audit): service restart mid-collection on an
    elevated Windows host ⇒ retained-tree log, reconcile summary log with a
    plausible count, one new `collection_gap` row, and
    `startup_reconciled_closed` rows visible in
    `process_retention_telemetry` after the next sweep flush.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-03"
dotnet test --filter "Category~ptr"
```

Known-issue fallback (document in audit if used):

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-03"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~ptr"
```

## Out of Scope

- Do not add a per-row exit-provenance column or any `process` table schema
  change (telemetry-only was decided 2026-08-22).
- Do not add a new heartbeat timer or change the maintenance sweep cadence
  or its config keys.
- Do not implement Parquet serialization of `collection_gap` /
  `collection_heartbeat` / telemetry tables (downstream consumption reads
  the DuckDB store; pipeline export is a future concern).
- Do not modify `ReconcileStaleOpenRowsLocked`, the retention sweep, or
  their config keys.
- Do not write any Linux or macOS code; `ProcessRundownSensor.cs` stays
  untouched (Linux deferred — logged follow-up in the wiki design page).
- Do not implement the file-backed ETW session (explicitly deferred pending
  gap-record evidence).
- Do not change `WintapMessage`, `ProcessObject`, `ProcessHash`, the PidHash
  formula, Esper EPL, or downstream analytics models.
- Do not add NuGet packages; no admin-only or reboot-dependent automated
  tests (manual checks cover those).
