# wpc-09 Minor Bug Sweep

**Date:** 2026-08-18
**Status:** Complete
**Author:** Engineer
**Architect Approval:** Approved 2026-08-18

## Purpose

Close the final code unit of the `improve-windows-process-collection` feature
before close-out. This unit fixes the small set of issues found in the
2026-08-18 Architect overnight smoke after wpc-07: boot-trace arm/disarm
lifecycle gaps, a small number of parent-resolution warnings, process-name /
DuckDB command-line parsing errors, and the cosmetic QA-counter logger tag if it
is still present. The unit also restores the intended OS-specific runtime data
root after the reboot smoke exposed that the shipped `/tmp/lintap-data` default
placed the Windows DuckDB under `C:\tmp` and allowed unrelated file contention
to prevent Wintap startup.

wpc-08 is intentionally skipped and must not be renumbered. The Architect
accepted manual slice-2 validation on 2026-08-18: full process tree back to
kernel-era roots, usernames on all reviewed records, and a stable overnight run.

## Scope

Implement this unit in the `wintap` repository. Modify only what is required for
the items below and file the audit at
`developer_docs/audits/wpc-09-bug-sweep.md`.

In scope:

1. **Boot-trace arm/disarm lifecycle fix.** Fix both:
   - enabling `EnableBootProcessTrace=True` then restarting Wintap must arm the
     Global Logger without manual registry setup; and
   - disabling the setting after it was armed must clean up Wintap-owned Global
     Logger state/session instead of leaving boot tracing unattended.
2. **Missing-parent warning triage.** Use Wintap logs first. If a concrete
   resolution gap is found, fix it. If the warnings are expected races or
   genuinely unresolvable parents, rate-limit and annotate the warning so it is
   actionable and not misleading.
3. **Process-name / DuckDB command-line parsing triage.** Use Wintap logs first.
   If the root is the DuckDB insert path for process command lines, fix
   parameterization/escaping there. Preserve the original command-line data; do
   not sanitize or truncate telemetry to hide parser errors.
4. **Cosmetic QA-counter logger tag.** Check whether the optional wpc-07 rider
   already fixed `[WindowsProcessSensor..ctor]` attribution on QA-counter lines.
   If still broken and a low-risk local fix is available, apply it; otherwise
   record the reason in the audit.
5. **Platform data-root defaults.** Remove the shared `/tmp/lintap-data`
   defaults that shadow `Env.cs`. An unconfigured deployment must use
   `%ProgramData%\Wintap` on Windows,
   `/Library/Application Support/Mactap` on macOS, and `/var/lib/lintap` on
   Linux/Unix while retaining all explicit override mechanisms.

Hard constraints for the whole feature, repeated verbatim:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

Additional constraints:

- Do not clean up unrelated code encountered during the sweep; flag unrelated
  findings in the audit instead.
- The Developer must not edit any wiki path or any instruction document.
- Do not update the implementation-plan checklist; close-out handles that.

## Dependencies

- `developer_docs/instructions/wpc-07-boot-etl-coverage.md`
- `developer_docs/audits/wpc-07-boot-etl-coverage.md`
- Current source files to read before editing:
  - `wintap/platform/windows/sensor/etw/helpers/BootProcessTraceHelper.cs`
  - `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
  - `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
  - `wintap/core/infrastructure/EventChannel.cs`
  - `wintap/core/infrastructure/ProcessResolver.cs`
  - `wintap/core/shared/Env.cs`
  - `wintap/core/shared/ConfigManager.cs`
  - `wintap/core/etl/ETLConfig.json`
  - `wintap/Wintap.Common.props`
  - `platform/windows/WintapCoreSvcMgr/BackupDatabaseManager.cs` if smoke logs
    point at the recovery/process-tree backup insert path
  - `tests/Wintap.Tests/WindowsProcessSensorTests.cs`

Important prior behavior:

- wpc-07 currently calls
  `BootProcessTraceHelper.StopOwnedBootSessionDisarmAndGetReplayPath(...)` only
  when `Properties.Settings.Default.EnableBootProcessTrace` is true in
  `WindowsSubscriptionManager.Start()`.
- wpc-07 currently calls `BootProcessTraceHelper.ArmForNextBoot(...)` only when
  the setting is true in `WindowsSubscriptionManager.Stop()`.
- `Properties.Settings.Default` is loaded at process start. Therefore an
  enable-then-restart can leave the stopping instance holding the old `False`
  value and skip shutdown arming. The first boot after enabling is then not
  covered.

