# Audit Artifact: wpc-06 Wire-in and Removal

**Date:** 2026-08-17
**Instruction:** `developer_docs/instructions/wpc-06-wire-in-removal.md`
**Status:** Complete

## Scope Implemented
- Replaced legacy Windows process bootstrap with mandatory first `WindowsProcessSensor` construction, snapshot refresh initialization, start/subscription, kernel Process flag seeding, and shutdown tracking through `baseSensors`.
- Deleted the legacy Security-log `ProcessSensor` and legacy manifest-only `KernelProcessSensor` files.
- Removed deleted process-sensor settings from `Settings.settings`, `Settings.Designer.cs`, and `App.config`.
- Removed compile-time references to deleted process sensors from Windows runtime code and plugin setting enablement.
- Added Windows process QA counter state, immutable snapshot DTO, stable snake_case formatter, interval timer logging, and shutdown logging.
- Added wpc-06 unit coverage for QA counter names/format, counter values after simulated activity, and final shutdown logging.

## Files Created
- `developer_docs/audits/wpc-06-wire-in-removal.md`

## Files Modified
- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `wintap/platform/windows/sensor/etw/APICallSensor.cs`
- `wintap/platform/windows/sensor/shared/EtwProviderSensor.cs`
- `wintap/core/infrastructure/PluginManager.cs`
- `wintap/core/infrastructure/EventChannel.cs`
- `wintap/Properties/Settings.settings`
- `wintap/Properties/Settings.Designer.cs`
- `wintap/App.config`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`

## Files Deleted
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs`
- `wintap/platform/windows/sensor/etw/KernelProcessSensor.cs`

## Tests Run
- `dotnet build -c Release`
- `dotnet test --filter "Category~wpc"`
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
- `dotnet build "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --filter "Category~wpc" --no-build --logger "console;verbosity=detailed"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --logger "console;verbosity=detailed"`
- Searches performed:
  - `ProcessSensor|KernelProcessSensor|ProcessStopSensor` in `*.cs`
  - `ProcessSensor|KernelProcessSensor|ProcessStopSensor` in `*.config`
  - `ProcessSensor|KernelProcessSensor|ProcessStopSensor` in `*.settings`
  - `TraceEvent` in `*.csproj`

## Test Results
```text
> dotnet build -c Release
C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.09

> dotnet test --filter "Category~wpc"
C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

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

Time Elapsed 00:00:01.76

> dotnet build "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release -p:WarningLevel=0
  Determining projects to restore...
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
  All projects are up-to-date for restore.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
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
  Wintap.Tests -> C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll

Build succeeded.

C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
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
  Wintap.Tests -> C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll

Build succeeded.

C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
    30 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.57

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --filter "Category~wpc" --no-build --logger "console;verbosity=detailed"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.09]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.09]   Starting:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 4) [10 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 4) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord [13 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotSuppressWhenResolverMissesOrOutsideTolerance [4 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverLookupResult_ForPidReuse [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedCommandLineFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_ComputesPidHashFromCanonicalEtwStartTime [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_EnrichmentExceptionsDoNotPreventEmission [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricExpiry_IncrementsManifestMetricMissesAndDoesNotBlockCallback [3 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationWindowHit_MergesMetricsForBothOrderingCases [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelation_UsesNearestMetricsAndResolverTimestampForPidReuse [2 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_UsesEnrichedFieldsAndEtwStartTimestampPidHash [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ClearsProcessDbBeforeFirstRefreshEmit [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_BoundsSidAccountCacheAndEvictsDeterministically [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_ReportsExpectedValuesAfterSimulatedActivity [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_EmitsOldestFirstWithPidTieBreaker [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotChooseFutureParentInstance [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_CachesExtractedSidAccountLookup [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.CanonicalizeCreateTimeUtc_NormalizesEtwTimestampToUtc [1 ms]
[xUnit.net 00:00:00.18]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ChoosesLatestParentInstanceBeforeChild [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedPathFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedUserFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_IncludesSyntheticSystemProcessSeeds [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationMiss_EmitsDefaultsAfterExpiry [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_UsesExpectedNamesAndFormatExactlyOnce [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.Stop_LogsFinalQaCountersWithSameSnapshotFormat [25 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SuppressesDuplicateRefreshWithinTolerance [< 1 ms]

Test Run Successful.
Total tests: 35
     Passed: 35
 Total time: 0.5860 Seconds

> dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --logger "console;verbosity=detailed"
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.08]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.09]   Starting:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 4) [6 ms]
  Passed Wintap.Tests.WintapMessageTests.Constructor_SetsMessageTypeAndPid [6 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 4) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverReturnedFields_WhenResolverReturnsRecord [12 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_DoesNotSuppressWhenResolverMissesOrOutsideTolerance [3 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_UsesResolverLookupResult_ForPidReuse [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_UsesExpectedCommandLineFallbackMatrix [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_ComputesPidHashFromCanonicalEtwStartTime [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_EnrichmentExceptionsDoNotPreventEmission [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricExpiry_IncrementsManifestMetricMissesAndDoesNotBlockCallback [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelationWindowHit_MergesMetricsForBothOrderingCases [1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.StopMetricCorrelation_UsesNearestMetricsAndResolverTimestampForPidReuse [2 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStart_UsesEnrichedFieldsAndEtwStartTimestampPidHash [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_ClearsProcessDbBeforeFirstRefreshEmit [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EnrichStartFields_BoundsSidAccountCacheAndEvictsDeterministically [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.QaCounterSnapshot_ReportsExpectedValuesAfterSimulatedActivity [< 1 ms]
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
  Passed Wintap.Tests.WindowsProcessSensorTests.Stop_LogsFinalQaCountersWithSameSnapshotFormat [16 ms]
  Passed Wintap.Tests.WindowsProcessSensorTests.EmitStop_IncrementsMissCounterAndUsesStopTimeFallback_WhenResolverReturnsNull [< 1 ms]
[xUnit.net 00:00:00.16]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSensorTests.InitializeSnapshotRefresh_SuppressesDuplicateRefreshWithinTolerance [< 1 ms]

Test Run Successful.
Total tests: 36
     Passed: 36
 Total time: 0.5550 Seconds
```

