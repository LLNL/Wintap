# wpc-07 Boot ETL Coverage

**Date:** 2026-08-17
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-17

## Purpose

Open slice 2 of the `improve-windows-process-collection` feature: opt-in
early-boot process coverage via the Windows Global Logger boot session. When a
prior shutdown armed the Global Logger, Windows records classic kernel process
events from early boot into an ETL file. At the next Wintap startup, this unit
stops that boot session, replays the ETL, and emits Start events for early-boot
process instances that the live snapshot Refresh did not cover — giving the
process tree its early-boot roots (smss/csrss/wininit-era lineage) that neither
live ETW nor the snapshot can reconstruct for processes that exited before
Wintap started.

The entire feature is governed by a new `EnableBootProcessTrace` setting,
default **off**. When off, this unit's code must be fully inert: no registry
reads or writes on the boot-trace path, no session detection, no replay.

## Scope

Implement this unit in the `wintap` repository.

Modify:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs` — boot ETL
  replay, replay dedup, `boot_replay_count` QA counter.
- A new small internal helper (suggested:
  `wintap/platform/windows/sensor/etw/helpers/BootProcessTraceHelper.cs`) —
  Global Logger registry arm/disarm, boot-session detection/stop, and the pure
  session-ownership verification predicate.
- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs` —
  startup ordering call (detect/stop/disarm before any ETW session
  construction) and shutdown re-arm call.
- `wintap/platform/windows/sensor/shared/EtwKernelCollector.cs` — **ordering
  only**, and only if a minimal adjustment is unavoidable (see Implementation
  Notes). Do not redesign the singleton session/source/parser.
- `wintap/Properties/Settings.settings`, `wintap/Properties/Settings.Designer.cs`,
  `wintap/App.config` — new `EnableBootProcessTrace` boolean setting, default
  `False`, following the existing sensor-setting pattern.
- Unit tests under `tests/Wintap.Tests/`, tagged
  `[Trait("Category", "wpc-07")]`.

Hard constraints for this feature, repeated verbatim:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

Additional hard constraints from the feature's compatibility notes:

- The `ClearProcessDB()`-then-Refresh startup contract is preserved unchanged.
- Esper EPL files are unchanged.
- When `EnableBootProcessTrace` is off (the default), there are **no registry
  writes and no replay path** — rollback for a misbehaving field deployment is
  "turn the setting off".

## Dependencies

- Existing project rules: `CLAUDE.md`
- Prior units and audits (all Complete):
  - `developer_docs/instructions/wpc-02-sensor-core.md` /
    `developer_docs/audits/wpc-02-sensor-core.md` — `WindowsProcessSensor`
    core; ETW ProcessStart timestamp is the canonical live-start time;
    `ProcessResolver` is the sole hot-path identity store via the
    `resolveProcessAtTime` seam; Stops emit through resolver-backed identity.
  - `developer_docs/instructions/wpc-03-snapshot-refresh.md` /
    `developer_docs/audits/wpc-03-snapshot-refresh.md` —
    `InitializeSnapshotRefresh()` clears the process DB, emits synthetic seeds
    and Refresh events oldest-first, and suppresses duplicates via
    `IsDuplicateSnapshotRefresh` (PID + create-time within
    `SnapshotStartMatchTolerance`, currently 2 s).
  - `developer_docs/instructions/wpc-05-stop-metrics-merge.md` /
    `developer_docs/audits/wpc-05-stop-metrics-merge.md` — manifest metric
    session precedent for sensor-owned session/source/worker fields.
  - `developer_docs/instructions/wpc-06-wire-in-removal.md` /
    `developer_docs/audits/wpc-06-wire-in-removal.md` — `WindowsProcessSensor`
    is wired in first by `WindowsSubscriptionManager.Start()`; QA counter
    snapshot/format and interval/shutdown logging exist; legacy sensors are
    deleted.
- Feature design: `../Wintap-Analytics/wiki/work/improve-windows-process-collection/design.md`
  (Startup sequencing, Edge Cases, Risks) and `references.md` (Global Logger
  boot-trace procedure from the validated POC).

Relevant sources to read before editing:

- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs` —
  current bootstrap: construct `WindowsProcessSensor`, run
  `InitializeSnapshotRefresh()`, `Start()` it, seed
  `kernelFlags = KernelTraceEventParser.Keywords.Process`, add to
  `baseSensors`; kernel session enable + `source.Process()` happen later on the
  listener thread; `Stop()` stops `KernelSession.Instance.EtwSession`.
- `wintap/platform/windows/sensor/shared/EtwKernelCollector.cs` —
  `KernelSession` is a lazy singleton whose constructor does
  `new TraceEventSession("NT Kernel Logger", TraceEventSessionOptions.Create)`
  (this **clobbers** an existing session of that name); `KernelSource`
  constructs `new ETWTraceEventSource("NT Kernel Logger",
  TraceEventSourceType.Session)`. Both are first touched indirectly — the boot
  session must be stopped before either singleton is materialized.
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs` — existing
  seams: `resolveProcessAtTime`, `emit`, `utcNow`, `genPidHash`, `log`; the
  `IsDuplicateSnapshotRefresh` tolerance check; QA counter state, snapshot DTO,
  and formatter from wpc-06.

