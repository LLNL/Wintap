# Audit Artifact: wrc-06 Manifest-Only Registry Sensor Rewrite

**Date:** 2026-08-25
**Instruction:** `developer_docs/instructions/wrc-06-manifest-registry-sensor.md`
**Status:** Complete

## Scope Implemented
- Replaced the legacy registry collector with numeric manifest-event dispatch, typed payload extraction, capture-payload decoding, normalized/qualified paths, and explicit registry message contracts.
- Wired `RegistryCaptureEnabler` to the live TraceEvent session through the approved minimal session hook and disposed it during sensor stop.
- Deleted the pointer maps, value cache, string-parsing event wrappers, and live-registry enrichment models.
- Added no-ETW handler-seam tests covering path handling, native type mapping, all six registry value types, previous values, explicit `NONE` fields, read gating, qualification drops, and emit-once behavior.

## Files Created
- `tests/Wintap.Tests/RegistrySensorTests.cs`
- `developer_docs/audits/wrc-06-manifest-registry-sensor.md`

## Files Modified
- `wintap/platform/windows/sensor/etw/RegistrySensor.cs`
- `wintap/platform/windows/sensor/shared/EtwProviderSensor.cs`
- `wintap/platform/windows/sensor/etw/helpers/RegistryEventParsers.cs` (deleted)
- `wintap/platform/windows/sensor/etw/helpers/RegistryManager.cs` (deleted)
- `wintap/platform/windows/sensor/shared/models/RegistryEvent.cs` (deleted)
- `wintap/platform/windows/sensor/shared/models/KernelRegistryEvent.cs` (deleted)

## Tests Run
- `dotnet build -c Release`
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0 --no-restore`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --filter "Category=wrc-06" --logger "console;verbosity=detailed" -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore --filter "Category~wrc"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore`
- `git diff --check`
- Source searches for deleted legacy types, `Microsoft.Win32`, string dispatch/parsing, dictionaries/caches, and out-of-scope wrc-07 wiring.

## Test Results
```text
C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.09
```

