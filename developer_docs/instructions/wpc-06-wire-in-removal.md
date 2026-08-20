# wpc-06 Wire-in and Removal

**Date:** 2026-08-17
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-17

## Purpose

Close slice 1 of the `improve-windows-process-collection` feature. After this
unit, `WindowsProcessSensor` is the only Windows process lifecycle sensor used at
runtime. The old Security-log `ProcessSensor` path and old manifest-only
`KernelProcessSensor` path are deleted, their dead settings/references are
removed, and the unified sensor logs the slice-1 QA counters at interval and on
shutdown.

This unit is a wire-in/removal unit, not a redesign. wpc-02 through wpc-05
already landed the unified sensor core, snapshot Refresh, Start field enrichment,
and Stop metric merge. Preserve those semantics.

## Scope

Implement this unit in the `wintap` repository.

Modify only what is required to:

- make `WindowsSubscriptionManager` bootstrap `WindowsProcessSensor` first;
- remove the two legacy Windows process sensor files and their dead references;
- add QA counter snapshot/log formatting and interval/shutdown logging to the
  unified sensor;
- add wpc-06 tests for pure QA-counter snapshot/format logic and any pure wire-in
  logic that can be tested without an ETW session;
- file the audit at `developer_docs/audits/wpc-06-wire-in-removal.md`.

Expected files to modify/delete:

- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs` — delete
- `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs` — delete
- `wintap/Properties/Settings.settings`
- `wintap/Properties/Settings.Designer.cs`
- `wintap/App.config` if generated/default settings entries still contain the
  deleted process-sensor settings
- source files with compile-time references to the deleted sensors, for example
  `wintap/platform/windows/sensor/etw/APICallSensor.cs`
- tests under `tests/Wintap.Tests/`, tagged
  `[Trait("Category", "wpc-06")]`

Hard constraints for this feature, repeated verbatim:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

Deletion scope is exactly the two legacy sensors and their dead references. Do
not clean up unrelated code encountered along the way; flag unrelated issues in
the audit instead.

## Dependencies

- Existing project rules: `AGENTS.md`
- Prior unit and audit: `developer_docs/instructions/wpc-02-sensor-core.md`,
  `developer_docs/audits/wpc-02-sensor-core.md`
  - `WindowsProcessSensor` exists and subscribes to shared classic kernel
    `ProcessStart/ProcessStop` events.
  - Stop identity is resolver-backed through `ProcessResolver`.
  - Resolver misses increment `StopWithoutStartCount` and still emit Stop.
- Prior unit and audit: `developer_docs/instructions/wpc-03-snapshot-refresh.md`,
  `developer_docs/audits/wpc-03-snapshot-refresh.md`
  - `InitializeSnapshotRefresh()` clears the process DB, emits synthetic seeds,
    emits Refresh oldest-first, and suppresses resolver-matched live Start
    duplicates through `SnapshotDedupSuppressedCount`.
- Prior unit and audit: `developer_docs/instructions/wpc-04-field-enrichment.md`,
  `developer_docs/audits/wpc-04-field-enrichment.md`
  - Start enrichment uses SID extraction/account lookup/token fallback,
    command-line ETW field/PEB fallback, and path lookup/device-path fallback.
- Prior unit and audit: `developer_docs/instructions/wpc-05-stop-metrics-merge.md`,
  `developer_docs/audits/wpc-05-stop-metrics-merge.md`
  - `WindowsProcessSensor` owns the manifest-provider Stop metric subscription.
  - Classic kernel Stop remains the lifecycle source.
  - `ManifestMetricMissesCount` already exists and maps to QA counter
    `manifest_metric_misses`.

Relevant sources to read before editing:

- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
  - current process-sensor-first bootstrap uses `ProcessSensor pc = new
    ProcessSensor(); pc.Initialize(); pc.Start();`;
  - current manager seeds `kernelFlags = KernelTraceEventParser.Keywords.Process`
    immediately after starting the process sensor;
  - current modelled-sensor loop skips setting name `ProcessSensor`.
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
  - extend the landed sensor; do not replace it;
  - existing counters include `StopWithoutStartCount`,
    `SnapshotDedupSuppressedCount`, and `ManifestMetricMissesCount`.
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs`
  - old Security-log 4688/4689 sensor and log-wrap reconstruction; delete this
    file and remove all dependencies on it.
