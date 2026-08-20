# wpc-02 Sensor Core

**Date:** 2026-08-17
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-17

## Purpose

Add the core of the new Windows process sensor for the
`improve-windows-process-collection` feature. This unit introduces a
`WindowsProcessSensor` that listens to classic kernel ETW process lifecycle
events from the existing shared NT Kernel Logger session, uses the ETW
ProcessStart timestamp as the canonical live Start time for stable `PidHash`
generation, leverages `ProcessResolver` as the single hot-path process identity
store, and emits Start/Stop `WintapMessage` process events.

This unit is intentionally narrow: it creates and tests the new sensor core but
does not replace the existing `ProcessSensor` / `KernelProcessSensor` runtime
wiring yet. Wire-in and deletion happen later in wpc-06.

## Scope

Implement this unit in the `wintap` repository.

Create:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- Unit tests under `tests/Wintap.Tests/`, tagged:
  `[Trait("Category", "wpc-02")]`

The new sensor must:

- be in namespace `gov.llnl.wintap.platform.windows.collect.etw`;
- follow the shared kernel-session subscription pattern used by `FileSensor` and
  `TcpSensor`:
  `KernelParser.Instance.EtwParser.<Event> += handler`;
- subscribe to classic kernel Process lifecycle events on the shared session:
  `ProcessStart` and `ProcessStop` as exposed by TraceEvent 3.1.23;
- contribute `KernelTraceEventFlags = KernelTraceEventParser.Keywords.Process`;
- canonicalize live process Start time from the ETW ProcessStart event
  timestamp;
- emit Start events from ProcessStart;
- emit Stop events from ProcessStop, resolving process identity through
  `ProcessResolver` / `EventChannel.GetProcessHistory` on the hot path;
- on Stop resolver miss, increment a stop-without-start counter and emit with
  hash-from-stop-time fallback.

Hard constraints for this feature, repeated verbatim:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

## Dependencies

- Existing project rules: `CLAUDE.md`
- Existing test project: `tests/Wintap.Tests/Wintap.Tests.csproj`
- Prior unit: `developer_docs/instructions/wpc-01-sid-helper.md`
  - wpc-01 added
    `wintap/platform/windows/sensor/etw/helpers/ProcessTraceDataExtensions.cs`.
  - Do not consume SID extraction in this unit; identity enrichment is later.
- Relevant sources to mirror:
  - `wintap/platform/windows/sensor/shared/EtwKernelCollector.cs`
    - owns `KernelSession`, `KernelSource`, and `KernelParser` singletons.
  - `wintap/platform/windows/sensor/etw/FileSensor.cs`
    - live example of `KernelParser.Instance.EtwParser.<Event> += handler`.
  - `wintap/platform/windows/sensor/etw/TcpSensor.cs`
    - additional shared-kernel-session subscription example.
  - `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
    - currently starts `ProcessSensor` first and aggregates kernel flags; do not
      change it in this unit unless strictly required for compilation.
  - `wintap/platform/windows/sensor/etw/ProcessSensor.cs`
    - current Security-log process Start/Refresh behavior being replaced later.
  - `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs`
    - current manifest-provider Stop behavior being absorbed later.
  - `wintap/core/shared/ProcessHash.cs`
    - existing `GenPidHash(pid, fileTimeUtc)` formula; do not change it.
  - `wintap/core/infrastructure/EventChannel.cs`
    - process events are registered/resolved there after `EventChannel.Send`.

### Known decision tension to preserve, not resolve in code

The consolidated wiki contains an older process identity ADR saying core owns
`PidHash` / `ParentPidHash` generation. For wpc-02, keep `ProcessResolver` as
the single hot-path process identity store and do not introduce a parallel
sensor-owned PID instance map. The sensor may still set Start `PidHash` using
the unchanged `ProcessHash.GenPidHash(pid, createTimeFileTimeUtc)` formula so
the resolver can register the Start row through `EventChannel.Send`, matching
existing process-event practice. Do not change schema, formula, `EventChannel`,
or `ProcessResolver` ownership beyond what this unit explicitly requires.

## Implementation Notes

### Sensor class shape

Create `internal class WindowsProcessSensor : EtwProviderCollector`.

Constructor defaults:

- `SensorName = "WindowsProcess"`
- `EtwProviderId = "SystemTraceControlGuid"`
- `KernelTraceEventFlags = KernelTraceEventParser.Keywords.Process`

`Start()` must:

1. call `base.Start()` if consistent with nearby sensors;
2. subscribe handlers on the shared kernel parser:
   - `KernelParser.Instance.EtwParser.ProcessStart += ...`
   - `KernelParser.Instance.EtwParser.ProcessStop += ...`
3. log that the Windows process sensor core is subscribed;
4. return `true`.

Do not create a new kernel ETW session. Do not call `EnableKernelProvider()`
from this sensor. The shared kernel session is enabled by
`WindowsSubscriptionManager`.

Do not wire `WindowsProcessSensor` into `WindowsSubscriptionManager` in this
unit. wpc-06 will replace `ProcessSensor` startup and retire the old settings.

### Test seams, kept minimal

`ProcessTraceData` is difficult to instantiate in unit tests, and live ETW tests
would require elevation/timing. Add only the minimal seams needed to test this
unit without live ETW:

- an internal resolver seam used by Stop processing;
- an internal emit seam so tests can capture `WintapMessage` output instead of
  sending to Esper/DuckDB;
- if needed, an internal clock seam for deterministic counters/logging.

Keep these seams inside `WindowsProcessSensor.cs`. Do not introduce a new
project-wide abstraction layer.

Suggested minimal shape:

```text
internal constructor parameters, all optional:
  Func<int, DateTime, ProcessRecord> resolveProcessAtTime
  Action<WintapMessage> emit
  Func<DateTime> utcNow