## Implementation Notes

### 1. Boot-trace lifecycle semantics

Implement the following lifecycle. This intentionally narrows the wpc-07
"off means inert" contract: **off still means no arming and no replay, but
startup may perform Wintap-owned cleanup so disabling the feature is safe.**

If implementation reveals that this cleanup cannot be made Wintap-owned and
safe, stop and raise it to the Architect before changing broader semantics.

Required startup behavior in `WindowsSubscriptionManager.Start()`:

1. At the top of startup, before `new WindowsProcessSensor()` and before any
   `KernelSession` / `KernelSource` / `KernelParser` singleton can be
   materialized, always run an owned Global Logger inspection/cleanup helper.
2. If an active `"NT Kernel Logger"` session is present and
   `BootProcessTraceHelper.IsOwnedBootSession(session.FileName,
   BootTraceEtlPath)` is true, stop it.
3. Disarm Wintap-owned Global Logger registry state at startup, including when
   the setting is now false. At minimum, set `Start=0` for the Wintap-owned
   `GlobalLogger` state. Do not stop or disarm a foreign active kernel session.
4. Return a replay path only when `EnableBootProcessTrace` is true and a Wintap
   boot ETL is available. When the setting is false, cleanup may run but replay
   must not run.
5. After existing detect/stop/disarm/replay-path selection, if
   `EnableBootProcessTrace` is true, arm for the next boot during startup. This
   is the key fix: an enabled machine should be armed for the next boot without
   waiting for a clean Wintap stop, and should remain resilient to crash or
   power loss.

Required shutdown behavior:

- Do not rely on shutdown as the only arming point. Keeping a shutdown re-arm
  when the setting is true is acceptable as a belt-and-suspenders measure, but
  startup arming is required.
- When the setting is false, shutdown must not arm.

Keep replay ordering unchanged after this lifecycle work: construct
`WindowsProcessSensor`, run `InitializeSnapshotRefresh()`, start the sensor/live
subscription, then replay the boot ETL if a replay path was returned.

Make the lifecycle decision unit-testable without registry writes or ETW
sessions. The registry/session operations are the seam boundary; pure decision
logic behind them gets tests.

Suggested testable shape (names are not mandatory):

- a small decision method that accepts `settingEnabled`, `ownedActiveSession`,
  `bootEtlExists`, and current registry ownership/file state and returns whether
  to stop session, disarm, arm, and replay;
- `BootProcessTraceHelper` methods that perform registry/session side effects
  remain thin wrappers and are manually smoked.

### 2. Missing-parent warnings

Start from the actual smoke logs and identify the event types/PIDs that produced
`Could not resolve parent process` warnings.

Current code path to inspect: `EventChannel.Send(...)` resolves parent context
for process events after the sensor emits them. If parent lookup misses, it logs
`Could not resolve parent process (childPid=..., parentPid=...)`, optionally
falls back through `_processResolver.GetPidHash(...)`, and finally assigns
`UnknownPidHash.Value`.

Required disposition:

- If logs show ordering or timing that should now be covered by snapshot/boot
  replay, fix the resolution gap with the smallest local change and add a
  `wpc-09` unit test.
- If logs show expected races, parent-exited-before-observed cases, PID 0/4
  boundary behavior, or otherwise genuinely unresolvable parents, keep the
  unknown-parent sentinel behavior but improve the warning so operators can tell
  it is expected best-effort attribution. It must remain rate-limited; do not
  introduce log floods.
- Do not add a sensor-owned PID instance map.

### 3. Process-name / DuckDB command-line parsing

Start from the smoke logs. Determine whether the process-name parsing errors are
the same root as the earlier DuckDB `unterminated quoted string` command-line
errors.

Current likely path: `ProcessResolver.RegisterProcess(...)` builds SQL strings
for process Start/Refresh rows using interpolation and `EscapeSql(...)`; the
process command line lands in `command_line`. `BackupDatabaseManager` has a
separate DuckDB insert path for process-tree backup/recovery; touch it only if
the logs show the errors come from that path.

If DuckDB string parsing is confirmed:

- Prefer `DuckDBCommand` parameters for process string values (`process_name`,
  `image_path`, `command_line`, `user_name`, hashes) instead of hand-built SQL
  string literals. If DuckDB.NET parameter support is not viable in this code
  path, strengthen escaping with tests and document the reason in the audit.