- `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs`
  - old manifest ProcessStop metric emitter; delete this file now that wpc-05
    merged its retained metrics into `WindowsProcessSensor`.
- `wintap/platform/windows/sensor/shared/EtwKernelCollector.cs`
  - shared `KernelSession`, `KernelSource`, and `KernelParser` behavior; do not
    create another kernel session in this unit.
- `wintap/Properties/Settings.settings`,
  `wintap/Properties/Settings.Designer.cs`, and `wintap/App.config`
  - remove only the deleted legacy process-sensor settings entries.
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
  - existing wpc-02 through wpc-05 tests and internal seams.

## Implementation Notes

### 1. Wire in `WindowsProcessSensor` first

Replace the current hardcoded legacy process bootstrap in
`WindowsSubscriptionManager.Start()` with `WindowsProcessSensor`.

Required runtime ordering:

1. Construct `WindowsProcessSensor` before any other Windows sensor.
2. Start/subscribe it before any other modelled or generic sensor is loaded.
3. Run its snapshot Refresh initialization as part of this first-sensor bootstrap
   so the old Security-log `ProcessSensor.Initialize()` reconstruction is gone.
4. Add the `WindowsProcessSensor` instance to `baseSensors` so its `Stop()` method
   is called during shutdown.
5. Keep the shared-kernel listener creation where the manager currently creates
   it, after modelled/generic sensor loading and kernel-flag aggregation, unless a
   compile/runtime issue forces a minimal adjustment.

The manager already seeds:

```csharp
kernelFlags = KernelTraceEventParser.Keywords.Process;
```

Verify that this remains true after the replacement. Do **not** add a second
Process flag path or a new setting just to enable process collection. The unified
process sensor is mandatory and first, not dynamically loaded from a setting.

Update the modelled-sensor loop so it no longer contains a special skip for a
deleted `ProcessSensor` setting. The loop should continue loading the remaining
settings-backed sensors exactly as before.

### 2. Delete legacy sensors and dead references

Delete exactly these files:

- `wintap/platform/windows/sensor/etw/ProcessSensor.cs`
- `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs`

Remove the settings entries that exist only for these legacy process paths:

- `ProcessSensor`
- `ProcessStopSensor`

Remove those entries from:

- `wintap/Properties/Settings.settings`
- `wintap/Properties/Settings.Designer.cs`
- `wintap/App.config` if present there

Search the repo for remaining compile-time references to the deleted sensors and
remove or replace only the dead reference. Known current references include:

- `WindowsSubscriptionManager.cs` constructing `ProcessSensor`
- `WindowsSubscriptionManager.cs` checking `sp.Name == "ProcessSensor"`
- `APICallSensor.cs` has `using static
  gov.llnl.wintap.platform.windows.collect.etw.ProcessSensor;`; remove this if it
  is unused after the legacy sensor is deleted
- `PluginManager.cs` sets `Properties.Settings.Default.ProcessSensor = true` when
  plugin event flags contain `Process`; remove or retarget this so it does not
  reference a deleted setting. Because `WindowsProcessSensor` is mandatory, plugin
  Process flags no longer need to enable a setting.

Also remove any now-unused `using System.Diagnostics.Eventing.Reader;` introduced
solely by `ProcessSensor.cs`. Do not remove unrelated Event Log readers such as
`EventlogSensor` or `WintapCoreSvcMgr` if they still use the API for non-process
functionality.

