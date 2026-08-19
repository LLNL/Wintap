# Audit Artifact: wpc-07 Boot ETL Coverage

**Date:** 2026-08-17
**Instruction:** `developer_docs/instructions/wpc-07-boot-etl-coverage.md`
**Status:** Complete

## Scope Implemented
- Added opt-in `EnableBootProcessTrace` setting, default `False`.
- Added Global Logger boot-process trace helper with fixed `%ProgramData%\Wintap\boot-process-trace.etl` path, pure ownership predicate, startup disarm/stop handling, and shutdown re-arm registry writes.
- Wired enabled-only startup handling before `WindowsProcessSensor` construction and enabled-only shutdown re-arm.
- Added synchronous boot ETL file replay after snapshot refresh and live process subscription registration.
- Added resolver-backed boot replay dedup by PID and create-time tolerance, using replayed ETW timestamp as canonical create time and emitting only Start events.
- Added `boot_replay_count` QA counter to snapshot DTO and formatter.
- Added wpc-07 unit tests for ownership predicate, replay dedup tolerance, and QA counter formatting.

## Files Created
- `wintap/platform/windows/sensor/etw/helpers/BootProcessTraceHelper.cs`
- `developer_docs/audits/wpc-07-boot-etl-coverage.md`

## Files Modified
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
- `wintap/Properties/Settings.settings`
- `wintap/Properties/Settings.Designer.cs`
- `wintap/App.config`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`

## Tests Run
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
- `dotnet build "tests\Wintap.Tests\Wintap.Tests.csproj"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-07"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"`
- Supplementary detailed output runs for test-name audit:
  - `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-07" --no-build --logger "console;verbosity=detailed"`
  - `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc" --no-build --logger "console;verbosity=detailed"`
  - `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --logger "console;verbosity=detailed"`

## Test Results
```text
> dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
  Determining projects to restore...
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  All projects are up-to-date for restore.
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.
    16 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.32

> dotnet build "tests\Wintap.Tests\Wintap.Tests.csproj"
  Determining projects to restore...
  All projects are up-to-date for restore.
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Debug\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Debug\net8.0\Wintap.dll
  Wintap.Tests -> C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll

Build succeeded.
    731 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.09

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-07"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 37 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 103 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    46, Skipped:     0, Total:    46, Duration: 58 ms - Wintap.Tests.dll (net8.0)

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-07" --no-build --logger "console;verbosity=detailed"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.09]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.09]   Starting:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsResolverMissAndIncrementsBootReplayCount [16 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "c:/programdata/wintap/./boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: True) [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "\0", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: null, configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "   ", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "C:\\ProgramData\\Other\\boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsPidReuseOutsideToleranceWithReplayedTimestamp [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterLineIncludesBootReplayCount [< 1 ms]
[xUnit.net 00:00:00.15]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_SuppressesDuplicateWithinToleranceWithoutIncrementingBootReplayCount [< 1 ms]

Test Run Successful.
Total tests: 10
     Passed: 10
 Total time: 0.5655 Seconds

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wpc" --no-build --logger "console;verbosity=detailed"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.06]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.09]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.10]   Starting:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 4) [9 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 4) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord [12 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsResolverMissAndIncrementsBootReplayCount [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotSuppressWhenResolverMissesOrOutsideTolerance [4 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "c:/programdata/wintap/./boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: True) [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "\0", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: null, configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "   ", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "C:\\ProgramData\\Other\\boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverLookupResult_ForPidReuse [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedCommandLineFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_ComputesPidHashFromCanonicalEtwStartTime [1 ms]
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
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_EmitsOldestFirstWithPidTieBreaker [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotChooseFutureParentInstance [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_CachesExtractedSidAccountLookup [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.CanonicalizeCreateTimeUtc_NormalizesEtwTimestampToUtc [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ChoosesLatestParentInstanceBeforeChild [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedPathFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedUserFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_IncludesSyntheticSystemProcessSeeds [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationMiss_EmitsDefaultsAfterExpiry [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_UsesExpectedNamesAndFormatExactlyOnce [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.Stop_LogsFinalQaCountersWithSameSnapshotFormat [16 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_SuppressesDuplicateWithinToleranceWithoutIncrementingBootReplayCount [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull [< 1 ms]
[xUnit.net 00:00:00.18]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SuppressesDuplicateRefreshWithinTolerance [< 1 ms]

Test Run Successful.
Total tests: 45
     Passed: 45
 Total time: 0.5679 Seconds

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --logger "console;verbosity=detailed"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.09]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.09]   Starting:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 4) [9 ms]
  Passed Wintap.Tests.WintapMessageTests.Constructor_SetsMessageTypeAndPid [9 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 4) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord [13 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_EmitsResolverMissAndIncrementsBootReplayCount [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotSuppressWhenResolverMissesOrOutsideTolerance [4 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "c:/programdata/wintap/./boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: True) [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "\0", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: null, configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "   ", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.BootTraceOwnershipPredicate_MatchesOnlyConfiguredNormalizedPath(sessionPath: "C:\\ProgramData\\Other\\boot-process-trace.etl", configuredPath: "C:\\ProgramData\\Wintap\\boot-process-trace.etl", expected: False) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverLookupResult_ForPidReuse [< 1 ms]
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
  Passed Wintap.Tests.WindowsProcessSensorTests.Stop_LogsFinalQaCountersWithSameSnapshotFormat [17 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.HandleReplayedStart_SuppressesDuplicateWithinToleranceWithoutIncrementingBootReplayCount [< 1 ms]
[xUnit.net 00:00:00.18]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SuppressesDuplicateRefreshWithinTolerance [< 1 ms]

Test Run Successful.
Total tests: 46
     Passed: 46
 Total time: 0.5611 Seconds
```

## Behavioral Notes
- With `EnableBootProcessTrace=False`, `WindowsSubscriptionManager` does not call the boot-trace helper on startup or shutdown.
- Startup boot handling runs before `WindowsProcessSensor` construction and before shared kernel session/source/parser materialization.
- Boot replay uses the existing emit path and resolver-backed tolerance dedup; replay emits Start only and increments `boot_replay_count` only for emitted replay Starts.
- `EtwKernelCollector.cs`, Wintap message schema, PidHash formula, Esper EPL, TraceEvent version, and NuGet dependencies were unchanged.
- The optional logger attribution rider was not applied; the existing logging delegate behavior remains unchanged.

## Post-Implementation Local Validation
- 2026-08-18 Architect validation: published build was deployed locally, host was rebooted, and Wintap was allowed to run overnight. Architect reported that results looked good.
- No additional code or test changes were made during closeout.

## Deviations From Instruction
- The test-project build command emitted hundreds of pre-existing analyzer/platform warnings; the audit records its success summary plus the full detailed test runner outputs with test names. No code behavior was changed to suppress those warnings.

## Follow-up Notes
- wpc-08 remains the formal validation-harness slice if detailed reproducible scoring artifacts are needed later; the manual reboot/overnight run is recorded here as local acceptance evidence for wpc-07 closeout.