Search results:

```text
> ProcessSensor|KernelProcessSensor|ProcessStopSensor in *.config
No files found

> ProcessSensor|KernelProcessSensor|ProcessStopSensor in *.settings
No files found

> TraceEvent in *.csproj
C:\PUBLIC\wintap\wintap\Wintap.csproj: Microsoft.Diagnostics.Tracing.TraceEvent Version="3.1.23"
C:\PUBLIC\wintap\platform\windows\WintapCoreSvcMgr\WintapCoreSvcMgr.csproj: Microsoft.Diagnostics.Tracing.TraceEvent Version="3.1.23"
```

`ProcessSensor|KernelProcessSensor|ProcessStopSensor` in `*.cs` returns expected non-deleted matches only: `WindowsProcessSensor`, `WindowsProcessSensorTests`, Linux `ProcessSensorHelper`/example names, and a `PluginManager` comment stating `WindowsProcessSensor` is mandatory. No active Windows runtime/config references to the deleted `ProcessSensor`, `KernelProcessSensor`, or `ProcessStopSensor` remain.

## Behavioral Notes
- `WindowsSubscriptionManager.Start()` now constructs `WindowsProcessSensor` before modelled/generic sensors, runs `InitializeSnapshotRefresh()`, starts it, seeds `kernelFlags = KernelTraceEventParser.Keywords.Process`, and adds it to `baseSensors`.
- QA counter log format is: `Windows process QA counters: sid_extracted=<n> sid_null=<n> sid_malformed=<n> sid_fallback=<n> cmdline_empty=<n> cmdline_peb_recovered=<n> stop_without_start=<n> manifest_metric_misses=<n> snapshot_count=<n> dedup_suppressed=<n>`.
- QA interval logging uses a `WindowsProcessSensor`-owned 60-second timer and shutdown logging calls the same snapshot/formatter path.
- Pure wire-in ordering was not unit-tested because `WindowsSubscriptionManager.Start()` constructs live ETW/kernel/session resources and would require heavy test doubles or live ETW behavior. Build plus manual smoke covers runtime ordering.
- `.wiki/wiki/log.md` was required by the Developer workflow but `.wiki` is not present in this checkout (`Test-Path .wiki` returned `False`). No `.wiki` path was touched.
- Pre-existing unrelated working-tree changes were present in `.claude/agents/engineer.md`, `.claude/settings.local.json`, and `CLAUDE.md`; these were not modified by this unit.

Manual smoke-run handoff for Architect/elevated execution:

