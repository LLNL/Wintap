# Audit Artifact: wrc-05 WintapMessage Registry Schema Extension

**Date:** 2026-08-25
**Instruction:** `developer_docs/instructions/wrc-05-wintapmessage-registry-schema.md`
**Status:** Complete

## Scope Implemented
- Appended `QWORD` and `NONE` to `DataTypeEnum` while preserving ordinals 0-4.
- Added serializer-visible `PreviousData` and `PreviousDataType` properties to `RegActivityObject` after `Data` and before `PID`.
- Added tests for enum stability and parsing, registry field round-trips, first-write and default behavior, reflection visibility, and unchanged existing properties.

## Files Created
- `tests/Wintap.Tests/WintapMessageRegistrySchemaTests.cs`
- `developer_docs/audits/wrc-05-wintapmessage-registry-schema.md`

## Files Modified
- `shared/WintapAPI/WintapMessage.cs`

## Tests Run
- `dotnet build -c Release`
- `dotnet build "shared\WintapAPI\WintapAPI.csproj" -c Release`
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-05"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --filter "Category=wrc-05" --logger "console;verbosity=detailed"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --logger "console;verbosity=detailed"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build`

## Test Results

Root solution build (known website-project issue):

```text
C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.12
```

WintapAPI project-scoped fallback build output:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.30
```

Wintap project-scoped fallback build result:

```text
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.
    16 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.35
```

Filtered wrc-05 test output, including every test case:

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.10]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.11]   Starting:    Wintap.Tests
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.DataTypeEnum_ParsesAndRoundTrips(value: "QWORD", ignoreCase: False, expected: QWORD, expectedName: "QWORD") [4 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.DataTypeEnum_ParsesAndRoundTrips(value: "NONE", ignoreCase: False, expected: NONE, expectedName: "NONE") [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.DataTypeEnum_ParsesAndRoundTrips(value: "qword", ignoreCase: True, expected: QWORD, expectedName: "QWORD") [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_ExistingPropertiesRemainUnchanged(name: "PID", expectedType: typeof(int)) [1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_ExistingPropertiesRemainUnchanged(name: "Data", expectedType: typeof(string)) [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_ExistingPropertiesRemainUnchanged(name: "ValueName", expectedType: typeof(string)) [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_ExistingPropertiesRemainUnchanged(name: "DataType", expectedType: typeof(gov.llnl.wintap.collect.models.WintapMessage+DataTypeEnum)) [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_ExistingPropertiesRemainUnchanged(name: "Path", expectedType: typeof(string)) [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_NewPropertiesAreSerializerVisible(name: "PreviousData", expectedType: typeof(string)) [< 1 ms]
[xUnit.net 00:00:00.15]   Finished:    Wintap.Tests
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_NewPropertiesAreSerializerVisible(name: "PreviousDataType", expectedType: typeof(gov.llnl.wintap.collect.models.WintapMessage+DataTypeEnum)) [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_DefaultsRemainDocumentedValues [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryMessage_RoundTripsAllRegistryProperties [1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.DataTypeEnum_PreservesOrdinalsAndAppendsNewMembers [< 1 ms]
  Passed Wintap.Tests.WintapMessageRegistrySchemaTests.RegistryActivity_FirstWriteEncodingRoundTrips [< 1 ms]

Test Run Successful.
Total tests: 14
     Passed: 14
 Total time: 0.5848 Seconds
```

Full test-project regression output:

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   231, Skipped:     0, Total:   231, Duration: 2 s - Wintap.Tests.dll (net8.0)
```

## Behavioral Notes
- Existing `DataTypeEnum` ordinals remain stable; `QWORD=5` and `NONE=6`.
- A default-constructed `RegActivityObject` has `PreviousData == null` and `PreviousDataType == STRING`; wrc-06 must assign `NONE` explicitly when no previous value exists.
- The new public properties are reflection-visible, so Parquet and Esper/EPL consumers see new-but-optional additive columns without serializer changes.

## Deviations From Instruction
- The repository-root `dotnet build -c Release` failed with the documented `MSB4249` website-project issue. Both instructed project-scoped fallback builds were used successfully.

## Follow-up Notes
- None
