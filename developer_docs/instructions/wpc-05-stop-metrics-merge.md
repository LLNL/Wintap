# wpc-05 Stop Metrics Merge

**Date:** 2026-08-17
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-17

## Purpose

Add manifest-provider ProcessStop resource metrics to the unified
`WindowsProcessSensor` for the `improve-windows-process-collection` feature.
The sensor already emits resolver-backed classic kernel ETW Start/Stop events
and snapshot Refresh events from wpc-02 through wpc-04. This unit adds a small
user-mode ETW subscription to `Microsoft-Windows-Kernel-Process` solely to
capture the rich ProcessStop counters that existed in `KernelProcessSensor`, then
merges those counters into the kernel Stop message emitted by
`WindowsProcessSensor`.

This unit keeps process identity resolver-backed. It does not introduce a
sensor-owned PID instance map, does not wire the new sensor into runtime startup,
does not delete the old sensors, and does not change the process-event schema.

## Scope

Implement this unit in the `wintap` repository.

Modify:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- helper files under `wintap/platform/windows/sensor/etw/helpers/` only if needed
  to keep the sensor readable
- unit tests under `tests/Wintap.Tests/`, tagged:
  `[Trait("Category", "wpc-05")]`

The implementation must:

- add a user-mode ETW subscription owned by `WindowsProcessSensor` to provider
  `Microsoft-Windows-Kernel-Process` / GUID
  `22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716`;
- enable keyword `0x10` (`WINEVENT_KEYWORD_PROCESS`; existing old code used
  decimal `16`);
- parse only manifest `ProcessStop/Stop` events for metric enrichment;
- correlate manifest stops to classic kernel ProcessStop events by PID and
  nearest event time within an initial 5-second window;
- expose the 5-second window as a named constant or internal setting so wpc-08
  tuning does not require code archaeology;
- merge the retained manifest metrics into the single kernel Stop emission;
- emit Stops with default metric values and increment `manifest_metric_misses`
  when no manifest match is available by expiry;
- avoid blocking ETW callbacks while waiting for manifest events;
- keep Stop identity and PID-reuse disambiguation in `ProcessResolver`.

Hard constraints for this feature, repeated verbatim:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

## Dependencies

- Existing project rules: `AGENTS.md`
- Existing test project: `tests/Wintap.Tests/Wintap.Tests.csproj`
- Prior unit and audit: `developer_docs/instructions/wpc-02-sensor-core.md`,
  `developer_docs/audits/wpc-02-sensor-core.md`
  - `WindowsProcessSensor` subscribes to shared classic kernel
    `ProcessStart/ProcessStop` events.
  - Stop identity is resolver-backed through the `resolveProcessAtTime` seam.
  - Resolver misses increment `StopWithoutStartCount` and still emit Stop.
- Prior unit and audit: `developer_docs/instructions/wpc-03-snapshot-refresh.md`,
  `developer_docs/audits/wpc-03-snapshot-refresh.md`
  - Refresh seeds resolver state through snapshot events.
- Prior unit and audit: `developer_docs/instructions/wpc-04-field-enrichment.md`,
  `developer_docs/audits/wpc-04-field-enrichment.md`
  - Start enrichment has landed; Refresh and existing Stop identity behavior must
    not be redesigned.

Relevant sources to read and preserve:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
  - extend the landed sensor; do not replace it with a new design.
  - reuse existing injectable resolver, emit, clock, and hash seams where
    possible.
