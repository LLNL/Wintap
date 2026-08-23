# ptr-04 System Seed Real Start Audit

**Date:** 2026-08-23
**Branch:** `process-tree-recovery`
**Instruction:** `developer_docs/instructions/ptr-04-system-seed-real-start.md`

## Scope

Implemented only ptr-04: synthetic Windows process seeds now use the real PID 4
kernel start FileTime when available, fall back safely to the WMI boot time with
the specified warning, and reject replayed PID 4/PID <= 0 rundown identities.
Added seam-driven ptr-04 tests without live ETW, elevation, or host process-list
dependencies. ptr-02 was not started.

## Files Changed

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
- `developer_docs/audits/ptr-04-system-seed-real-start.md`

## Implementation

- Added optional `Func<long?> probeSystemStartFileTimeUtc`, defaulting to
  `ProcessResolver.TryGetSystemStartFileTimeUtc`.
- Wrapped probe invocation so exceptions become fallback rather than escaping.
- Anchored PID 4, PID 0, and PID -1 synthetic seed create times and hashes to
  the probed PID 4 FileTime.
- Avoided the WMI/COM boot-time call on the successful probe path.
- Preserved WMI behavior on null/exception and emitted the specified
  `SENSOR HEALTH` warning at `LogLevel.Warn`.
- Added the replay guard before dedup/emission/counting for PID 4 and PID <= 0.
- Left `BuildParentPidHashes`, seed fields, schemas, hash formula, dependencies,
  replay tolerance, and non-Windows code unchanged.

## Verification Commands

Required root commands were run exactly in this order:

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-04"
dotnet test --filter "Category~ptr"
dotnet test --filter "Category~wpc"
```

All four stopped at the documented solution-level known issue:

```text
C:\PUBLIC\Wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
```

The documented fallback commands were then run in order. As required, detailed
console logging was added to each test command:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-04" --logger "console;verbosity=detailed"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~ptr" --logger "console;verbosity=detailed"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc" --logger "console;verbosity=detailed"
```

Fallback build result:

```text
Build succeeded.
    16 Warning(s)
    0 Error(s)
Time Elapsed 00:00:10.09
```

Warnings were existing restore advisories: NU1603 (`TaskScheduler` 2.12.0 used
for requested >= 2.11.1), NU1903 (`Microsoft.OpenApi` 2.3.1 advisory), and
NU1701 framework compatibility warnings for legacy WebApi/Owin packages.

An initial fallback ptr-04 test invocation exposed a test-only missing
`gov.llnl.wintap` namespace import for `LogLevel` (`CS0246`). The import was
added and the same command was rerun successfully. This was the only
implementation-time deviation and did not alter production behavior.

## Detailed Test Output

### `Category=ptr-04`

```text
Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_SuppressesSyntheticSeedIdentitiesWithoutCounting
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_RealSystemStartSkipsWmiBootTime
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ProbeMissFallsBackToWmiBootTimeAndWarns
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ProbeExceptionFallsBackWithoutEscaping
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SystemSeedHashMatchesReconcileLiveIdentity
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SeedsSyntheticProcessesFromRealSystemStart

Test Run Successful.
Total tests: 6
     Passed: 6
 Total time: 1.5687 Seconds
```

### `Category~ptr`

```text
Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_SuppressesSyntheticSeedIdentitiesWithoutCounting
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_RealSystemStartSkipsWmiBootTime
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ProbeMissFallsBackToWmiBootTimeAndWarns
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ProbeExceptionFallsBackWithoutEscaping
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SystemSeedHashMatchesReconcileLiveIdentity
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SeedsSyntheticProcessesFromRealSystemStart
Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseForMissingEmptyOrNullHash(pidHash: "")
Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseForMissingEmptyOrNullHash(pidHash: "missing-process")
Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseForMissingEmptyOrNullHash(pidHash: null)
Passed Wintap.Tests.SystemIdentityProbeTests.EnsureEventStoreTables_CreatesRequiredTablesAndIsIdempotent
Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseWithoutThrowingForQuoteContainingHash
Passed Wintap.Tests.SystemIdentityProbeTests.TryGetWindowsProcStartFileTimeUtc_MatchesCurrentProcessStartTime
Passed Wintap.Tests.SystemIdentityProbeTests.TryGetWindowsProcStartFileTimeUtc_ReturnsNullForNegativePid
Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsTrueForUpsertedOpenRowAndFalseAfterExitUpdate

Test Run Successful.
Total tests: 14
     Passed: 14
 Total time: 1.7606 Seconds
```

### `Category~wpc`