```powershell
# From an elevated PowerShell in C:\PUBLIC\wintap after Release build:
$env:WINTAP_DISABLE_MCP = "1"
$env:WINTAP_DISABLE_DUCKDB_UI = "1"
dotnet .\wintap\bin\Release\net8.0\Wintap.dll

# In a second PowerShell, generate process workload:
1..20 | ForEach-Object { Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList "/c exit 0" -Wait }

# Let Wintap run at least 65 seconds so an interval QA counter line is emitted.
# Stop Wintap cleanly with Ctrl+C or service stop.
```

Smoke observations to collect:

- Evidence/counts for process `Start`, `Stop`, and `Refresh` telemetry from the unified ETW/snapshot path.
- At least one interval log line beginning `Windows process QA counters:` with all ten required `name=value` pairs.
- One shutdown log line beginning `Windows process QA counters:` with the same ten required `name=value` pairs.
- Audit policy independence evidence: record process creation/termination audit policy state (`auditpol /get /subcategory:"Process Creation"` and `auditpol /get /subcategory:"Process Termination"`) and confirm process telemetry is still observed through `WindowsProcessSensor`/ETW/snapshot rather than Security-log 4688/4689 collection.

Pass criteria:

- Start/Stop/Refresh process telemetry is observed.
- Interval and shutdown QA counter lines are present and parseable.
- No Security-log process fallback/audit-policy dependency appears in runtime evidence.

Manual smoke results:

- **Executed by Architect 2026-08-17** (elevated run, Release build, documented procedure above). Result: **PASS** (audit-policy evidence waived by Architect — see note below).
- Wire-in ordering confirmed from the run log: snapshot refresh started (log line 108), process DB clear (line 109), snapshot complete with `Refreshed 489 existing processes` (line 156), `WindowsProcessSensor` started (lines 157–159), manifest metric provider enabled (line 160), shared kernel listener started after sensor loading (lines 172–175).
- Process telemetry flowing through the unified path: process parquet writes (lines 176–177, 181–182) and process stop parquet writes (lines 178–179, 183–184).
- Interval QA counter lines present and parseable (lines 180, 185), e.g.:
  `Windows process QA counters: sid_extracted=15 sid_null=0 sid_malformed=0 sid_fallback=0 cmdline_empty=0 cmdline_peb_recovered=0 stop_without_start=0 manifest_metric_misses=0 snapshot_count=489 dedup_suppressed=0`
- Shutdown QA counter line emitted on clean stop (Ctrl+C), all ten pairs present:
  `8/17/2026 4:23:14 PM [Info]  [WindowsProcessSensor..ctor]:   Windows process QA counters: sid_extracted=155 sid_null=0 sid_malformed=0 sid_fallback=0 cmdline_empty=0 cmdline_peb_recovered=0 stop_without_start=0 manifest_metric_misses=0 snapshot_count=489 dedup_suppressed=0`
- Shutdown also emitted `ProcessResolver.ReconcileStaleOpenRowsLocked` reconcile-close lines (e.g. pid=4 System, pid=43824 SearchProtocolHost.exe) with stored-vs-live PidHash values — existing resolver shutdown behavior, noted for reference.
- Audit-policy state capture (`auditpol` evidence) was waived by the Architect for this closeout; runtime evidence shows telemetry sourced from `WindowsProcessSensor`/ETW/snapshot with no Security-log 4688/4689 collection path remaining in the build (legacy sensors deleted in this unit).
- Warnings observed during the run, judged pre-existing or out-of-scope for wpc-06: `SensSensor problem loading sensor: Value cannot be null`; `SignedS3UrlAdapter` missing; several `Could not resolve parent process` warnings; DuckDB parser errors on command lines containing unterminated quoted strings.

## Deviations From Instruction
- Repository-root `dotnet build -c Release` and solution-scoped `dotnet test --filter "Category~wpc"` failed with the documented pre-existing `Wintap-Workbench` website-project `MSB4249` issue, so the instruction-approved project-scoped fallback commands were used.
- Final detailed test evidence used `-c Release --no-build --logger "console;verbosity=detailed"` after the project-scoped Release build to capture full test names/pass statuses against the built Release test assembly.
- Manual smoke run was not executed in this Developer session due lack of elevated runtime execution; exact Architect handoff procedure and pass criteria are documented above.

## Follow-up Notes
- Elevated manual smoke procedure executed by the Architect on 2026-08-17; results recorded above. wpc-06 verification gate closed.
- Out-of-scope warnings observed during the smoke run (SensSensor null-value load failure, missing SignedS3UrlAdapter, parent-process resolution warnings, DuckDB unterminated-quote parser errors) are candidates for separate follow-up units.