- Add hostile command-line tests tagged `wpc-09`, including at least:
  - an unterminated quote, e.g. `cmd.exe /c "unterminated`;
  - embedded single quotes;
  - embedded double quotes;
  - backslashes and spaces typical of Windows paths.
- The fix must preserve exact command-line content in the stored row.

Do not sanitize, truncate, or drop command lines to make inserts succeed.

### 4. QA-counter logger tag

Current `WindowsProcessSensor` default logger is a constructor-created lambda,
which wpc-07 audit says left QA-counter lines attributed to
`[WindowsProcessSensor..ctor]`. Check the current code and smoke logs.

- If still present, prefer a tiny named-method wrapper or direct call pattern so
  attribution is no longer the constructor.
- Do not change the `Windows process QA counters:` payload or counter names.
- If not fixed because the change is not trivial, note that in the audit.

### 5. Platform data-root defaults

The shipped `wintap/core/etl/ETLConfig.json` currently contains
`"DataRoot": "/tmp/lintap-data"`, and `ConfigRoot.DataRoot` has the same
initializer. `Wintap.Common.props` copies that shared JSON into every platform
output. On Windows, the slash-rooted value resolves to `C:\tmp\lintap-data` and
prevents `Env.FileDataRoot` from reaching its existing platform branch.

Implement the settled behavior:

1. Remove the top-level `DataRoot` property from the shipped shared
   `ETLConfig.json`. Do not replace it with another platform-specific, empty,
   tokenized, or environment-variable value.
2. Retain the public `ConfigRoot.DataRoot` property for JSON binding, but remove
   its `/tmp/lintap-data` initializer so a new `ConfigRoot` has a null/empty
   data root.
3. Preserve precedence exactly:
   - `Env.SetDataRoot(...)` programmatic override;
   - `ConfigManager.GetValue<string>("DataRoot")`, including
     `WINTAP_DATA_ROOT` before an explicit JSON `DataRoot`;
   - the `Env.cs` platform default.
4. Preserve the existing platform defaults:
   - Windows: `%ProgramData%\Wintap`;
   - macOS: `/Library/Application Support/Mactap`;
   - Linux and other Unix: `/var/lib/lintap`.
5. Treat null, empty, and whitespace-only override/configuration values as
   absent. Do not otherwise normalize, rewrite, create, or validate an
   explicitly supplied path.
6. Extract a small internal pure decision seam from `Env.FileDataRoot` so tests
   can supply the effective programmatic override, effective configured value,
   target platform, and Windows CommonApplicationData path (or equivalent
   primitive inputs). `Env.FileDataRoot` must call that seam using the existing
   live inputs. Do not mutate process environment variables or shared static
   override state in tests.
7. Confirm `Wintap.Common.props` continues to copy the now platform-neutral
   shared JSON; no project-file change should be necessary.

Do not migrate or delete data from an old implicit `/tmp/lintap-data` or
`C:\tmp\lintap-data` store. Do not change the explicit Linux development data
root selected by the Makefile; that is a configured development override, not
the unconfigured deployment default.

## Acceptance Criteria

1. Enabling `EnableBootProcessTrace=True` and restarting Wintap arms the Global
   Logger for the next boot without manual registry setup.
2. Startup always performs Wintap-owned Global Logger cleanup/disarm before any
   Wintap kernel session singleton materializes.
3. With the setting false, Wintap does not arm and does not replay boot ETLs, but
   it does clean up Wintap-owned armed/session state left from prior enabled
   runs.
4. With the setting true, startup arms for the next boot after cleanup and still
   replays an available owned boot ETL after snapshot and live subscription.
5. Pure lifecycle decision logic has `wpc-09` unit coverage; registry/session
   writes remain manual-smoke territory.
6. Missing-parent warnings are either fixed or annotated/rate-limited as expected
   behavior, with the disposition explained in the audit.
7. Confirmed DuckDB command-line/parser failures are fixed in the insert path,
   with hostile command-line tests preserving exact data.
8. QA-counter logger attribution is fixed if still trivially broken, or the
   audit records why it was left unchanged.
9. With no explicit override, runtime data resolves to `%ProgramData%\Wintap`
   on Windows, `/Library/Application Support/Mactap` on macOS, and
   `/var/lib/lintap` on Linux/Unix.
