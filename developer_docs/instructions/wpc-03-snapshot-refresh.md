# wpc-03 Snapshot Refresh

**Date:** 2026-08-17
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-17

## Purpose

Add live Windows process snapshot refresh to the new `WindowsProcessSensor` for
the `improve-windows-process-collection` feature. This unit replaces the old
`ProcessSensor.Initialize()` Security-log reconstruction behavior with a live
process snapshot that emits `Refresh` process events from exact live create
times, parent PID, executable path, command line read from the PEB, and user
identity from the process token.

This unit extends the wpc-02 `WindowsProcessSensor` core. It does not wire the
new sensor into `WindowsSubscriptionManager`, delete old sensors, remove
Security-log dependencies from the repository, add boot ETL replay, or add live
Start enrichment. Those remain later units.

## Scope

Implement this unit in the `wintap` repository.

Modify:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- Unit tests under `tests/Wintap.Tests/`, tagged:
  `[Trait("Category", "wpc-03")]`

Add small helper files under `wintap/platform/windows/sensor/etw/helpers/` only
if keeping all snapshot interop inside `WindowsProcessSensor.cs` would make the
sensor hard to read.

The implementation must:

- enumerate currently running Windows processes from the live system;
- collect exact process create times via `GetProcessTimes`;
- collect parent PID via `NtQueryInformationProcess`;
- collect executable path via `QueryFullProcessImageName`;
- collect command line by reading the target process PEB;
- collect user identity via `OpenProcessToken`;
- emit `Refresh` events oldest-first;
- call `EventChannel.ClearProcessDB()` once before emitting snapshot Refresh
  events;
- seed the same three synthetic system processes as the existing
  `ProcessSensor.Initialize()` path: PID 4, PID 0, and PID -1;
- suppress duplicate Refresh events for instances already registered by live
  Start using resolver-backed PID + create-time tolerance;
- keep the existing `WintapMessage` / `ProcessObject` event shape unchanged.

Hard constraints for this feature, repeated verbatim:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

## Dependencies

- Existing project rules: `CLAUDE.md`
- Existing test project: `tests/Wintap.Tests/Wintap.Tests.csproj`
- Prior unit: `developer_docs/instructions/wpc-02-sensor-core.md`
- Prior audit: `developer_docs/audits/wpc-02-sensor-core.md`