After deletion, searches for `ProcessSensor`, `KernelProcessSensor`, and
`ProcessStopSensor` should return no active Windows runtime/config references to
the deleted process sensors. Matches in documentation, audit artifacts, or Linux
helper names are not automatically defects; document any expected non-code matches
in the audit if they appear.

### 3. QA counter state and naming

Log these QA counters at interval and on shutdown, with these exact output names:

- `sid_extracted`
- `sid_null`
- `sid_malformed`
- `sid_fallback`
- `cmdline_empty`
- `cmdline_peb_recovered`
- `stop_without_start`
- `manifest_metric_misses`
- `snapshot_count`
- `dedup_suppressed`

Use the existing Wintap logging facility (`WintapLogger.Log.Append`). Do not add a
new settings entry for QA logging unless an existing design setting already calls
for one; slice 1 calls for log output only.

Map existing counters rather than duplicating state:

- `stop_without_start` maps to existing `StopWithoutStartCount`.
- `manifest_metric_misses` maps to existing `ManifestMetricMissesCount`.
- `dedup_suppressed` maps to existing `SnapshotDedupSuppressedCount`.

Add only the missing state needed for the remaining counters:

- `sid_extracted`: increment when a Start event's SID status is
  `SidParseStatus.Extracted`.
- `sid_null`: increment when a Start event's SID status is `SidParseStatus.NoSid`.
- `sid_malformed`: increment when a Start event's SID status is
  `SidParseStatus.Malformed`, including extraction exceptions that are converted
  to malformed.
- `sid_fallback`: increment when the token-user fallback path is used because SID
  status is `NoSid` or `Malformed`. Count the fallback attempt/path use; do not
  require that it successfully returns a non-empty user.
- `cmdline_empty`: increment when the ETW Start command-line field is blank and
  the PEB fallback path is attempted.
- `cmdline_peb_recovered`: increment when the ETW Start command-line field is
  blank and PEB fallback returns a non-empty command line.
- `snapshot_count`: record the number of Refresh messages actually emitted by the
  latest snapshot initialization, including synthetic system seeds and excluding
  duplicate Refreshes suppressed by dedup.

Do not rename the existing internal properties unless necessary. It is acceptable
to expose a small immutable/internal snapshot DTO or method for tests, as long as
the log output uses the snake_case names above.

Counter updates must not drop events. Counter/logging failures should be
best-effort and must not break Start/Stop/Refresh emission.

### 4. QA counter log format and cadence

Add a single, parseable log line format for the counter snapshot. Keep it stable
for wpc-08 smoke validation. Suggested format:

```text
Windows process QA counters: sid_extracted=<n> sid_null=<n> sid_malformed=<n> sid_fallback=<n> cmdline_empty=<n> cmdline_peb_recovered=<n> stop_without_start=<n> manifest_metric_misses=<n> snapshot_count=<n> dedup_suppressed=<n>
```

The exact prefix may vary, but all counter names above must appear exactly once in
the log line as `name=value` pairs.

Required logging points:

1. **Interval:** log the QA counter snapshot periodically while the sensor is
   running. Use a lightweight timer owned by `WindowsProcessSensor`, or reuse an
   existing sensor timing pattern if cleaner. Avoid creating a new user setting.
   A 60-second interval is acceptable for this unit unless a nearby Wintap pattern
   strongly suggests another interval.
2. **Shutdown:** override `WindowsProcessSensor.Stop()` so shutdown logs one final
   QA counter snapshot and stops/disposes any timer and owned manifest metric
   session/source/worker resources as safely as the current code allows. This
   final log is the shutdown evidence required for the smoke run.

Do not let QA logging hold locks around ETW callback work longer than necessary.
If counters are updated from multiple callbacks, use safe primitive updates
(`Interlocked` or equivalent) or clearly bounded locking.

### 5. Preserve process semantics

Preserve all behavior landed in earlier units:

- Start `PidHash` uses the ETW ProcessStart timestamp as the canonical live-start
  time.
- Refresh uses live process create times and existing snapshot ordering, synthetic
  seeds, and dedup behavior.