- `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs`
  - old manifest ProcessStop metric source; this file is deleted in wpc-06, so
    do not leave new behavior dependent on it.
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs`
  - old Security-log path; read only for context. Do not add Security-log
    fallback behavior.
- `wintap/platform/windows/sensor/shared/EtwProviderSensor.cs`
  - user-mode provider session precedent.
- `wintap/core/infrastructure/EventChannel.cs` and
  `wintap/core/infrastructure/ProcessResolver.cs`
  - read-only for this unit unless a compelling compile-only adjustment is
    unavoidable. The sensor remains resolver-backed.
- `shared/WintapAPI/WintapMessage.cs`
  - existing `ProcessObject` metric fields already exist; do not add schema.

## Design Constraints

### One lifecycle Stop, enriched by a second provider

Classic kernel ETW ProcessStop remains the lifecycle source of truth for Stop
emission. The manifest provider contributes only metrics. There must be one Stop
message emitted for one kernel Stop event. Do not emit a separate manifest Stop
message and do not let manifest events create process identity.

### Manifest metric field list to retain

The old `KernelProcessSensor.parseUserModeProcessStop` parsed the following
manifest `ProcessStop/Stop` payload fields. Carry this list into the
implementation so wpc-06 can delete the old file safely:

- `ProcessID` — correlation key only; do not use as identity by itself.
- `ImageName` — available manifest image name; do not let it override
  resolver-backed Stop name/path when resolver identity is available.
- `ExitCode` — may confirm the kernel Stop exit code, but the kernel Stop
  `ExitStatus` remains the lifecycle exit-code source unless absent.
- `CPUCycleCount` — merge to `Process.CPUCycleCount`.
- `CreateTime` — parsed by the old code but not used for Wintap `PidHash`; do not
  recompute identity from it.
- `CommitCharge` — merge to `Process.CommitCharge`.
- `CommitPeak` — merge to `Process.CommitPeak`.
- `HardFaultCount` — merge to `Process.HardFaultCount`.
- `ReadOperationCount` — merge to `Process.ReadOperationCount`.
- `ReadTransferKiloBytes` — merge to `Process.ReadTransferKiloBytes`.
- `TokenElevationType` — merge to `Process.TokenElevationType`.
- `WriteOperationCount` — merge to `Process.WriteOperationCount`.
- `WriteTransferKiloBytes` — merge to `Process.WriteTransferKiloBytes`.
- `ActivityID` — the old code copied this to `WintapMessage.ActivityId` when
  available.
- `CorrelationId` — the old code copied this to `WintapMessage.CorrelationId`
  when available.

The retained ProcessObject metric set is therefore exactly:

- `CPUCycleCount`
- `CPUUtilization` with the old default value `0`
- `CommitCharge`
- `CommitPeak`
- `HardFaultCount`
- `ReadOperationCount`
- `ReadTransferKiloBytes`
- `TokenElevationType`
- `WriteOperationCount`
- `WriteTransferKiloBytes`

When no manifest match is available, these fields must remain their default
values (`0` for numeric fields) and Stop still emits.

### Correlation window and emission behavior

Use a named correlation window of 5 seconds for this unit. Do not add a new
user-facing setting entry unless the existing design already calls for it. An
internal constant such as `StopMetricCorrelationWindow` is sufficient.

Correlation rules:

1. Match by same PID.
2. Among candidates within the 5-second window, choose the nearest event time to
   the kernel Stop time.
3. If a matching manifest metric event arrives before expiry, emit the kernel
   Stop promptly with metrics merged.
4. If the kernel Stop arrives after a manifest metric event already buffered for
   the same PID and within the window, emit promptly with the nearest buffered
   metrics.
5. If no match exists when the pending kernel Stop expires, emit promptly with
   default metric values and increment `ManifestMetricMissesCount` (the QA
   counter named `manifest_metric_misses`).
6. If a manifest event arrives after the matching kernel Stop has already expired
   and emitted default metrics, do not emit a duplicate Stop and do not let the
   late manifest event poison a later reused PID instance.

Do not block an ETW callback thread with `Thread.Sleep`, waits, locks held across
delays, or synchronous polling. It is acceptable for the final Stop emission to be
scheduled/queued until a match or expiry, but the callback must return quickly.
Use the existing `utcNow` seam and an internal drain method so tests can advance
time deterministically without real timers or sleeps.

### Resolver-backed identity and PID reuse

Stop identity remains exactly the wpc-02 design:

- Resolve identity through `resolveProcessAtTime(pid, kernelStopTimeUtc)` when
  emitting the Stop.
- Use the resolver-returned `PidHash`, `ParentPidHash`, parent PID, name, and path
  when available.
- On resolver miss, keep the existing fallback behavior: increment
  `StopWithoutStartCount`, generate the fallback Stop-time `PidHash`, and emit.
- Do not add a sensor-owned PID instance map.

The manifest correlation buffer may hold short-lived pending metric DTOs keyed by
PID and timestamp, but it must not become a process identity store. PID-reuse
disambiguation is by nearest kernel Stop time for metric matching and by
`ProcessResolver` for identity.

### User-mode provider subscription

`WindowsProcessSensor.Start()` currently subscribes to shared classic kernel
`ProcessStart` and `ProcessStop`. Extend it so the same sensor also starts a
small user-mode ETW listener for the manifest provider. Because the sensor's
`EtwProviderId` is already `SystemTraceControlGuid` for kernel-flag aggregation,
do not change that property to the manifest provider.

Implementation expectations:

- Keep `KernelTraceEventFlags = KernelTraceEventParser.Keywords.Process`.
- Add private manifest session/source fields owned by `WindowsProcessSensor`, or
  a very narrow helper, following the `EtwProviderCollector` pattern.
- Use a stable session name under `Wintap.Collectors.*`, for example
  `Wintap.Collectors.WindowsProcess.Metrics`.
- Enable provider GUID `22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716` with keyword
  `0x10`.
- Register a parser callback and process only events whose provider is
  `Microsoft-Windows-Kernel-Process` and whose event name is `ProcessStop/Stop`.
- If the manifest subscription fails, log a single warning and continue running
  the classic kernel lifecycle subscription; Stops then emit metric-less and
  misses are counted as appropriate.

Do not build heavy ETW test doubles for session behavior. ETW-session behavior is
covered by wpc-08 validation.

## Implementation Notes

### Suggested metric DTOs

Add internal primitive DTOs so tests can exercise correlation without live ETW:

- A manifest stop metrics DTO containing:
  - `Pid`
  - `TimestampUtc`
  - `ImageName`
  - `ExitCode`
  - `CPUCycleCount`
  - `CommitCharge`
  - `CommitPeak`
  - `HardFaultCount`
  - `ReadOperationCount`
  - `ReadTransferKiloBytes`
  - `TokenElevationType`
  - `WriteOperationCount`
  - `WriteTransferKiloBytes`
  - optional `ActivityId`
  - optional `CorrelationId`
- A pending kernel Stop DTO containing:
  - `Pid`
  - `StopTimeUtc`
  - `ImageFileName`
  - `ExitCode`
  - `ExpiresAtUtc`

Names are flexible, but keep them internal and local to the sensor/helper. Do not
introduce a project-wide abstraction layer.

### Stop flow

Refactor existing Stop emission without changing its public behavior:

1. Keep a core method that creates and emits a Stop message from primitive kernel
   Stop fields plus an optional manifest metric DTO.
2. Existing resolver-backed identity logic should live in that core method.
3. The classic kernel ETW callback should enqueue/correlate the kernel Stop
   instead of immediately emitting metric-less Stops unconditionally.
4. Preserve existing internal `EmitStop(...)` usability for prior tests if
   practical, either as a wrapper that emits immediately with no metrics or as the
   core method used after correlation. Existing wpc-02 tests must keep passing.

When metrics are merged, populate the existing `ProcessObject` fields listed
above. Also copy manifest `ActivityID` and `CorrelationId` to the top-level
message if present and if those properties already exist on `WintapMessage`.

### Correlation buffer behavior

Keep two bounded/short-lived collections:

- pending kernel Stops waiting for metrics until `stopTime + 5s`;
- recent manifest metric events waiting for a kernel Stop within the same window.

On every kernel Stop, manifest Stop, and explicit drain call:

- try to match pending kernel Stops to nearest manifest metrics;
- emit matched Stops and remove both sides;
- emit expired kernel Stops with defaults and increment `ManifestMetricMissesCount`;
- prune manifest metrics older than the window so stale metrics cannot match a
  later PID reuse.

Use simple locking around collection mutation if callbacks can arrive on multiple
threads. Do not hold locks while calling `emit(message)` if avoidable.

### Payload parsing

Use safe numeric parsing similar to existing `TryGetLongPayload` /
`TryGetIntPayload`: handle thousands separators by removing commas, tolerate
missing fields, and default missing/unparseable metrics to zero. A malformed
manifest metric event must not throw out of the callback and must not drop the
classic kernel Stop.

The old `CreateTime` parsing helper in `KernelProcessSensor` must not be used to
compute `PidHash`. If retained for diagnostics or DTO completeness, keep it
best-effort and do not let failures affect Stop emission.

## Tests

Add wpc-05 tests under `tests.Wintap.Tests`. Tests must not start live ETW
sessions, must not require administrator privileges, must not use real timers or
`Thread.Sleep`, and must not depend on the host's current process list.

Use internal DTOs/methods and the existing injectable resolver, emit, clock, and
hash seams. Keep seams minimal. Do not introduce heavy ETW test doubles.

Required tests, all tagged `[Trait("Category", "wpc-05")]`:

1. **Correlation window hit — metrics merged.**
   - Arrange a kernel Stop and a manifest ProcessStop for the same PID within the
     5-second window.
   - Exercise both ordering cases if practical: manifest before kernel and kernel
     before manifest.
   - Assert exactly one Stop is emitted.
   - Assert resolver-backed identity fields still come from the resolver.
   - Assert the retained metric fields are populated from manifest metrics:
     `CPUCycleCount`, `CommitCharge`, `CommitPeak`, `HardFaultCount`,
     `ReadOperationCount`, `ReadTransferKiloBytes`, `TokenElevationType`,
     `WriteOperationCount`, `WriteTransferKiloBytes`, and `CPUUtilization = 0`.
2. **Correlation miss — defaults emitted.**
   - Arrange a kernel Stop with no manifest match.
   - Advance the test clock past the named 5-second window and drain.
   - Assert one Stop is emitted with default metric values and normal exit code /
     resolver-backed identity.
3. **Expiry — `manifest_metric_misses` incremented and callback not blocked.**
   - Assert the kernel Stop handler returns without synchronous waiting when no
     manifest event is present.
   - Advance the test clock to expiry and drain.
   - Assert `ManifestMetricMissesCount` increments by one and emission occurs at
     expiry without real-time sleeps.
4. **PID-reuse resolver interaction.**
   - Arrange two process instances with the same PID at different stop times and
     resolver seam results that differ by stop timestamp.
   - Provide a manifest Stop nearest to only one kernel Stop.
   - Assert the manifest metrics merge into the nearest matching Stop only.
   - Assert each emitted Stop resolves identity using the kernel Stop timestamp,
     so the reused PID's other instance is not poisoned by the manifest metrics.

Existing wpc-02, wpc-03, and wpc-04 tests should continue to pass after any
constructor seam updates, but this unit's required verification filter is
`Category=wpc-05`.

## Acceptance Criteria

1. `WindowsProcessSensor` starts a user-mode manifest subscription to
   `Microsoft-Windows-Kernel-Process` GUID
   `22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716` with keyword `0x10` for
   ProcessStop metrics.
2. Manifest subscription failure degrades to metric-less Stops with a warning; it
   does not fail the classic kernel lifecycle sensor.
3. The implementation parses and retains the exact ProcessStop metric set listed
   in this instruction from old `parseUserModeProcessStop` behavior.
4. Classic kernel ProcessStop remains the lifecycle source; manifest events do
   not emit standalone process Stop messages.
5. Manifest metrics correlate to kernel Stops by PID nearest-in-time within the
   named initial 5-second window.
6. The 5-second correlation window is a named constant or internal setting and no
   new user-facing settings entry is added.
7. ETW callbacks do not block waiting for manifest events.
8. Stops with no manifest match emit with default metric values after expiry and
   increment the QA counter `manifest_metric_misses` / internal
   `ManifestMetricMissesCount`.
9. Late or unmatched manifest events are pruned and do not produce duplicate Stops
   or poison later PID reuse.
10. Stop identity remains resolver-backed by `ProcessResolver` through the
    existing resolver seam at the kernel Stop timestamp.
11. No sensor-owned PID instance map is added.
12. Existing resolver-miss fallback behavior and `StopWithoutStartCount` remain
    intact.
13. No `WintapMessage` or `ProcessObject` schema changes are made.
14. No `PidHash` formula or `ProcessHash` behavior is changed.
15. TraceEvent remains at 3.1.23.
16. No new NuGet dependencies are added.
17. No Security-log fallback behavior is added.
18. Unit tests under `tests/Wintap.Tests` are tagged
    `[Trait("Category", "wpc-05")]` and cover the required cases.
19. `dotnet build -c Release` succeeds, or the documented project-scoped fallback
    succeeds if the known solution-level website-project issue is encountered.
20. `dotnet test --filter "Category=wpc-05"` succeeds and selects the wpc-05
    tests, or the documented project-scoped fallback succeeds if the known
    solution-level website-project issue is encountered.
21. The Developer files the audit at
    `developer_docs/audits/wpc-05-stop-metrics-merge.md`.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wpc-05"
```