Relevant sources to read and preserve:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
  - wpc-02 sensor core to extend.
  - It already has minimal seams for resolver lookup, emit, UTC clock, and
    `PidHash` generation.
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs`
  - `Initialize()` is the behavior being replaced in the new sensor.
  - Preserve its `EventChannel.ClearProcessDB()`-before-Refresh ordering.
  - Preserve its three synthetic system process seeds.
  - Do not carry forward its Security-log reconstruction, log-wrap failure, or
    event-log create-time approximation.
- `wintap/platform/linux/sensor/ProcessRundownSensor.cs`
  - Linux precedent for snapshot-refresh shape: enumerate, sort by start time,
    create `Refresh` process messages, and send through `EventChannel`.
- `wintap/core/infrastructure/EventChannel.cs`
  - Read-only for this unit.
  - `EventChannel.Send` resolves process parent context and registers process
    messages through the configured `IProcessResolver`.
  - `EventChannel.ClearProcessDB()` calls resolver `ClearDB()`.
- `wintap/core/infrastructure/ProcessResolver.cs`
  - Read-only for this unit.
  - Resolver is the sole process identity store; do not add a sensor-owned PID
    instance map.
  - Its live-start-time tolerance/repair behavior is the safety net for residual
    skew between live ETW Start timestamps and snapshot create times.
- `wintap/core/shared/ProcessHash.cs`
  - Existing `GenPidHash(pid, fileTimeUtc)` formula; do not change it.

## Design Constraints

### Timing and PidHash semantics

Carry this cross-source timing nuance into the implementation:

- Live `Start` `PidHash` uses the ETW process-start timestamp as Wintap's
  canonical source. This was settled in wpc-02 and must not be changed here.
- Snapshot `Refresh` uses live create times from `GetProcessTimes`.
- These two clocks can have small residual skew for the same process instance.
  Start-vs-Refresh dedup must therefore be resolver-backed by PID plus
  create-time tolerance. The resolver's tolerance/repair behavior handles any
  remaining skew; do not change the `PidHash` formula and do not force snapshot
  create times to the ETW timestamp.

### Process identity storage

Do not add a sensor-owned PID instance map for live process identity. The only
allowed in-memory collection in this unit is a local list/dictionary for the
current snapshot batch so parent `PidHash` values can be assigned before
emission.

For duplicate suppression, query the resolver through an internal seam that
defaults to `EventChannel.GetProcessHistory`. A Refresh is a duplicate when the
resolver returns a record for the same PID whose `CreateTime` is within the
configured tolerance of the snapshot create time.

Use a two-second tolerance to match the existing `ProcessResolver` live-start
repair window. Keep the tolerance private/internal to `WindowsProcessSensor` or
the snapshot helper; do not modify `ProcessResolver` to expose its private
constant.

Because `EventChannel.GetProcessHistory(pid, eventTime)` resolves records active
at the supplied time, production duplicate lookup should query at
`snapshotCreateTimeUtc + tolerance`, then verify the returned record has the
same PID and `Math.Abs(record.CreateTime - snapshotCreateTimeUtc) <= tolerance`.
This handles either ordering of small ETW-vs-live create-time skew.

### Startup ordering contract

Add a `WindowsProcessSensor` initialization/snapshot method that performs the
replacement for `ProcessSensor.Initialize()` but do not wire it into runtime
startup yet. wpc-06 will replace the old sensor bootstrap.

The new method must preserve this ordering:

1. Log that Windows process snapshot refresh is starting.
2. Call `EventChannel.ClearProcessDB()` once.
3. Build the snapshot batch, including the three synthetic system processes.
4. Compute each Refresh message's `PidHash` and best available `ParentPidHash`.
5. Emit non-duplicate Refresh events oldest-first through the sensor emit seam,
   whose production default remains `EventChannel.Send`.

Never call `ClearProcessDB()` after Refresh emission begins. The clear must be
before the Refresh stream so `ProcessResolver` rebuilds from the snapshot, as it
does today.

## Implementation Notes

### Snapshot data model

Add an internal primitive snapshot DTO so tests do not need live Windows process
handles. Keep it internal to `WindowsProcessSensor.cs` or a narrow helper file.

Suggested shape:

```csharp
internal sealed class SnapshotProcessInfo
{
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public DateTime CreateTimeUtc { get; init; }
    public string Name { get; init; }
    public string Path { get; init; }
    public string CommandLine { get; init; }
    public string User { get; init; }
}
```

Add optional constructor seams to `WindowsProcessSensor` only as needed:

- `Func<IReadOnlyList<SnapshotProcessInfo>> enumerateSnapshot`
- `Action clearProcessDb`
- `Func<int, DateTime, ProcessRecord> resolveProcessAtTime` can be reused from
  wpc-02 for duplicate checks.

Production defaults:

- enumerate live processes using the Windows snapshot enumerator from this unit;
- clear calls `EventChannel.ClearProcessDB()`;
- resolver calls `EventChannel.GetProcessHistory(pid, eventTimeUtc)`;
- emit calls `EventChannel.Send(message)`.

Do not introduce a project-wide abstraction layer.

### Live snapshot enumerator

Enumerate processes with `Process.GetProcesses()` or `NtQuerySystemInformation`.
For each live process, collect fields independently and best-effort. A failure to
read one enrichment field must not drop the process if PID and create time are
available.

Required field sources:

- PID: live process ID.
- Create time: `GetProcessTimes` on a process handle, converted to UTC.
- Parent PID: `NtQueryInformationProcess` using `ProcessBasicInformation` and
  `InheritedFromUniqueProcessId`.
- Path: `QueryFullProcessImageName`; fall back to the best available process
  name if path access fails.
- Name: file name from path when available; otherwise process name.
- Command line: read from the target process PEB using
  `NtQueryInformationProcess` for `PROCESS_BASIC_INFORMATION`, then
  `ReadProcessMemory` for `PEB.ProcessParameters` and
  `RTL_USER_PROCESS_PARAMETERS.CommandLine`. This is best-effort and may be
  empty for protected/exited processes.
- User: `OpenProcessToken` with token query access. Prefer an existing .NET
  token-to-identity path such as `WindowsIdentity` from the token handle when it
  fits; fall back to a SID string or empty string if lookup fails.

Use least-privilege process access flags. A typical handle attempt should start
with `PROCESS_QUERY_LIMITED_INFORMATION` and add VM read rights only for the PEB
command-line read. Always close native handles.

Skip processes when exact create time cannot be read. The unit's correctness is
based on exact create times; do not fall back to event-log times, `DateTime.Now`,
or imprecise reconstruction.

Do not use the Windows Security event log, 4688/4689 events, audit policy, WMI,
or any new package for the snapshot.

### Synthetic system process seeds

Seed these same three synthetic processes before normal live processes are
emitted. Use `StateManager.MachineBootTime.ToUniversalTime()` as their create
time, matching the current `ProcessSensor.Initialize()` behavior.

Preserve current seed field values:

- PID 4:
  `Name = "System"`, `Path = Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe")`