```text
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.

C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
    8 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.87
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.06]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.12]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.12]   Starting:    Wintap.Tests
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 12, expected: NONE) [5 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 7, expected: MULTI_SZ) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 6, expected: NONE) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 0, expected: NONE) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 1, expected: STRING) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 5, expected: NONE) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 2, expected: EXPAND_SZ) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: -1, expected: NONE) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 3, expected: BINARY) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 99, expected: NONE) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 11, expected: QWORD) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 8, expected: NONE) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.MapDataTypeMapsNativeTypesAndDefaultsToNone(nativeType: 4, expected: DWORD) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.NormalizeKeyPathProducesLegacyQualifiedForm(input: "", expected: "") [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.NormalizeKeyPathProducesLegacyQualifiedForm(input: null, expected: "") [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.NormalizeKeyPathProducesLegacyQualifiedForm(input: "Registry\Machine\Software", expected: "registry\machine\software") [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.NormalizeKeyPathProducesLegacyQualifiedForm(input: "\REGISTRY\MACHINE\SOFTWARE\Waves Audio\MaxxAu"···, expected: "registry\machine\software\waves audio\maxxaudi"···) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.NormalizeKeyPathProducesLegacyQualifiedForm(input: "   ", expected: "") [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.HandleCreateKeyEmitsCompleteExplicitContract [12 ms]
  Passed Wintap.Tests.RegistrySensorTests.HandleSetValueDecodesSixTypeOverwriteMatrix(type: 11, current: [17, 34, 51, 68, 85, ···], previous: [136, 119, 102, 85, 68, ···], expectedCurrent: "0x8877665544332211", expectedPrevious: "0x1122334455667788", expectedType: QWORD) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.HandleSetValueDecodesSixTypeOverwriteMatrix(type: 3, current: [202, 254, 186, 190], previous: [222, 173, 190, 239], expectedCurrent: "CA-FE-BA-BE", expectedPrevious: "DE-AD-BE-EF", expectedType: BINARY) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.HandleSetValueDecodesSixTypeOverwriteMatrix(type: 2, current: [37, 0, 84, 0, 77, ···], previous: [37, 0, 84, 0, 69, ···], expectedCurrent: "%TMP%\wrc2", expectedPrevious: "%TEMP%\wrc", expectedType: EXPAND_SZ) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.HandleSetValueDecodesSixTypeOverwriteMatrix(type: 7, current: [100, 0, 101, 0, 108, ···], previous: [97, 0, 108, 0, 112, ···], expectedCurrent: "delta|epsilon", expectedPrevious: "alpha|beta|gamma", expectedType: MULTI_SZ) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.HandleSetValueDecodesSixTypeOverwriteMatrix(type: 4, current: [33, 67, 101, 135], previous: [120, 86, 52, 18], expectedCurrent: "0x87654321", expectedPrevious: "0x12345678", expectedType: DWORD) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.HandleSetValueDecodesSixTypeOverwriteMatrix(type: 1, current: [103, 0, 111, 0, 111, ···], previous: [104, 0, 101, 0, 108, ···], expectedCurrent: "goodbye-wrc", expectedPrevious: "hello-wrc", expectedType: STRING) [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.ReadGateSeamIsConsultedAndReadCarriesNoData [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.DeleteRowsNormalizePathsAndSetNoDataExplicitly [< 1 ms]
[xUnit.net 00:00:00.22]   Finished:    Wintap.Tests
  Passed Wintap.Tests.RegistrySensorTests.AssembleCreateKeyPathHandlesEdges [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.AssembleCreateKeyPathHandlesRelativeAndAbsoluteNames [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.FirstWriteEmitsNoneForMissingPreviousValue [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.NonexistentKeyPathEmitsWithoutLiveEnrichment [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.EveryHandlerEmitsAtMostOnce [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.UnqualifiedCreateKeyIsDropped [< 1 ms]
  Passed Wintap.Tests.RegistrySensorTests.EmptyKeyNameDropsEveryKeyNameBasedKind [< 1 ms]

Test Run Successful.
Total tests: 34
     Passed: 34
 Total time: 0.6544 Seconds
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   111, Skipped:     0, Total:   111, Duration: 81 ms - Wintap.Tests.dll (net8.0)
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   276, Skipped:     0, Total:   276, Duration: 1 s - Wintap.Tests.dll (net8.0)
```

```text
Legacy-type source search: no matches.
RegistrySensor forbidden API/string-dispatch/cache search: no matches.
RegistrySensor wrc-07 wiring search: no matches.
git diff --check: no whitespace errors (line-ending warnings only).
```

## Behavioral Notes
- CreateKey uses `BaseName` plus relative `RelativeName`, or an absolute `RelativeName`; all other emitted kinds use their own `KeyName`. Unqualified paths are dropped without per-event logging.
- Write values and previous values decode only from ETW payload bytes. There are no live-registry enrichment reads, maps, dictionaries, or caches. `Microsoft.Win32` is absent from `RegistrySensor.cs`.
- Read events intentionally carry only path and value name, with `Data=""`, `DataType=NONE`, `PreviousData=""`, and `PreviousDataType=NONE`, because QueryValueKey has no native type field.
- `Registry.PID` remains unset at zero for legacy parity; message-level `PID` carries attribution.
- The write-only canary planned for wrc-07 does not conflict with the no-enrichment-read requirement.
- The EventChannel self-PID filter remains unchanged.
- The legacy throw-swallowed paths are now functional: CreateKey, DeleteKey, DeleteValue, ExpandString, MultiString, and QWord can emit, so production Registry volume is expected to increase toward the probe8 masked baseline.

## Deviations From Instruction
- The repository-root `dotnet build -c Release` failed with the documented `MSB4249` website-project issue. The instructed project-scoped fallback build passed.

## Follow-up Notes
- Existing `EtwProviderCollector.Stop()` derives an attach name from `EtwProviderId` while `Start()` creates the session from `SensorName`; this pre-existing shared lifecycle behavior was outside the instruction's explicitly limited hook-only change.
