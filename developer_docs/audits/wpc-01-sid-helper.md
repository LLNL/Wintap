# Audit Artifact: wpc-01 SID Extraction Helper

**Date:** 2026-08-17
**Instruction:** `developer_docs/instructions/wpc-01-sid-helper.md`
**Status:** Complete

## Scope Implemented
- Added the Windows ETW `ProcessTraceData` UserSID extraction helper with the required `SidParseStatus` enum, public extension overloads, and internal pure payload parser.
- Added synthetic payload xUnit coverage for extracted SIDs, null-SID markers, and malformed payload guards across required versions and pointer sizes.
- Updated the Windows test project to reference `wintap/Wintap.csproj` without adding any NuGet package references.

## Files Created
- `wintap/platform/windows/sensor/etw/helpers/ProcessTraceDataExtensions.cs`
- `tests/Wintap.Tests/WindowsProcessSidExtractionTests.cs`

## Files Modified
- `tests/Wintap.Tests/Wintap.Tests.csproj`

## Tests Run
- `dotnet build -c Release` from `C:\PUBLIC\wintap\tests\Wintap.Tests`
- `dotnet test --filter "Category=wpc-01"` from `C:\PUBLIC\wintap\tests\Wintap.Tests`
- `dotnet test --filter "Category=wpc-01" --logger "console;verbosity=detailed"` from `C:\PUBLIC\wintap\tests\Wintap.Tests` to capture test names for the Architect handoff

## Test Results
```text
> dotnet build -c Release
  Determining projects to restore...
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  All projects are up-to-date for restore.
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(30,45): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier.ToString()' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(152,25): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier.BinaryLength' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(157,13): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier.GetBinaryForm(byte[], int)' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(151,31): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
  Wintap.Tests -> C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll

Build succeeded.

    34 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.12

> dotnet test --filter "Category=wpc-01"
  Determining projects to restore...
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  All projects are up-to-date for restore.
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Debug\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Debug\net8.0\Wintap.dll
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(151,31): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(30,45): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier.ToString()' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(157,13): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier.GetBinaryForm(byte[], int)' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\WindowsProcessSidExtractionTests.cs(152,25): warning CA1416: This call site is reachable on all platforms. 'SecurityIdentifier.BinaryLength' is only supported on: 'windows'. (https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1416) [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
  Wintap.Tests -> C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 10 ms - Wintap.Tests.dll (net8.0)

> dotnet test --filter "Category=wpc-01" --logger "console;verbosity=detailed"
  Determining projects to restore...
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
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
[xUnit.net 00:00:00.11]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.11]   Starting:    Wintap.Tests
[xUnit.net 00:00:00.16]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 4) [13 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 4) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 3, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(version: 4, pointerSize: 8) [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero [< 1 ms]
  Passed Wintap.Tests.WindowsProcessSidExtractionTests.TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength [< 1 ms]

Test Run Successful.
Total tests: 9
     Passed: 9
 Total time: 0.7393 Seconds
```

## Behavioral Notes
- Tests use synthetic byte-array payloads only; no live ETW sessions, administrator privileges, or Security event log reads are required.
- The parser sets `postSidOffset` only for extracted SIDs and null-SID markers, and leaves it at `-1` for malformed payloads.
- A repo-root `dotnet build -c Release` attempts to build the solution website project `Wintap-Workbench` and fails with an existing MSB4249 website-project/MSBuild limitation; the exact build command above was run from the test project directory so it builds `Wintap.Tests`, `Wintap`, and `WintapAPI`.

## Deviations From Instruction
- None

## Follow-up Notes
- None
