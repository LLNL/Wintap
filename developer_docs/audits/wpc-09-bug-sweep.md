# Audit Artifact: wpc-09 Minor Bug Sweep

**Date:** 2026-08-18
**Instruction:** `developer_docs/instructions/wpc-09-bug-sweep.md`
**Status:** Complete

## Scope Implemented
- Added pure boot-trace lifecycle decisions and startup-owned cleanup/re-arm behavior, including disabled-setting cleanup and foreign-session preservation.
- Parameterized DuckDB Start/Refresh process inserts so command lines and other string telemetry are stored exactly without SQL parser failures.
- Annotated the existing once-per-parent missing-parent warning as expected best-effort attribution while retaining the unknown-parent sentinel.
- Replaced the process sensor's constructor lambda logger with a named logger method to correct QA-counter caller attribution.
- Added `wpc-09` lifecycle and hostile command-line persistence tests.
- Restored platform-owned unconfigured data roots by removing the shared
  `/tmp/lintap-data` defaults while preserving programmatic, environment, and
  explicit JSON override precedence.
- Added pure `wpc-09` coverage for Windows, macOS, Linux/Unix, precedence,
  empty-value fallthrough, and platform-neutral shipped configuration.

## Files Created
- `tests/Wintap.Tests/ProcessResolverTests.cs`
- `tests/Wintap.Tests/RuntimeDataRootTests.cs`
- `developer_docs/audits/wpc-09-bug-sweep.md`

## Files Modified
- `wintap/platform/windows/sensor/etw/helpers/BootProcessTraceHelper.cs`
- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `wintap/core/infrastructure/EventChannel.cs`
- `wintap/core/infrastructure/ProcessResolver.cs`
- `wintap/core/shared/Env.cs`
- `wintap/core/shared/ConfigManager.cs`
- `wintap/core/etl/ETLConfig.json`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`

## Tests Run
- `dotnet build "wintap\Wintap.csproj" -c Release`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-09"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --logger "console;verbosity=detailed"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-09" --no-build --logger "console;verbosity=detailed"`

## Test Results
```text
> dotnet build "wintap\Wintap.csproj" -c Release
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.
    708 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.55

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-09"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 82 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54, Duration: 91 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    55, Skipped:     0, Total:    55, Duration: 86 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --logger "console;verbosity=detailed"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.09]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.09]   Starting:    Wintap.Tests
  Passed Wintap.Tests.WintapMessageTests.Constructor_SetsMessageTypeAndPid [8 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 4) [15 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 4) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord [19 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsResolverMissAndIncrementsBootReplayCount [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotSuppressWhenResolverMissesOrOutsideTolerance [4 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "c:/programdata/wintap/./boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: True) [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "\0", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: null, configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "   ", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "C:\\ProgramData\\Other\\boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverLookupResult_ForPidReuse [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: False, ownedActiveSession: False, bootEtlExists: False, registryOwned: False, expectedStop: False, expectedDisarm: False, expectedArm: True, expectedReplay: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: True, ownedActiveSession: False, bootEtlExists: True, registryOwned: True, expectedStop: False, expectedDisarm: False, expectedArm: True, expectedReplay: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: False, activeSessionPresent: True, ownedActiveSession: True, bootEtlExists: True, registryOwned: True, expectedStop: True, expectedDisarm: True, expectedArm: False, expectedReplay: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: False, activeSessionPresent: False, ownedActiveSession: False, bootEtlExists: True, registryOwned: True, expectedStop: False, expectedDisarm: True, expectedArm: False, expectedReplay: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: True, ownedActiveSession: True, bootEtlExists: True, registryOwned: True, expectedStop: True, expectedDisarm: True, expectedArm: True, expectedReplay: True) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedCommandLineFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_ComputesPidHashFromCanonicalEtwStartTime [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_EnrichmentExceptionsDoNotPreventEmission [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricExpiry_IncrementsManifestMetricMissesAndDoesNotBlockCallback [2 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationWindowHit_MergesMetricsForBothOrderingCases [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelation_UsesNearestMetricsAndResolverTimestampForPidReuse [2 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsPidReuseOutsideToleranceWithReplayedTimestamp [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_UsesEnrichedFieldsAndEtwStartTimestampPidHash [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ClearsProcessDbBeforeFirstRefreshEmit [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_BoundsSidAccountCacheAndEvictsDeterministically [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_ReportsExpectedValuesAfterSimulatedActivity [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterLineIncludesBootReplayCount [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_EmitsOldestFirstWithPidTieBreaker [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotChooseFutureParentInstance [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_CachesExtractedSidAccountLookup [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.CanonicalizeCreateTimeUtc_NormalizesEtwTimestampToUtc [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ChoosesLatestParentInstanceBeforeChild [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedPathFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedUserFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_IncludesSyntheticSystemProcessSeeds [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationMiss_EmitsDefaultsAfterExpiry [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_UsesExpectedNamesAndFormatExactlyOnce [< 1 ms]
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "powershell.exe -Command \"Write-Output 'quoted val"···) [48 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.Stop_LogsFinalQaCountersWithSameSnapshotFormat [17 ms]
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "C:\\Program Files\\Example App\\tool.exe /path C:\"···) [13 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_SuppressesDuplicateWithinToleranceWithoutIncrementingBootReplayCount [< 1 ms]
[xUnit.net 00:00:00.20]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SuppressesDuplicateRefreshWithinTolerance [< 1 ms]
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "cmd.exe /c \"unterminated") [12 ms]
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "tool.exe --value=\"double quoted\"") [12 ms]

Test Run Successful.
Total tests: 55
     Passed: 55
 Total time: 0.5793 Seconds
```