## Implementation Notes

### 1. New setting

Add `EnableBootProcessTrace` (`System.Boolean`, User scope, default `False`)
to `Settings.settings`, `Settings.Designer.cs`, and `App.config`, matching the
existing sensor-setting entries exactly in style.

When the setting is `False`, none of the boot-trace code below may run:
no registry access, no session detection or stop, no replay attempt, no re-arm
at shutdown. Guard at the call sites in `WindowsSubscriptionManager`.

### 2. Boot ETL path

Use one fixed, internal, named path constant — no second user-facing setting:

```csharp
// %ProgramData%\Wintap\boot-process-trace.etl
Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
             "Wintap", "boot-process-trace.etl")
```

Expose it as an internal static property (suggested: `BootTraceEtlPath`) on the
boot-trace helper so the arming write, the ownership predicate, and the replay
all use the same value. Ensure the directory exists before arming at shutdown.

### 3. Session-ownership verification predicate (pure, tested)

The Global Logger boot session runs under the session name
`"NT Kernel Logger"` — the same name another tool could own. Before treating a
live session as ours, verify ownership: **the session's log-file path must
match our configured boot ETL path.**

Implement as a pure, internal, static predicate (suggested signature):

```csharp
internal static bool IsOwnedBootSession(string sessionLogFilePath, string configuredEtlPath)
```

Rules:

- Null/empty/whitespace `sessionLogFilePath` → `false`.
- Compare after `Path.GetFullPath` normalization, trimmed, using
  `StringComparison.OrdinalIgnoreCase`.
- Any normalization exception → `false` (never throw).

If the predicate returns `false` for a live `"NT Kernel Logger"` session while
enabled: log one warning, do **not** stop the session, skip replay, and
continue normal startup. (Another tool owning the name is a pre-existing
Wintap-wide condition; `KernelSession` will clobber it exactly as it does
today — this unit must not make that worse or better.)

### 4. Startup sequencing (enabled only)

At the **top** of `WindowsSubscriptionManager.Start()`, before
`new WindowsProcessSensor()` and before anything can touch
`KernelSession`/`KernelSource`/`KernelParser`:

1. Detect whether an `"NT Kernel Logger"` session is active (TraceEvent's
   session query, e.g. `TraceEventSession.GetActiveSession` /
   `GetActiveSessionNames`, is sufficient; use the attached session's
   `FileName` for the predicate input).
2. If active and `IsOwnedBootSession(...)` is `true`: stop the session and
   record that a boot ETL is available for replay.
3. Disarm the registry arming: set
   `HKLM\SYSTEM\CurrentControlSet\Control\WMI\GlobalLogger` value `Start` to
   `0`. Disarm whenever the setting is enabled at startup — even if no session
   or file was found — so a stale arming never persists across a run.
4. All failures (registry access denied, session query/stop failure) log a
   warning and continue; boot-trace handling failures never block live
   collection.

If no owned session was found but the ETL file exists at `BootTraceEtlPath`
(e.g. the session was already stopped by boot-session auto-stop), the file may
still be replayed in step 5. If the file is absent or unreadable: log + skip;
the snapshot alone still yields a complete live tree (degraded lineage for
exited early-boot processes only — the accepted design behavior).

Keep the existing bootstrap ordering after this point exactly as wpc-06 landed
it: construct sensor → `InitializeSnapshotRefresh()` → `pc.Start()` → seed
`kernelFlags` → `baseSensors.Add(pc)`. `EtwKernelCollector.cs` should need no
change; touch it only if a minimal ordering adjustment is compelled, and
document why in the audit.

### 5. Boot ETL replay (enabled only)

After `InitializeSnapshotRefresh()` has completed **and** `pc.Start()` has
registered the live subscription, invoke replay (suggested: an internal
`ReplayBootTrace(string etlPath)` method on `WindowsProcessSensor`, called from
the manager when startup detection produced a replayable file).

Replay mechanics:

- Open the ETL with `ETWTraceEventSource(etlPath, TraceEventSourceType.FileOnly)`
  plus a `KernelTraceEventParser`; handle `ProcessStart` (and `ProcessDCStart`
  if present in the boot ETL) events; `Process()` the file to completion. File
  mode replay is synchronous and bounded by the small boot ETL — running it
  inline on the bootstrap path is acceptable; wrap the whole replay in
  try/catch so a corrupt ETL logs one warning and never blocks startup.