- PID 0:
  `Name = "System Idle Process"`, `Path = "idle"`
- PID -1:
  `Name = "Unknown"`, `Path = "unknown-sys"`

For all three:

- `ParentPID = 4`
- `CommandLine = ""`
- `User = "SYSTEM"`
- `CreateTimeUtc = boot time UTC`
- `PidHash = GenPidHash(pid, bootTimeUtc.ToFileTimeUtc())`
- `ParentPidHash = GenPidHash(4, bootTimeUtc.ToFileTimeUtc())`

Do not "fix" the existing self-parent behavior for PID 4 in this unit. Preserve
the old seed semantics exactly unless compilation requires a trivial adjustment.

### Refresh message creation

Create Refresh messages from snapshot records with the existing event schema:

- `new WintapMessage(createTimeUtc, pid, WintapMessage.MessageTypeEnum.Process)`
- `MessageType = WintapMessage.MessageTypeEnum.Process`
- `ActivityType = WintapMessage.ActivityTypeEnum.Refresh`
- `PidHash = GenPidHash(pid, createTimeUtc.ToFileTimeUtc())`
- `ProcessName = name`
- `ProcessPath = path`
- `Process = new WintapMessage.ProcessObject { ... }`

Populate `ProcessObject` fields:

- `PID`
- `ParentPID`
- `ParentPidHash`
- `Name`
- `Path`
- `CommandLine`
- `Arguments` equal to command line, matching current process sensor style
- `User`

Set `ParentPidHash` before emission when the parent instance is present in the
snapshot batch. Parent selection rule:

- find records with `Pid == child.ParentPid`;
- require `parent.CreateTimeUtc < child.CreateTimeUtc`;
- choose the matching parent with the latest create time preceding the child;
- use that parent's `PidHash`.

If no parent instance is found, leave `ParentPidHash` empty and allow
`EventChannel.Send` / `ProcessResolver` fallback behavior to run. Do not invent a
new fallback hash rule in this unit.

### Sort order

Emit Refresh events oldest-first by create time, with PID as a deterministic
tiebreaker. This mirrors Linux `ProcessRundownSensor` and preserves the parent
before child behavior required for resolver registration.

Apply duplicate suppression before or during emission, but do not reorder the
remaining Refresh events.

### Duplicate suppression

Suppress a snapshot Refresh when a live Start for the same instance is already
registered in the resolver.

Algorithm:

1. For each non-synthetic snapshot record, compute `lookupTime = createTimeUtc +
   SnapshotStartMatchTolerance`.
2. Resolve `ProcessRecord existing = resolveProcessAtTime(pid, lookupTime)`.
3. If `existing != null`, `existing.ProcessId == pid`, and
   `Abs(existing.CreateTime.ToUniversalTime() - createTimeUtc) <=
   SnapshotStartMatchTolerance`, do not emit the Refresh.
4. Increment an internal `SnapshotDedupSuppressedCount` counter for each
   suppressed Refresh.

Do not suppress the synthetic system process seeds through this path.

This rule intentionally allows Start to win when live ETW observes a process
during snapshot enumeration. It also explicitly handles the timing nuance that
live Start `PidHash` is based on the ETW process-start timestamp, while Refresh
`PidHash` is based on live `GetProcessTimes` create time.

### Error handling

Native enrichment must be isolated per process and per field. Protected
processes, exited processes, access-denied handles, WOW64 pointer-size issues,
and malformed PEB data are expected. Log at Debug for per-process enrichment
failures unless there is a broad enumerator failure.

Snapshot startup should return `false` only when enumeration itself fails before
any usable batch can be built. Individual process failures should be skipped or
emitted with empty enrichment fields as described above.

## Tests