```text
Passed Wintap.Tests.RuntimeDataRootTests.ConfigRoot_HasNoDataRootDefault
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_UsesExpectedPlatformDefault(platformName: "LINUX", expected: "/var/lib/lintap")
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_UsesExpectedPlatformDefault(platformName: "OSX", expected: "/Library/Application Support/Mactap")
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_UsesExpectedPlatformDefault(platformName: "WINDOWS", expected: "C:\\ProgramData\\Wintap")
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_EmptyValuesUsePlatformDefault(overridden: "", configured: "")
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_EmptyValuesUsePlatformDefault(overridden: null, configured: null)
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_EmptyValuesUsePlatformDefault(overridden: "   ", configured: "\t")
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 4)
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 4)
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 8)
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 8)
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero
Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength
Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord
Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsResolverMissAndIncrementsBootReplayCount
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: True, ownedActiveSession: False, bootEtlExists: True, registryOwned: True, expectedStop: False, expectedDisarm: False, expectedArm: True, expectedReplay: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: False, ownedActiveSession: False, bootEtlExists: False, registryOwned: False, expectedStop: False, expectedDisarm: False, expectedArm: True, expectedReplay: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: False, activeSessionPresent: True, ownedActiveSession: True, bootEtlExists: True, registryOwned: True, expectedStop: True, expectedDisarm: True, expectedArm: False, expectedReplay: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: False, activeSessionPresent: False, ownedActiveSession: False, bootEtlExists: True, registryOwned: True, expectedStop: False, expectedDisarm: True, expectedArm: False, expectedReplay: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: True, ownedActiveSession: True, bootEtlExists: True, registryOwned: True, expectedStop: True, expectedDisarm: True, expectedArm: True, expectedReplay: True)
Passed Wintap.Tests.RuntimeDataRootTests.ShippedEtlConfig_HasNoDataRootOverride
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_ConfiguredValueWinsPlatformDefault
Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_ProgrammaticOverrideWins
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotSuppressWhenResolverMissesOrOutsideTolerance
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "c:/programdata/wintap/./boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: True)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "\0", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: null, configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "   ", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False)
Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "C:\\ProgramData\\Other\\boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False)
Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverLookupResult_ForPidReuse
Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedCommandLineFallbackMatrix
Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_ComputesPidHashFromCanonicalEtwStartTime
Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_EnrichmentExceptionsDoNotPreventEmission
Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricExpiry_IncrementsManifestMetricMissesAndDoesNotBlockCallback
Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationWindowHit_MergesMetricsForBothOrderingCases
Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelation_UsesNearestMetricsAndResolverTimestampForPidReuse
Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsPidReuseOutsideToleranceWithReplayedTimestamp
Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_UsesEnrichedFieldsAndEtwStartTimestampPidHash
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ClearsProcessDbBeforeFirstRefreshEmit
Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_BoundsSidAccountCacheAndEvictsDeterministically
Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_ReportsExpectedValuesAfterSimulatedActivity
Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterLineIncludesBootReplayCount
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_EmitsOldestFirstWithPidTieBreaker
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotChooseFutureParentInstance
Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_CachesExtractedSidAccountLookup
Passed Wintap.Tests.WindowsProcessSensorTests.CanonicalizeCreateTimeUtc_NormalizesEtwTimestampToUtc
Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "powershell.exe -Command \"Write-Output 'quoted value'\"")
Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "C:\\Program Files\\Example App\\tool.exe /path C:\\Temp\\")
Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "cmd.exe /c \"unterminated")
Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "tool.exe --value=\"double quoted\"")
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ChoosesLatestParentInstanceBeforeChild
Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedPathFallbackMatrix
Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedUserFallbackMatrix
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_IncludesSyntheticSystemProcessSeeds
Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationMiss_EmitsDefaultsAfterExpiry
Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_UsesExpectedNamesAndFormatExactlyOnce
Passed Wintap.Tests.WindowsProcessSensorTests.Stop_LogsFinalQaCountersWithSameSnapshotFormat
Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_SuppressesDuplicateWithinToleranceWithoutIncrementingBootReplayCount
Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull
Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SuppressesDuplicateRefreshWithinTolerance

Test Run Successful.
Total tests: 64
     Passed: 64
 Total time: 1.5411 Seconds
```

The detailed runner reported every test and theory case shown above separately,
accounting for the exact 64-test total.

## Compatibility Notes

- **System pid_hash values change relative to prior datasets.** Previously
  `GenPidHash(4, wmiBootTime)`; now `GenPidHash(4, truePid4Start)`. The Idle
  (PID 0) and Unknown (PID -1) seed hashes and all three seeds'
  `parent_pid_hash` anchors change the same way. Any downstream join of the
  System root or top-level `parent_pid_hash` values across datasets recorded
  before/after this unit will not line up. Within a single dataset the tree
  remains internally consistent; no migration is performed.
- Datasets also become **stable across same-boot restarts** for the System root
  (that is the point of the fix), so post-ptr-04 datasets are more
  self-consistent than pre-ptr-04 ones.
- The fallback path (PID 4 unreadable) reproduces today's WMI behavior,
  including its drift defects. This is accepted degradation and is flagged by
  the SENSOR HEALTH warning.

## Manual Check

Not performed. The elevated service/event-store check across two maintenance
sweeps requires a genuine elevated host run and more than ten minutes; automated
seam tests cover the unit behavior but do not claim this operational check.

## Deviations

- Root verification could not pass because of documented `MSB4249`; all
  documented project-scoped fallbacks were run.
- Test fallback commands include the requested detailed console logger.
- One initial project test compile found and prompted the missing test namespace
  import, after which all required fallback commands passed.
- No instruction, wiki, schema, dependency, ProcessResolver, Linux, macOS, or
  ptr-02 changes were made for ptr-04.

## Unrelated Worktree State

The following pre-existing ptr-01/user changes were present before ptr-04 and
were not modified or reverted as part of this unit:

```text
 M wintap/core/infrastructure/EventChannel.cs
 M wintap/core/infrastructure/IProcessResolver.cs
 M wintap/core/infrastructure/ProcessResolver.cs
?? diagnostics/sid-extraction-test/
?? tests/Wintap.Tests/SystemIdentityProbeTests.cs
```

Final scope verification used `git status --short`, scoped `git diff`, and
`git diff --check`. The only ptr-04 changes are the three files listed above;
`git diff --check` reported no whitespace errors (only Git LF-to-CRLF working
copy notices).