10. `Env.SetDataRoot`, `WINTAP_DATA_ROOT`, and explicit JSON `DataRoot`
    overrides retain their settled precedence; null/empty/whitespace values do
    not shadow platform defaults.
11. Pure `wpc-09` tests cover override precedence and every platform default
    without environment mutation or host-OS dependence; `new ConfigRoot()` has
    no non-empty `DataRoot` default and the shipped JSON has no `DataRoot`.
12. No schema changes, no PidHash changes, TraceEvent remains 3.1.23, and no new
   NuGet dependencies are added.
13. Audit artifact is filed at
    `developer_docs/audits/wpc-09-bug-sweep.md` and includes manual-smoke
    results supplied by the Architect.

## Tests

Add new tests tagged:

```csharp
[Trait("Category", "wpc-09")]
```

Required unit tests:

- Boot lifecycle decision tests for enable/restart, enabled steady state,
  disable-after-arm cleanup, foreign-session preservation, and no replay when
  disabled.
- Hostile command-line persistence/escaping tests if the DuckDB path is changed.
- Missing-parent warning tests if the fix changes pure behavior that can be
  tested without live ETW/admin privileges.
- Data-root decision tests for programmatic/configured precedence; null, empty,
  and whitespace fallthrough; Windows, macOS, and Linux/Unix defaults; and an
  empty default `ConfigRoot.DataRoot`.

Do not add registry-writing, live ETW-session, admin-only, reboot-dependent, or
sleep/timing-dependent unit tests.

## Test Command

Run and document these gates from the repo root:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-09"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"
```

Known issue: repo-root solution Release build has the pre-existing
`Wintap-Workbench` `MSB4249` failure; the project-scoped Release build above is
the pre-approved fallback.

## Manual Smoke Procedure

The Developer writes the procedure into the audit; the Architect executes it
elevated and pastes results into `developer_docs/audits/wpc-09-bug-sweep.md`.

1. Publish/deploy the wpc-09 build locally.
2. Ensure Wintap is stopped. In `Wintap.dll.config`, set
   `EnableBootProcessTrace` to `True`.
3. Start/restart Wintap elevated.
4. Verify Global Logger registry values exist without manual registry setup:
   - key: `HKLM\SYSTEM\CurrentControlSet\Control\WMI\GlobalLogger`
   - `Start` is `1` after startup arming;
   - `FileName` points to `%ProgramData%\Wintap\boot-process-trace.etl`;
   - `EnableKernelFlags` begins with process flag `01 00 00 00` and is padded.
5. Reboot.
6. After Wintap starts, verify logs show owned boot-session stop/disarm and boot
   ETL replay, and verify boot-time processes appear in telemetry.
7. Stop Wintap, set `EnableBootProcessTrace` to `False`, and start/restart
   Wintap elevated.
8. Verify disabling cleans up:
   - `GlobalLogger` `Start` is `0`;
   - no Wintap-owned kernel boot session persists;
   - Wintap does not replay the ETL while disabled;
   - no manual registry cleanup is required.
9. For the data-root check, ensure there is no explicit `WINTAP_DATA_ROOT` and
   no JSON `DataRoot`, then verify the Windows log reports
   `%ProgramData%\Wintap` as the effective root and that DuckDB/log/Parquet
   runtime files are created beneath it rather than beneath `C:\tmp`.

Pass criteria: all checks above pass, Wintap remains stable, and logs contain no
new untriaged process-path errors.

## Out of Scope

- Do not implement wpc-08 or any formal validation harness.
- Do not renumber units.
- Do not fix SensSensor null-value load failure.
- Do not fix missing `SignedS3UrlAdapter` deployment/config gap.
- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change `ProcessHash` or the `PidHash` formula.
- Do not upgrade TraceEvent or add any NuGet package.
- Do not add a sensor-owned PID instance map.
- Do not clean up unrelated warnings, analyzers, formatting, or dead code.
- Do not edit any wiki path.
- Do not edit `developer_docs/instructions/`.
- Do not update the implementation plan checklist.
- Do not migrate, copy, merge, or delete an existing implicit
  `/tmp/lintap-data` or `C:\tmp\lintap-data` store.
- Do not change the explicit Linux development data-root override in the
  Makefile.
- Do not add platform-specific `ETLConfig.json` copies or remove explicit
  programmatic, environment, or JSON `DataRoot` support.
- Do not add path ACL, installer, Code42/AV/EDR exclusion, service-recovery, or
  DuckDB lock-retry behavior.