Add wpc-03 tests under `tests/Wintap.Tests/`. Tests must not start live ETW
sessions, must not require administrator privileges, and must not depend on the
host's current process list.

Use the internal snapshot DTO/enumerator seam to test pure snapshot behavior.

Required tests:

1. Parent-instance selection chooses the latest parent create time preceding the
   child's create time when multiple parent PID instances exist in the snapshot
   fixture.
2. Parent-instance selection does not choose a parent instance whose create time
   is after the child's create time.
3. Refresh messages are emitted oldest-first with PID as a deterministic
   tiebreaker.
4. `ClearProcessDB()` seam is called before the first Refresh emit.
5. Synthetic system process seeds for PID 4, PID 0, and PID -1 are included with
   the same names, paths, parent PID, user, boot-time create time, and hash rules
   as the existing `ProcessSensor.Initialize()` behavior.
6. Dedup rule suppresses a Refresh when the resolver seam returns the same PID
   with create time within tolerance.
7. Dedup rule does not suppress a Refresh when the resolver seam returns null or
   a same-PID record outside tolerance.

If existing wpc-02 tests need constructor updates because of new optional seams,
keep their behavior unchanged and preserve their `[Trait("Category", "wpc-02")]`
coverage.

## Acceptance Criteria

1. `WindowsProcessSensor` exposes an internal/public startup snapshot method that
   replaces the old `ProcessSensor.Initialize()` behavior for the new sensor.
2. Snapshot enumeration uses live Windows APIs for exact create time, parent PID,
   path, PEB command line, and token user, with no Security-log dependency.
3. `EventChannel.ClearProcessDB()` is called once before Refresh emission begins.
4. The three synthetic system process seeds PID 4 / 0 / -1 match the existing
   `ProcessSensor.Initialize()` seed semantics.
5. Refresh events are emitted oldest-first and sent through the existing emit
   seam / `EventChannel.Send` production path.
6. Refresh `PidHash` uses the unchanged `ProcessHash.GenPidHash(pid,
   createTimeFileTimeUtc)` formula with live snapshot create time.
7. Live Start `PidHash` behavior from wpc-02 remains unchanged: ETW
   ProcessStart timestamp is canonical for Start.
8. Start-vs-Refresh dedup is resolver-backed by PID + create-time tolerance, with
   Start winning when a matching live Start is already registered.
9. No sensor-owned PID instance map is added.
10. `EventChannel.cs` and `ProcessResolver.cs` are not modified except for a
    compelling compile-only reason; if touched, document why in the audit.
11. No `WintapMessage` or `ProcessObject` schema changes are made.
12. No `PidHash` formula or `ProcessHash` behavior is changed.
13. TraceEvent remains at 3.1.23.
14. No new NuGet dependencies are added.
15. Unit tests under `tests/Wintap.Tests` are tagged
    `[Trait("Category", "wpc-03")]` and cover the required cases.
16. `dotnet build -c Release` succeeds.
17. `dotnet test --filter "Category=wpc-03"` succeeds and selects the wpc-03
    tests.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wpc-03"
```

If the repository-root commands still fail because `Wintap.sln` includes the
existing `Wintap-Workbench` website project (`MSB4249` under .NET SDK MSBuild),
run the closest project-scoped equivalents and document the deviation in
`developer_docs/audits/wpc-03-snapshot-refresh.md`:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test --filter "Category=wpc-03"
```

## Out of Scope

- Do not wire `WindowsProcessSensor` into `WindowsSubscriptionManager` yet.
- Do not delete `ProcessSensor.cs` or `KernelProcessSensor.cs` yet.
- Do not remove `System.Diagnostics.Eventing.Reader` usage yet.
- Do not change settings entries for `ProcessSensor` or `KernelProcessSensor`.
- Do not add boot ETL / Global Logger handling.
- Do not replay ETL files.
- Do not add manifest-provider ProcessStop metric correlation.
- Do not add SID extraction or token fallback to live ETW Start events.
- Do not change live Start create-time canonicalization from the ETW
  ProcessStart timestamp.
- Do not add per-Start `OpenProcess` / `GetProcessTimes` lookups for live ETW
  Starts.
- Do not add live ETW, admin-only, timing-sensitive, or reboot-dependent tests.
- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change `ProcessHash` or the `PidHash` formula.
- Do not upgrade TraceEvent or add any NuGet package.