- For each replayed process-start record, extract the same primitive fields as
  the live Start path (PID, parent PID, image name, command line, SID via the
  wpc-01 helper — the helper already handles per-event `PointerSize` for
  cross-architecture ETLs). The replayed ETW event timestamp is the canonical
  create time for the instance (there is no live process to query).
- **Dedup:** emit a Start only for instances not already covered by Refresh.
  Reuse the existing resolver-based tolerance pattern from
  `IsDuplicateSnapshotRefresh`: look up
  `resolveProcessAtTime(pid, createTimeUtc + tolerance)` and suppress when a
  record for the same PID exists with create-time skew within tolerance. Use a
  named constant (suggested: `BootReplayMatchTolerance`) with the same 2-second
  value as `SnapshotStartMatchTolerance`; extracting a shared internal
  tolerance-check helper used by both paths is acceptable and makes the logic
  directly testable.
- Emitted replay Starts go through the existing `emit` seam so
  `ProcessResolver` registers them (lineage parents for later events).
  `PidHash` uses the unchanged formula with the replayed create time.
- Do **not** emit Stop events from the replay. Early-boot processes that
  exited pre-service matter only as lineage parents; this is settled design.
- Structure the per-record handling as an internal method taking primitive
  fields (suggested: `HandleReplayedStart(...)`) so tests can exercise dedup
  without an ETL file or ETW types.

### 6. `boot_replay_count` QA counter