- Stop identity resolves through `ProcessResolver`; no sensor-owned PID instance
  map is introduced.
- Manifest provider Stop metrics enrich the classic kernel Stop; manifest events
  do not emit standalone Stop messages.
- Security-log 4688/4689 collection and reconstruction are removed; do not add a
  Security-log fallback.
- No audit-policy dependency remains for process telemetry.

## Tests

Add tests under `tests.Wintap.Tests`, tagged:

```csharp
[Trait("Category", "wpc-06")]
```

Tests must not start ETW sessions, require administrator privileges, rely on real
timers, use `Thread.Sleep`, or depend on the host process list. Do not build heavy
ETW test doubles; ETW session behavior belongs to the manual smoke run and wpc-08.

Required wpc-06 tests:

1. **QA counter snapshot names and format.**
   - Exercise the sensor through existing seams/internal methods so counters have
     non-zero representative values.
   - Assert the formatted log/snapshot contains exactly these names as
     `name=value`: `sid_extracted`, `sid_null`, `sid_malformed`, `sid_fallback`,
     `cmdline_empty`, `cmdline_peb_recovered`, `stop_without_start`,
     `manifest_metric_misses`, `snapshot_count`, `dedup_suppressed`.
2. **QA counter values after simulated activity.**
   - Use existing seams to simulate SID extracted/null/malformed Start cases,
     command-line empty with PEB recovered and not recovered, snapshot emission
     and dedup, Stop resolver miss, and manifest metric miss expiry.
   - Assert the snapshot reports the expected values and maps existing counters
     rather than resetting/duplicating them.
3. **Shutdown logging uses the same snapshot/format.**
   - Use the logger seam or equivalent testable hook to call `Stop()` without live
     ETW and assert a final QA counter line is emitted.
4. **Pure wire-in logic if testable without ETW.**
   - If a small seam/helper is introduced for process-sensor bootstrap ordering or
     kernel flag seeding, test that `WindowsProcessSensor` is the first process
     sensor and `Keywords.Process` remains seeded.
   - If this cannot be tested without constructing live ETW sessions or heavy
     manager test doubles, document that in the audit and rely on build plus
     manual smoke for runtime ordering.

Existing wpc-02 through wpc-05 tests must continue to pass.

## Acceptance Criteria

1. `WindowsSubscriptionManager.Start()` constructs and starts/subscribes
   `WindowsProcessSensor` first, before other Windows sensors.
2. Snapshot Refresh initialization for `WindowsProcessSensor` runs during the
   first-sensor bootstrap; old Security-log reconstruction is no longer called.
3. `kernelFlags` still includes `KernelTraceEventParser.Keywords.Process` through
   the existing manager seed; no duplicate Process flag setting path or new
   process-enable setting is added.
4. `WindowsProcessSensor` is added to `baseSensors` so its `Stop()` runs on
   shutdown.
5. `ProcessSensor.cs` is deleted.
6. `KernelProcessSensor.cs` is deleted.
7. Legacy settings entries `ProcessSensor` and `ProcessStopSensor` are removed
   from `Settings.settings`, `Settings.Designer.cs`, and default config if
   present.
8. Compile-time references to deleted sensors are removed, including MEF/dynamic
   loading/config references and using directives.
9. Unrelated `System.Diagnostics.Eventing.Reader` usages that still serve other
   sensors/tools are left alone; only process-sensor-deletion dead usage is
   removed.
10. QA counters log at interval and on shutdown using existing Wintap logging.
11. QA counter log output uses the exact snake_case names listed in this
    instruction.
12. Existing counter state is mapped into log output for
    `stop_without_start`, `manifest_metric_misses`, and `dedup_suppressed`; state
    is not duplicated for those counters.
