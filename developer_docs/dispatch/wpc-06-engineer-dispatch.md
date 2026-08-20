# Dispatch Prompt — Engineer — wpc-06 (wire-in and removal)

> Paste the block below to the **Engineer** subagent. It produces the
> instruction document `developer_docs/instructions/wpc-06-wire-in-removal.md`.
> After you (Architect) approve that instruction, dispatch the **Developer**
> with a pointer to the approved instruction file — the Developer implements
> the instruction, not this prompt.

---

Draft the instruction document for unit wpc-06 (wire-in and removal) of the
improve-windows-process-collection feature. Write it to
`developer_docs/instructions/wpc-06-wire-in-removal.md`. This unit closes
slice 1: the new WindowsProcessSensor becomes the only Windows process
sensor and the legacy paths are deleted.

## Feature context (read all of these first)

- `C:\PUBLIC\Wintap-Analytics\wiki\work\improve-windows-process-collection\brief.md`
- `C:\PUBLIC\Wintap-Analytics\wiki\work\improve-windows-process-collection\design.md`
- `C:\PUBLIC\Wintap-Analytics\wiki\work\improve-windows-process-collection\implementation_plan.md`
  (step 6 is this unit)
- `C:\PUBLIC\Wintap-Analytics\wiki\work\improve-windows-process-collection\dev_handoff.md`
- `C:\PUBLIC\wintap\developer_docs\audits\wpc-02-sensor-core.md` through
  `wpc-05-stop-metrics-merge.md` (what actually landed, including the
  counters that already exist, e.g. `ManifestMetricMissesCount`)

## Unit scope (from implementation_plan.md step 6)

1. **Wire-in:** `WindowsSubscriptionManager` starts `WindowsProcessSensor`
   first (process-sensor-first bootstrap; kernel flags already seed
   `Keywords.Process` — verify, don't re-add).
2. **Removal:** delete `ProcessSensor.cs` and `KernelProcessSensor.cs`,
   their settings entries in `wintap/Properties/Settings.*`, and the
   now-unused `System.Diagnostics.Eventing.Reader` usage. Search for any
   remaining references (MEF registration, config, using directives) so the
   build is clean with no orphans.
3. **QA counters:** logged at interval and on shutdown — `sid_extracted`,
   `sid_null`, `sid_malformed`, `sid_fallback`, `cmdline_empty`,
   `cmdline_peb_recovered`, `stop_without_start`, `manifest_metric_misses`,
   `snapshot_count`, `dedup_suppressed`. Reconcile these names with the
   counters already implemented in wpc-02..05 (map/rename in log output
   rather than duplicating state). Use the existing Wintap logging
   facility; no new settings unless the design already calls for one.

## Also read (for the instruction's code references)

- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
  (bootstrap ordering, kernel-flag aggregation, how legacy sensors are
  currently registered/started)
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs` and
  `KernelProcessSensor.cs` (everything that references them must go)
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs` (current
  state: counters, drain method, subscriptions)
- `wintap/platform/windows/sensor/shared/EtwKernelCollector.cs`
- `wintap/Properties/Settings.*` (legacy sensor settings entries)
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`

## Tests

Tagged `[Trait("Category", "wpc-06")]`: QA-counter snapshot/format logic
(counters report the right values after simulated activity through the
existing seams) and any pure wire-in logic that is testable without an ETW
session. Do not build heavy ETW test doubles; session behavior belongs to
the manual smoke run and wpc-08.

## Verification gate for this unit (stricter — end of slice 1)

- Release build green. Known issue: repo-root `dotnet build -c Release`
  fails on the Wintap-Workbench website project (MSB4249, pre-existing).
  Pre-approve the established fallback: `dotnet build wintap\Wintap.csproj
  -c Release` plus building `tests\Wintap.Tests`.
- Full feature suite green: `dotnet test --filter "Category~wpc"` (plus
  P1.1 regression: `dotnet test` with no filter on the test project).
- Documented manual smoke run (elevated, console or service mode): counts
  of Start/Stop/Refresh observed, QA counters logged at interval and
  shutdown, confirmation of no audit-policy dependency. The Developer
  should write the exact smoke-run procedure (commands, what to observe,
  pass criteria) into the audit; if the session cannot run elevated, the
  procedure is executed by the Architect and results are pasted into the
  audit before the unit is considered closed.

## Constraints and discipline

The instruction must be self-contained: the Developer will not read the
Analytics wiki. Carry over the constraints verbatim: no
WintapMessage/ProcessObject schema changes, no PidHash formula changes,
TraceEvent stays at 3.1.23, no new NuGet dependencies. Deletion scope is
exactly the two legacy sensors and their dead references — do not clean up
unrelated code encountered along the way; flag it instead. The Developer
files the audit at `developer_docs/audits/wpc-06-wire-in-removal.md` and
does not touch any wiki path.

Do not modify source, tests, or anything under `developer_docs/audits/`.
Do not update the implementation plan checklist (that happens at closeout).