If the repository-root commands still fail because `Wintap.sln` includes the
existing `Wintap-Workbench` website project (`MSB4249` under .NET SDK MSBuild),
run the closest project-scoped equivalents and document the deviation in
`developer_docs/audits/wpc-05-stop-metrics-merge.md`:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-05"
```

## Out of Scope

- Do not wire `WindowsProcessSensor` into `WindowsSubscriptionManager` yet.
- Do not delete `ProcessSensor.cs` or `KernelProcessSensor.cs` yet.
- Do not remove `System.Diagnostics.Eventing.Reader` usage yet.
- Do not change settings entries for `ProcessSensor` or `KernelProcessSensor`.
- Do not add QA counter interval/shutdown logging beyond the narrow
  `manifest_metric_misses` counter needed for this unit; wpc-06 handles broader
  QA counter logging.
- Do not add boot ETL / Global Logger handling.
- Do not replay ETL files.
- Do not add live ETW-session, admin-only, timing-sensitive, or reboot-dependent
  tests.
- Do not build heavy ETW test doubles; ETW-session behavior is wpc-08's job.
- Do not add a sensor-owned PID instance map.
- Do not change Start enrichment behavior from wpc-04.
- Do not change Refresh create-time, ordering, synthetic seed, or dedup semantics
  from wpc-03.
- Do not change Stop resolver-backed identity semantics from wpc-02.
- Do not change live Start create-time canonicalization from the ETW
  ProcessStart timestamp.
- Do not add per-Start `OpenProcess` / `GetProcessTimes` lookups for live ETW
  Starts.
- Do not change Linux/macOS paths.
- Do not change `EventChannel.cs` or `ProcessResolver.cs` except for a compelling
  compile-only reason; if touched, document why in the audit.
- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change `ProcessHash` or the `PidHash` formula.
- Do not upgrade TraceEvent or add any NuGet package.
- Do not update the implementation plan checklist; that happens at closeout.
