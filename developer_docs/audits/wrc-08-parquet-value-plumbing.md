# Audit Artifact: wrc-08 Parquet Value Plumbing

**Date:** 2026-08-25
**Instruction:** `developer_docs/instructions/wrc-08-parquet-value-plumbing.md`
**Status:** Complete

## Scope Implemented
- Added previous registry data and type fields to the registry EPL select list and grouping key.
- Extracted the registry EventBean mapping into the order-preserving `RegistrySerializer.BuildFlatMessage` seam and added the three always-present Parquet columns.
- Added nine tests covering the embedded EPL artifact, standalone NEsper compilation, the 18-column contract, enum rendering, first writes, null guards, preserved semantics, and independent value passthrough.

## Files Created
- `tests/Wintap.Tests/RegistryParquetPlumbingTests.cs`
- `developer_docs/audits/wrc-08-parquet-value-plumbing.md`

## Files Modified
- `wintap/core/etl/esper/registry.epl`
- `wintap/core/etl/extract/RegistrySerializer.cs`

## Tests Run
- `dotnet build -c Release`
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0 --no-restore`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore --filter "Category=wrc-08" --logger "console;verbosity=detailed"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore --filter "Category~wrc"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore`
- `git diff --check -- "wintap/core/etl/esper/registry.epl" "wintap/core/etl/extract/RegistrySerializer.cs" "tests/Wintap.Tests/RegistryParquetPlumbingTests.cs"`

## Test Results
```text
C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.08
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

Time Elapsed 00:00:00.89
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.11]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.11]   Starting:    Wintap.Tests
  Passed Wintap.Tests.RegistryParquetPlumbingTests.BuildFlatMessagePreservesExistingColumnSemantics [56 ms]
  Passed Wintap.Tests.RegistryParquetPlumbingTests.EmbeddedEplGroupsByPreviousValueFields [< 1 ms]
  Passed Wintap.Tests.RegistryParquetPlumbingTests.BuildFlatMessageEncodesFirstWriteWithoutPreviousValue [< 1 ms]
  Passed Wintap.Tests.RegistryParquetPlumbingTests.EmbeddedEplSelectsAllRegistryValueFields [9 ms]
  Passed Wintap.Tests.RegistryParquetPlumbingTests.BuildFlatMessageKeepsCurrentAndPreviousValuesIndependent [< 1 ms]
[xUnit.net 00:00:01.67]   Finished:    Wintap.Tests
  Passed Wintap.Tests.RegistryParquetPlumbingTests.EmbeddedEplCompilesAgainstWintapMessage [1 s]
  Passed Wintap.Tests.RegistryParquetPlumbingTests.BuildFlatMessageRendersEnumNamesNeverOrdinals [< 1 ms]
  Passed Wintap.Tests.RegistryParquetPlumbingTests.BuildFlatMessagePreservesEighteenColumnContractAndOrder [3 ms]
  Passed Wintap.Tests.RegistryParquetPlumbingTests.BuildFlatMessageNullGuardsOnlyNewValueColumns [< 1 ms]

Test Run Successful.
Total tests: 9
     Passed: 9
 Total time: 2.0809 Seconds
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   134, Skipped:     0, Total:   134, Duration: 1 s - Wintap.Tests.dll (net8.0)
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   299, Skipped:     0, Total:   299, Duration: 2 s - Wintap.Tests.dll (net8.0)
```

```text
git diff --check: no whitespace errors (line-ending warnings only).
```

## Behavioral Notes
- The embedded registry EPL compiles successfully against the real `WintapMessage` type using the production-equivalent case-insensitive compiler configuration; the documented compile fallback was not needed.
- Adding previous value and type to the EPL grouping key intentionally separates otherwise-identical rows whose previous values differ within the same 10-second batch.
- `Reg_DataType`, `Reg_PreviousData`, and `Reg_PreviousDataType` are inserted immediately after `Reg_Data`; boxed enums render as names, and null new fields render as `"NONE"`, `""`, and `"NONE"` respectively.
- Existing column names, insertion order, and semantics are retained, including `HostHame`, raw `FirstSeenMs`/`LastSeenMs`, and `EventTime` derived from `firstSeen`.
- `publish/esper/registry.epl` is generated deployment output and was not edited; a rebuilt/republished deployment is required to ship the source EPL change.
- The Architect's live-data DuckDB verification addendum remains owed after a branch build produces live registry Parquet data.

## Deviations From Instruction
- The repository-root `dotnet build -c Release` failed with the documented `MSB4249` website-project issue. The instructed project-scoped fallback build passed. Release/no-build/no-restore flags were used for the final test runs after the successful build.

## Follow-up Notes
- Architect to run and record the required live-data DuckDB query showing a populated overwrite row and first-write `NONE` behavior.