13. No `WintapMessage` or `ProcessObject` schema changes are made.
14. No `PidHash` formula or `ProcessHash` behavior is changed.
15. TraceEvent remains at 3.1.23.
16. No new NuGet dependencies are added.
17. No Security-log process fallback or audit-policy dependency is introduced.
18. No boot ETL / Global Logger work is added in this unit.
19. Unit tests tagged `[Trait("Category", "wpc-06")]` cover QA counter
    snapshot/format/value behavior and any pure wire-in logic that is testable
    without live ETW.
20. Full feature suite `Category~wpc` is green.
21. P1.1 regression suite on the test project is green.
22. Release build gate is green using the primary command or the pre-approved
    project-scoped fallback for the known website-project issue.
23. Manual smoke run is documented in
    `developer_docs/audits/wpc-06-wire-in-removal.md` with commands, observations,
    pass/fail criteria, Start/Stop/Refresh counts, interval and shutdown QA
    counter evidence, and audit-policy independence evidence.

## Test Command

Primary commands from the repository root:

```powershell
dotnet build -c Release
dotnet test --filter "Category~wpc"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"
```

Known issue: repo-root `dotnet build -c Release` and solution-scoped test commands
can fail because `Wintap.sln` includes the pre-existing `Wintap-Workbench` website
project (`MSB4249`: ASP.NET compiler only available on .NET Framework MSBuild).
If that happens, the established fallback is pre-approved. Run and document:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet build "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"
```

The unfiltered test-project run is required for the P1.1 regression gate.

## Manual Smoke Run Requirement

The Developer must write the exact smoke-run procedure and results into
`developer_docs/audits/wpc-06-wire-in-removal.md`. The procedure must be usable by
the Architect if the Developer session cannot run elevated.

Minimum smoke coverage:

1. Run Wintap elevated in console or service mode from the built Release output.
2. Generate a small process workload that creates and exits processes.
3. Observe and record counts/evidence for process `Start`, `Stop`, and `Refresh`
   events.
4. Observe and record at least one interval QA counter log line.
5. Stop Wintap cleanly and record the shutdown QA counter log line.
6. Confirm no Security-log audit policy dependency:
   - Either run on a host with process creation/termination audit policy disabled,
     or document the audit-policy state and the absence of Security-log process
     dependency in logs/code path.
   - Pass criterion: process Start/Stop/Refresh telemetry is still observed from
     the unified ETW/snapshot path, not from Security-log 4688/4689 collection.

If the Developer cannot run elevated, the Developer must still write the exact
commands, observations to collect, and pass criteria into the audit. The Architect
will execute the procedure and paste results into the audit before the unit is
considered closed.

## Audit Requirement

File the audit at:

```text
developer_docs/audits/wpc-06-wire-in-removal.md
```

The audit must include:

- files changed and files deleted;
- searches performed for deleted-sensor references and the results;
- build/test commands and results, including any MSB4249 fallback;
- manual smoke-run procedure and results or Architect-execution handoff;
- any unrelated cleanup opportunities explicitly flagged as out of scope;
- confirmation that no wiki path was touched by the Developer.

Do not update the implementation plan checklist; that happens at closeout.

## Out of Scope

- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change `ProcessHash` or the `PidHash` formula.
- Do not upgrade TraceEvent or add any NuGet package.
- Do not add new user-facing settings for process collection or QA logging.
- Do not add boot ETL / Global Logger coverage; that is wpc-07.
- Do not replay ETL files.
- Do not add live ETW-session, admin-only, timing-sensitive, or reboot-dependent
  unit tests.
- Do not build heavy ETW test doubles.
- Do not add a sensor-owned PID instance map.
- Do not change Start enrichment behavior except to count/log QA metrics.
- Do not change Refresh semantics except to count/log QA metrics.
- Do not change Stop resolver-backed identity semantics except to count/log QA
  metrics.
- Do not add Security-log 4688/4689 process fallback behavior.
- Do not clean up unrelated code encountered while searching; flag it in the
  audit instead.
- Do not modify any wiki path.
- Do not update the implementation plan checklist.