### Final rerun after platform data-root scope extension

```text
> dotnet build "wintap\Wintap.csproj" -c Release
Build succeeded.
    708 Warning(s)
    0 Error(s)
Time Elapsed 00:00:02.21

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-09"
Passed!  - Failed:     0, Passed:    19, Skipped:     0, Total:    19, Duration: 82 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 87 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"
Passed!  - Failed:     0, Passed:    65, Skipped:     0, Total:    65, Duration: 115 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-09" --no-build --logger "console;verbosity=detailed"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.08]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.09]   Starting:    Wintap.Tests
  Passed Wintap.Tests.RuntimeDataRootTests.ConfigRoot_HasNoDataRootDefault [3 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_UsesExpectedPlatformDefault(platformName: "LINUX", expected: "/var/lib/lintap") [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: True, ownedActiveSession: False, bootEtlExists: True, registryOwned: True, expectedStop: False, expectedDisarm: False, expectedArm: True, expectedReplay: False) [5 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_UsesExpectedPlatformDefault(platformName: "OSX", expected: "/Library/Application Support/Mactap") [< 1 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_UsesExpectedPlatformDefault(platformName: "WINDOWS", expected: "C:\\ProgramData\\Wintap") [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: False, ownedActiveSession: False, bootEtlExists: False, registryOwned: False, expectedStop: False, expectedDisarm: False, expectedArm: True, expectedReplay: False) [< 1 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_EmptyValuesUsePlatformDefault(overridden: "", configured: "") [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: False, activeSessionPresent: True, ownedActiveSession: True, bootEtlExists: True, registryOwned: True, expectedStop: True, expectedDisarm: True, expectedArm: False, expectedReplay: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: False, activeSessionPresent: False, ownedActiveSession: False, bootEtlExists: True, registryOwned: True, expectedStop: False, expectedDisarm: True, expectedArm: False, expectedReplay: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceLifecycleDecision_CoversEnableDisableAndForeignSessionSafety(settingEnabled: True, activeSessionPresent: True, ownedActiveSession: True, bootEtlExists: True, registryOwned: True, expectedStop: True, expectedDisarm: True, expectedArm: True, expectedReplay: True) [< 1 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_EmptyValuesUsePlatformDefault(overridden: null, configured: null) [< 1 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_EmptyValuesUsePlatformDefault(overridden: "   ", configured: "\t") [< 1 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ShippedEtlConfig_HasNoDataRootOverride [15 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_ConfiguredValueWinsPlatformDefault [< 1 ms]
  Passed Wintap.Tests.RuntimeDataRootTests.ResolveDataRoot_ProgrammaticOverrideWins [< 1 ms]
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "powershell.exe -Command \"Write-Output 'quoted val"···) [49 ms]
[xUnit.net 00:00:00.20]   Finished:    Wintap.Tests
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "C:\\Program Files\\Example App\\tool.exe /path C:\"···) [14 ms]
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "cmd.exe /c \"unterminated") [12 ms]
  Passed Wintap.Tests.ProcessResolverTests.UpsertProcessStart_PreservesHostileCommandLineExactly(commandLine: "tool.exe --value=\"double quoted\"") [12 ms]

Test Run Successful.
Total tests: 19
     Passed: 19
 Total time: 0.5905 Seconds
```

