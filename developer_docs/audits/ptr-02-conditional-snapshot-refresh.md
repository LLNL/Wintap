# ptr-02 Conditional Snapshot Refresh Audit

**Date:** 2026-08-23
**Branch:** `process-tree-recovery`
**Instruction:** `developer_docs/instructions/ptr-02-conditional-snapshot-refresh.md`
**Status:** Complete

## Scope

Implemented only ptr-02. Windows process snapshot initialization now retains the
resolver tree when the live PID 4 hash matches an open System row, and otherwise
clears and rebuilds conservatively. The PID 4 probe is read once and shared by
the gate and snapshot seed construction. The constructor reads
`WINTAP_BOOT_SESSION_RECOVERY_ENABLED` once with a default of true and accepts
the approved optional kill-switch and open-row seams.

Added the specified boot-trace warning immediately after snapshot initialization
in `WindowsSubscriptionManager.Start()`. No manager seam was added: the exact
three-part condition is directly present at the production call site, avoiding
production widening beyond the instruction's one-statement manager change.

Added seam-driven sensor tests and a temp-file DuckDB resolver test for the
`COALESCE` idempotency backstop. All added test cases are tagged `ptr-02`.

## Files Created

- `developer_docs/audits/ptr-02-conditional-snapshot-refresh.md`

## Files Modified

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
- `tests/Wintap.Tests/SystemIdentityProbeTests.cs`

## Verification Commands And Outputs

### Exact Required Repository-Root Commands

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-02"
```

Both commands were run from `C:\PUBLIC\Wintap` in the required order and hit
the documented solution-level website-project issue. Build exited 1:

```text
C:\PUBLIC\Wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\Wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.18
```

The root test command exited 1:

```text
C:\PUBLIC\Wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
```

### Documented Fallbacks

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-02" --logger "console;verbosity=detailed"
```

Fallback build exited 0:

```text
WintapAPI -> C:\PUBLIC\Wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
Wintap -> C:\PUBLIC\Wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.
    16 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.37
```

The 16 warnings are existing restore advisories: NU1603 for TaskScheduler 2.11.1
resolving to 2.12.0, NU1903 for Microsoft.OpenApi 2.3.1, and NU1701 framework
compatibility warnings for legacy WebApi/Owin packages.

Fallback ptr-02 test exited 0 with all named cases passing:

```text
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_LookupReceivesExactSystemHash
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ReadsProbeOnceAndSharesHashWithSystemSeed
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_KillSwitchSkipsProbeAndLookupAndClears
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ThrowingBootSessionSeamDegradesSafely(probeThrows: False)
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ThrowingBootSessionSeamDegradesSafely(probeThrows: True)
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_OpenSystemRowKeepsTreeAndEmitsRefreshes
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_OuterFailureMarksRefreshAsRebuilt
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ProbeUnavailableRebuildsWithoutLookupAndUsesWmiSeeds
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_AbsentSystemRowClearsBeforeEmitAndLogsDegradation
Passed Wintap.Tests.SystemIdentityProbeTests.UpsertProcessStart_PreservesExistingExitTimeForSamePidHash

Test Run Successful.
Total tests: 10
     Passed: 10
 Total time: 1.4339 Seconds
```

### Additional Regression Verification

These project-scoped commands were additional verification, not the exact
required commands:

```powershell
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~ptr" --logger "console;verbosity=detailed"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc" --logger "console;verbosity=detailed"
```

Results:

```text
Category~ptr: Test Run Successful. Total tests: 24, Passed: 24, Failed: 0. Total time: 1.8723 Seconds.
Category~wpc: Test Run Successful. Total tests: 64, Passed: 64, Failed: 0. Total time: 1.5023 Seconds.
```

The detailed ptr run named all 10 ptr-02 cases above, all 6 ptr-04 cases, and
all 8 ptr-01 cases as passed. The detailed wpc run named all 64 existing cases
as passed, including the snapshot clear/order/dedup tests and resolver upsert
tests affected by this shared behavior.

## Behavioral And Compatibility Notes

- An exact live `genPidHash(4, livePid4StartFileTimeUtc)` open-row match keeps
  the process tree; an absent row, closed row, null probe, throwing probe or
  lookup, disabled setting, or outer refresh failure degrades conservatively.
- The keep path does not clear the process database. Rebuild clears exactly
  once before the first refresh emission. There is no identity write or clear.
- `LastRefreshRebuiltFromSnapshot` is false only for a successful keep decision,
  true for every rebuild decision, and true in the outer failure catch.
- Snapshot ordering, deduplication, synthetic seed fields, and ptr-04 fallback
  behavior remain unchanged except that `BuildSnapshotBatch` receives the one
  hoisted probe result.
- The manager warning is emitted only when boot tracing is enabled, replay path
  is null, and the refresh reports a rebuild. It is intentionally suppressed on
  a same-boot keep path.
- Both operator grep contracts are present verbatim: `retained across restart
  (boot session match)` and `rebuilt from snapshot, lineage degraded`.
- **Upgrade boundary (self-healing):** a same-boot tree seeded before ptr-04
  carries a WMI-hash System row. The first post-upgrade start therefore misses
  the real-start hash, logs `no open System row for this boot session`, and does
  one extra clear-and-rebuild. The real-start System row then satisfies all
  subsequent same-boot lookups. This is expected and requires no action.
- The same conservative self-healing rebuild occurs if the System row was
  closed or repaired during a prior run.
- No schema, PidHash formula, dependency, boot-trace lifecycle, Linux, or macOS
  behavior was changed.

## Manual Check

Not performed. The restart-then-reboot service check requires an elevated
Windows service environment. Automated seam tests cover the gate, logs, seed
anchor, failure behavior, and state property, but do not claim the operational
restart/reboot check.

## Deviations And Follow-Ups

- The exact root commands could not pass because of documented `MSB4249`; all
  documented project-scoped fallbacks passed.
- Detailed console logging was added to the fallback and additional test runs
  to capture test names and statuses.
- No pure manager seam was added because the instruction permits considering
  one only if necessary; the exact condition and warning are directly visible
  at the sole approved manager edit and production scope was kept minimal.
- The elevated manual check remains an operational follow-up.

## Scope And Grep Verification

- `git diff --check` passed with no whitespace errors; Git reported only
  existing LF-to-CRLF working-copy notices.
- Grep found one config-key read with default true, one probe invocation in the
  refresh flow, one open-row lookup, both required log phrases, both property
  assignments, and the exact manager warning.
- No ptr-02 changes were made to instructions/wiki, `ProcessResolver.cs`,
  `IProcessResolver.cs`, `EventChannel.cs`, `BootProcessTraceHelper`, schemas,
  dependencies, or non-Windows code.

## Unrelated Worktree State

The branch already contained ptr-01 and ptr-04 implementation changes and an
untracked diagnostics directory before ptr-02. They were not reverted. The
following pre-existing items remain visible in status and are not ptr-02 edits:

```text
M wintap/core/infrastructure/EventChannel.cs
M wintap/core/infrastructure/IProcessResolver.cs
M wintap/core/infrastructure/ProcessResolver.cs
?? diagnostics/sid-extraction-test/
```

`tests/Wintap.Tests/SystemIdentityProbeTests.cs` was a pre-existing untracked
ptr-01 file; ptr-02 added only the required tagged idempotency test to it.
`WindowsProcessSensor.cs` and `WindowsProcessSensorTests.cs` already contained
the uncommitted ptr-04 implementation; ptr-02 was layered onto those changes.