```

Production defaults:

- resolver fallback calls `EventChannel.GetProcessHistory(pid, eventTimeUtc)`;
- emit calls `EventChannel.Send(message)`;
- clock returns `DateTime.UtcNow`.

If tests cannot access internal members, use the same pattern as wpc-01:
`[assembly: InternalsVisibleTo("Wintap.Tests")]`.

### Create-time canonicalization

Add one helper that all Start-side `PidHash` computation goes through, for
example:

```text
internal DateTime CanonicalizeCreateTimeUtc(int pid, DateTime etwTimestampUtc)
```

Rules:

1. Normalize `etwTimestampUtc` to UTC.
2. Return the normalized ETW timestamp.

Do not perform `OpenProcess` / `GetProcessTimes` lookups on every ProcessStart
in this unit. ETW has been Wintap's long-running process-start ground truth, and
this unit should avoid per-process handle-open overhead in the hot path.

Do not change `ProcessHash.GenPidHash`. Use the canonical ETW Start time's
`ToFileTimeUtc()` as the existing formula input.

### Process identity storage

Do not add a sensor-owned PID instance map in this unit. `ProcessResolver` is
the single hot-path process identity store.

On Start:

- canonicalize create time from the ETW ProcessStart timestamp;
- compute `PidHash` with `ProcessHash.GenPidHash(pid, createTimeUtc.ToFileTimeUtc())`;
- leave `ParentPidHash` empty unless it is already trivially available from a
  resolver lookup; `EventChannel.Send` already performs parent resolution for
  process events before registering the Start;
- emit the Start through `EventChannel.Send` so `ProcessResolver.RegisterProcess`
  records the process instance.

On Stop:

- before emitting, resolve the stopped process via the resolver seam using the
  stopped PID and Stop event timestamp UTC;
- production resolver seam must call
  `EventChannel.GetProcessHistory(pid, stopEventTimeUtc)`;
- if resolver returns a process record, use its `PidHash`, `ParentPidHash`,
  `ParentProcessId`, `ProcessName`, and `ProcessPath` on the Stop message;
- if resolver returns null, increment an internal `StopWithoutStartCount` counter,
  compute `PidHash` with the unchanged formula using the Stop event timestamp
  file time, set name/path from the ETW stop payload if available, and leave
  parent fields blank or unknown;
- emit the Stop through `EventChannel.Send` so `ProcessResolver.RegisterProcess`
  can close/update the matching process row when `PidHash` resolves.

### Start event emission

The ProcessStart handler must translate TraceEvent's classic kernel
`ProcessTraceData` into an internal primitive input and call the testable Start
core. Use TraceEvent 3.1.23 property names as available in the package.

Populate Start `WintapMessage` as follows:

- constructor event time: canonical ETW Start time UTC;
- PID: started process PID;
- `MessageType = WintapMessage.MessageTypeEnum.Process`;
- `ActivityType = WintapMessage.ActivityTypeEnum.Start`;
- `PidHash`: computed from canonical ETW Start time;
- `ProcessName`: best available process name from ETW `ImageFileName`;
- `ProcessPath`: best available path/name from ETW `ImageFileName` for now;
- `Process = new WintapMessage.ProcessObject { ... }` with:
  - `PID`
  - `ParentPID`
  - `ParentPidHash` empty unless trivially resolved without adding new storage
  - `Name`
  - `Path`
  - `CommandLine` from ETW `CommandLine` if available, otherwise empty
  - `Arguments` same as command line or empty, matching existing sensor style

Do not add SID/user lookup in this unit. Leave `User` empty/unknown unless it is
already trivially available without using the wpc-01 helper.

Do not add path enrichment via `QueryFullProcessImageName` in this unit. That is
wpc-04.

### Stop event emission

The ProcessStop handler must translate TraceEvent's classic kernel stop payload
into an internal primitive input and call the testable Stop core.

Populate Stop `WintapMessage` as follows:

- constructor event time: Stop event timestamp UTC;
- PID: stopped process PID;
- `MessageType = WintapMessage.MessageTypeEnum.Process`;
- `ActivityType = WintapMessage.ActivityTypeEnum.Stop`;
- `PidHash`: from resolver, else unchanged formula using Stop timestamp file
  time;
- `ProcessName` / `ProcessPath`: from resolver, else ETW payload best effort;
- `Process = new WintapMessage.ProcessObject { ... }` with:
  - `PID`
  - `ParentPID`
  - `ParentPidHash`
  - `Name`
  - `Path`
  - `ExitCode` from the classic kernel stop payload (`ExitStatus` / equivalent)

Do not add manifest-provider resource metrics in this unit. CPU cycles, commit,
IO counts, hard faults, and token elevation are wpc-05.

### Error handling

ETW callbacks must not throw out of the callback. Catch per-event exceptions,
log at Debug/Warn consistent with nearby sensors, and continue processing later
events.

Start handling must not depend on opening the live process. Short-lived and
protected-process Starts must still emit from ETW payload data.

Resolver misses for Stop are expected during drops/startup gaps. They must be
counted as `StopWithoutStartCount` and must still emit a Stop event.

## Acceptance Criteria

1. `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs` exists and
   defines `internal class WindowsProcessSensor` in namespace
   `gov.llnl.wintap.platform.windows.collect.etw`.
2. `WindowsProcessSensor.Start()` subscribes to classic kernel ProcessStart and
   ProcessStop on `KernelParser.Instance.EtwParser` and does not create a new
   kernel ETW session.
3. The sensor contributes `KernelTraceEventFlags = Keywords.Process`.
4. Create-time canonicalization for live Starts uses the ETW ProcessStart
   timestamp normalized to UTC and does not open the live process.
5. Start emission computes `PidHash` with the existing
   `ProcessHash.GenPidHash(pid, createTimeFileTimeUtc)` formula and emits
   through `EventChannel.Send` so `ProcessResolver` can register the Start.
6. No sensor-owned PID instance map is added.
7. Stop emission uses `ProcessResolver` / `EventChannel.GetProcessHistory` to
   stamp `PidHash`, `ParentPidHash`, name, path, and parent PID when resolver
   data exists.
8. Stop resolver miss increments an internal counter and still emits a Stop
   event with hash-from-stop-time fallback.
9. Unit tests under `tests/Wintap.Tests/` are tagged
   `[Trait("Category", "wpc-02")]` and cover:
   - canonicalization normalizes the ETW ProcessStart timestamp to UTC;
   - Start `PidHash` uses the canonical ETW Start time;
   - Stop emission uses resolver-returned fields when resolver returns a record;
   - PID reuse is handled by resolver lookup result: when the resolver seam
     returns the newer instance for the Stop timestamp, the Stop uses that
     newer instance's `PidHash`;
   - Stop resolver miss increments the miss counter and emits with
     hash-from-stop-time fallback when the resolver returns null.
10. Tests do not start live ETW sessions and do not require administrator
    privileges.
11. No `WindowsSubscriptionManager` wire-in, old sensor deletion, Security-log
    removal, SID/user enrichment, snapshot refresh, boot ETL replay, or manifest
    stop metrics are implemented in this unit.
12. No `WintapMessage` or `ProcessObject` schema changes are made.
13. No `PidHash` formula or `ProcessHash` behavior is changed.
14. TraceEvent remains at 3.1.23.
15. No new NuGet dependencies are added.
16. `dotnet build -c Release` succeeds.
17. `dotnet test --filter "Category=wpc-02"` succeeds and selects the wpc-02
    tests.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wpc-02"
```

## Out of Scope

- Do not wire `WindowsProcessSensor` into `WindowsSubscriptionManager` yet.
- Do not delete `ProcessSensor.cs` or `KernelProcessSensor.cs` yet.
- Do not remove `System.Diagnostics.Eventing.Reader` usage yet.
- Do not change settings entries for `ProcessSensor` or `KernelProcessSensor`.
- Do not consume `ProcessTraceDataExtensions.TryGetUserSid` yet.
- Do not add SID-to-account lookup, SID caching, or token fallback.
- Do not add command-line PEB fallback.
- Do not add `QueryFullProcessImageName` or device-path translation.
- Do not add snapshot Refresh enumeration.
- Do not call `EventChannel.ClearProcessDB()`.
- Do not add manifest-provider ProcessStop metric correlation.
- Do not add boot ETL / Global Logger handling.
- Do not add live ETW, admin-only, timing-sensitive, or reboot-dependent tests.
- Do not add per-Start `OpenProcess` / `GetProcessTimes` lookups in this unit.
- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change `ProcessHash` or the `PidHash` formula.
- Do not upgrade TraceEvent or add any NuGet package.