## Behavioral Notes
- Log triage used `C:\tmp\lintap-data\Logs\Wintap.log`. Startup lines 118-129 showed seven parent misses while the 407-row snapshot was still being emitted, and later misses were sparse; this is consistent with unavailable/exited parents rather than a concrete resolver ordering defect. The warning remains rate-limited once per parent PID and now explains the unknown-parent sentinel.
- Log lines 122-123 confirmed `ProcessResolver.RegisterProcess` was the DuckDB parser-failure path for two `WmiPrvSE.exe` rows. Start/Refresh process strings now use DuckDB parameters and hostile-value tests preserve command lines exactly.
- Log QA-counter lines were attributed to `[WindowsProcessSensor..ctor]`; the default logger now uses the named `LogMessage` method without changing the counter payload.
- Startup computes and executes owned cleanup before constructing `WindowsProcessSensor`. Enabled startup re-arms immediately, including when a foreign active runtime session is preserved; disabled startup cleans owned state without replay. Foreign active sessions are never stopped or disarmed.
- Manual smoke procedure for the Architect: deploy the build elevated; enable `EnableBootProcessTrace`; restart and verify `GlobalLogger` has `Start=1`, the Wintap ETL `FileName`, and a padded process flag; reboot and verify owned stop/disarm plus replay; then disable the setting, restart, and verify `Start=0`, no owned session, and no replay. Manual smoke results have not yet been supplied.
- Preliminary local smoke on 2026-08-18 confirmed the service was running with `EnableBootProcessTrace=True`, but an active foreign `NT Kernel Logger` session with an empty file name caused the initial lifecycle decision to skip arming. The decision was corrected to preserve the foreign runtime session while still arming Wintap for the next boot; all four gates were rerun successfully and the local publish was refreshed. Elevated redeploy/retest remains pending.
- Post-redeploy reboot smoke confirmed `GlobalLogger` was armed with `Start=1`, `FileName=C:\ProgramData\Wintap\boot-process-trace.etl`, and `EnableKernelFlags` beginning `{1, 0, 0, 0...}`. Post-boot Wintap initialization stopped before sensor startup because `C:\Program Files\Code42-AAT\code42-aat.exe` held the DuckDB event-store file open; boot-session stop/disarm/replay validation remains pending after that unrelated external lock is released.
- The Architect reported that retrying Wintap startup after the transient Code42 lock worked and directed wpc-09 to be marked complete.
- With no explicit data-root override, the shared configuration now falls through to `Env.cs`: `%ProgramData%\Wintap` on Windows, `/Library/Application Support/Mactap` on macOS, and `/var/lib/lintap` on Linux/Unix. Existing data is not migrated.
- The Release build emitted 708 pre-existing package, analyzer, and Windows-platform compatibility warnings; it completed with zero errors.

## Deviations From Instruction
- None

## Follow-up Notes
- None