Add a `boot_replay_count` counter (replay Starts actually emitted, not
suppressed duplicates) to the existing QA counter state, snapshot DTO, and
formatter from wpc-06. It appears in the interval and shutdown log line as an
eleventh `name=value` pair (the design's QA counter list already includes it).
Update the wpc-06 format/name tests minimally to expect the new pair — that is
a permitted, required test update, not a deviation.

### 7. Re-arm at shutdown (enabled only)

When `EnableBootProcessTrace` is `true`, re-arm the Global Logger at shutdown
so the *next* boot is captured. Do this from `WindowsSubscriptionManager.Stop()`
via the boot-trace helper (keeping all registry writes in one class), writing
under `HKLM\SYSTEM\CurrentControlSet\Control\WMI\GlobalLogger`:

- `Start` = `1` (REG_DWORD)
- `FileName` = `BootTraceEtlPath` (REG_SZ)
- `EnableKernelFlags` = binary value beginning `01 00 00 00`
  (EVENT_TRACE_FLAG_PROCESS), **zero-padded to 32 bytes** — the POC README
  documents that an unpadded value can fail the boot session.

Re-arm failures log a warning and never fail shutdown. When the setting is
off, `Stop()` performs no registry access.

### 8. Rider (optional, one line, low risk)

The wpc-06 smoke run showed interval/shutdown QA counter lines attributed to
`[WindowsProcessSensor..ctor]` — the default `log` delegate is a lambda created
in the constructor, so the logger's caller attribution reflects the
constructor instead of the emitting method. If a one-line fix is available
(e.g. replacing the default lambda with a small named wrapper method so
attribution lands on a sensibly named method), apply it and note it in the
audit. If it takes more than a trivially safe change, skip it and record that
instead. Cosmetic only; must not alter the QA counter line's
`Windows process QA counters:` payload format.

## Tests

Add tests under `tests/Wintap.Tests/`, tagged:

```csharp
[Trait("Category", "wpc-07")]
```

Tests must not start ETW sessions, must not read or write the registry, must
not require administrator privileges, must not use real ETL files, real
timers, `Thread.Sleep`, or reboot-dependent behavior. **ETW-session, registry,
and reboot behavior are explicitly out of unit-test reach and are deferred to
the wpc-08 validation harness (armed-reboot boot-coverage run). Do not build
heavy test doubles for them.**

Required wpc-07 tests:

1. **Session-ownership verification predicate.**
   - Matching path → `true`, including case differences and unnormalized forms
     (e.g. mixed separators or a redundant `.` segment) of the same file.
   - Non-matching path → `false`.
   - Null / empty / whitespace session log-file path → `false`.
   - Unnormalizable garbage input → `false`, no exception.
2. **Replay dedup tolerance logic.**
   - Via the internal replayed-start handling method with an injected
     `resolveProcessAtTime` seam and `emit` capture:
     - resolver returns a same-PID record with create time within
       `BootReplayMatchTolerance` → Start suppressed, `boot_replay_count`
       unchanged;
     - resolver returns a same-PID record outside tolerance (PID reuse) →
       Start emitted with the replayed timestamp as create time;
     - resolver miss → Start emitted;
     - emitted replay Starts increment `boot_replay_count`; suppressed ones do
       not.
3. **QA counter line includes `boot_replay_count`.**
   - The snapshot/format output contains `boot_replay_count=<n>` as a
     `name=value` pair alongside the existing ten counters; update the wpc-06
     exact-name-set assertions accordingly.

Existing wpc-01 through wpc-06 tests must continue to pass (with only the
counter-name-set update from note 6).

## Acceptance Criteria

1. `EnableBootProcessTrace` setting exists (`Boolean`, default `False`) in
   `Settings.settings`, `Settings.Designer.cs`, and `App.config`.
2. With the setting off, the boot-trace path is fully inert: no registry reads
   or writes, no session detection/stop, no replay attempt, no shutdown
   re-arm. (Compile-level/code-review criterion plus tests where seams allow.)
3. With the setting on, startup detects an active `"NT Kernel Logger"` session
   and stops it **only** when the session-ownership predicate confirms its log
   file matches the fixed boot ETL path; a non-matching session is left
   running with one warning logged.
4. The boot-session detect/stop/disarm runs before `WindowsProcessSensor`
   construction and before `KernelSession`/`KernelSource` singletons are
   materialized.
5. The registry arming is disarmed (`Start=0`) at startup whenever the setting
   is enabled.
6. Replay runs only after snapshot Refresh initialization and live
   subscription registration, uses `ETWTraceEventSource` file mode, and emits
   Start events only for instances not covered by Refresh (PID + create time
   within the named tolerance, resolver-backed).
7. Replayed Starts use the replayed ETW timestamp as canonical create time and
   the unchanged PidHash formula; no Stop events are emitted from replay.
8. Missing/corrupt ETL, registry failures, and session-stop failures log and
   never block live collection or startup.
9. Shutdown re-arms the Global Logger registry (Start=1, FileName, padded
   EnableKernelFlags) only when the setting is enabled.
10. `boot_replay_count` is counted and appears in the interval and shutdown QA
    counter lines; wpc-06 format tests are updated for the eleventh pair.
11. `ClearProcessDB()`-then-Refresh startup contract is unchanged.
12. No `WintapMessage`/`ProcessObject` schema changes; no PidHash formula
    changes; Esper EPL files unchanged; TraceEvent stays 3.1.23; no new NuGet
    dependencies.
13. Unit tests tagged `[Trait("Category", "wpc-07")]` cover the ownership
    predicate and dedup tolerance logic as specified; no ETW-session, registry,
    or reboot test doubles are built.
14. Full feature suite `Category~wpc` is green; unfiltered test-project run is
    green.
15. The Developer files the audit at
    `developer_docs/audits/wpc-07-boot-etl-coverage.md`.

## Test Command

Known issue: repo-root `dotnet build -c Release` and solution-scoped
`dotnet test` fail with the pre-existing `Wintap-Workbench` website-project
error (`MSB4249`: ASP.NET compiler only available on .NET Framework MSBuild).
The project-scoped fallback is pre-approved; run and document:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet build "tests\Wintap.Tests\Wintap.Tests.csproj"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-07"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"
```

The `Category~wpc` run is the regression gate for the counter-name-set update.

## Out of Scope

- Do not verify armed-reboot behavior, live boot-session stop, or registry
  arming at runtime — that is the wpc-08 harness (armed-reboot boot-coverage
  run).
- Do not add live ETW-session, registry-dependent, admin-only,
  timing-sensitive, or reboot-dependent unit tests; do not build heavy test
  doubles for ETW sessions or the registry.
- Do not add any user-facing setting beyond `EnableBootProcessTrace` (the ETL
  path is a fixed internal constant).
- Do not emit Stop events from boot ETL replay.
- Do not change live Start/Stop/Refresh semantics from wpc-02 through wpc-06
  except the specified dedup-helper extraction and `boot_replay_count`
  addition.
- Do not change the `ClearProcessDB()`-then-Refresh contract or Refresh
  ordering/seed/dedup semantics.
- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change `ProcessHash` or the `PidHash` formula.
- Do not upgrade TraceEvent or add any NuGet package.
- Do not redesign `EtwKernelCollector.cs`; ordering-only adjustment, and only
  if compelled — document in the audit.
- Do not fix the other wpc-06 smoke warnings (SensSensor load failure, missing
  `SignedS3UrlAdapter`, parent-process resolution warnings, DuckDB
  unterminated-quote parser errors); those are separate follow-up candidates.
- Do not add a sensor-owned PID instance map.
- Do not modify any wiki path.
- Do not update the implementation plan checklist; that happens at closeout.
