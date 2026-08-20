# Audit Artifact: wpc-02 Sensor Core

**Date:** 2026-08-17
**Instruction:** `developer_docs/instructions/wpc-02-sensor-core.md`
**Status:** Complete

## Scope Implemented
- Added `WindowsProcessSensor` core for shared-kernel classic ETW ProcessStart/ProcessStop events.
- Added testable Start/Stop emission seams, UTC create-time canonicalization, resolver-backed Stop identity handling, and Stop resolver miss fallback counting.
- Added wpc-02 xUnit tests covering timestamp canonicalization, Start hash input, resolver-backed Stop fields, PID reuse via resolver result, and Stop miss fallback behavior.

## Files Created
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
- `developer_docs/audits/wpc-02-sensor-core.md`

## Files Modified
- None

## Tests Run
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
- From `tests/Wintap.Tests`: `dotnet test --filter "Category=wpc-02" --logger "console;verbosity=detailed"`

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
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
    16 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.33

> dotnet test --filter "Category=wpc-02" --logger "console;verbosity=detailed"  (from C:\PUBLIC\wintap\tests\Wintap.Tests)
  Determining projects to restore...
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  All projects are up-to-date for restore.
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Debug\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Debug\net8.0\Wintap.dll
  Wintap.Tests -> C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.06]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.09]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.09]   Starting:    Wintap.Tests
[xUnit.net 00:00:00.14]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord [13 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverLookupResult_ForPidReuse [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_ComputesPidHashFromCanonicalEtwStartTime [2 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.CanonicalizeCreateTimeUtc_NormalizesEtwTimestampToUtc [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull [< 1 ms]

Test Run Successful.
Total tests: 5
     Passed: 5
 Total time: 0.5789 Seconds
```

## Behavioral Notes
- `Start()` subscribes to `KernelParser.Instance.EtwParser.ProcessStart` and `ProcessStop` without creating or enabling a new ETW session.
- Production `PidHash` generation defaults to `ProcessHash.GenPidHash(pid, fileTimeUtc)`; tests use an internal hash seam to avoid initializing `StateManager`, which can require elevation on this host.
- Resolver-backed Stop events preserve resolver identity fields; resolver misses increment `StopWithoutStartCount` and still emit Stop events.

## Deviations From Instruction
- The repository-root `dotnet build -c Release` and `dotnet test --filter "Category=wpc-02"` commands target `Wintap.sln`, which currently contains the `Wintap-Workbench` website project and fail under .NET SDK MSBuild with `MSB4249`. I ran project-scoped equivalents instead.
- Added one internal `genPidHash` constructor seam beyond the suggested resolver/emit/clock seams so wpc-02 tests do not require administrator privileges through `StateManager` initialization. Production default still uses the unchanged `ProcessHash.GenPidHash` formula.

## Follow-up Notes
- None
