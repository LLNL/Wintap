# Audit Artifact: wrc-03 Registry Payload Decode Core

**Date:** 2026-08-25
**Instruction:** `developer_docs/instructions/wrc-03-payload-decode-core.md`
**Status:** Complete

## Scope Implemented
- Added allocation-free numeric registry event-ID classification for manifest event IDs 1-15.
- Added pure byte decoding for REG_SZ, REG_EXPAND_SZ, REG_BINARY, REG_DWORD, REG_MULTI_SZ, REG_QWORD, empty input, truncated numeric payloads, and unknown registry types.
- Added exhaustive fixture-driven tests using the instruction's probe5 bytes, plus edge-case and dispatch coverage.

## Files Created
- `wintap/platform/windows/sensor/etw/helpers/RegistryPayloadDecoder.cs`
- `tests/Wintap.Tests/RegistryPayloadDecoderTests.cs`
- `developer_docs/audits/wrc-03-payload-decode-core.md`

## Files Modified
- None

## Tests Run
- `dotnet build -c Release`
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-03" --logger "console;verbosity=detailed"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --logger "console;verbosity=detailed" --no-restore`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --no-restore --filter "Category=wrc-03" --logger "console;verbosity=detailed"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --no-build --no-restore`

## Test Results

Root solution build (known website-project issue):

```text
C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.10
```

Project-scoped fallback build result:

```text
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.
    16 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.22
```

Filtered wrc-03 test output, including every test case:

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.10]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.10]   Starting:    Wintap.Tests
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesInitialWriteFixtures(regType: 7, data: [97, 0, 108, 0, 112, ···], expected: "alpha|beta|gamma") [5 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesInitialWriteFixtures(regType: 1, data: [104, 0, 101, 0, 108, ···], expected: "hello-wrc") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesInitialWriteFixtures(regType: 3, data: [222, 173, 190, 239], expected: "DE-AD-BE-EF") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesInitialWriteFixtures(regType: 2, data: [37, 0, 84, 0, 69, ···], expected: "%TEMP%\\wrc") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesInitialWriteFixtures(regType: 4, data: [120, 86, 52, 18], expected: "0x12345678") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesInitialWriteFixtures(regType: 11, data: [136, 119, 102, 85, 68, ···], expected: "0x1122334455667788") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesSzWithoutTerminatingNull [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 12, expected: "FlushKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 13, expected: "CloseKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 14, expected: "QuerySecurityKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 10, expected: "QueryMultipleValueKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 3, expected: "DeleteKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 4, expected: "QueryKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 5, expected: "SetValueKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 15, expected: "SetSecurityKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 7, expected: "QueryValueKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 9, expected: "EnumerateValueKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 1, expected: "CreateKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 0, expected: "Unknown") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 6, expected: "DeleteValueKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: -1, expected: "Unknown") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 999, expected: "Unknown") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 2, expected: "OpenKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 11, expected: "SetInformationKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 8, expected: "EnumerateKey") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(eventId: 16, expected: "Unknown") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 7) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 2) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 99) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 3) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 11) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 1) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 4) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(regType: 0) [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesOverwriteFixtures(regType: 3, data: [202, 254, 186, 190], expected: "CA-FE-BA-BE") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesOverwriteFixtures(regType: 11, data: [17, 34, 51, 68, 85, ···], expected: "0x8877665544332211") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesOverwriteFixtures(regType: 1, data: [103, 0, 111, 0, 111, ···], expected: "goodbye-wrc") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesOverwriteFixtures(regType: 4, data: [33, 67, 101, 135], expected: "0x87654321") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesOverwriteFixtures(regType: 2, data: [37, 0, 84, 0, 77, ···], expected: "%TMP%\\wrc2") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesOverwriteFixtures(regType: 7, data: [100, 0, 101, 0, 108, ···], expected: "delta|epsilon") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesPreviousDataFixtures(regType: 7, data: [97, 0, 108, 0, 112, ···], expected: "alpha|beta|gamma") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesPreviousDataFixtures(regType: 4, data: [120, 86, 52, 18], expected: "0x12345678") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesPreviousDataFixtures(regType: 2, data: [37, 0, 84, 0, 69, ···], expected: "%TEMP%\\wrc") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesPreviousDataFixtures(regType: 3, data: [222, 173, 190, 239], expected: "DE-AD-BE-EF") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesPreviousDataFixtures(regType: 11, data: [136, 119, 102, 85, 68, ···], expected: "0x1122334455667788") [< 1 ms]
[xUnit.net 00:00:00.16]   Finished:    Wintap.Tests
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesPreviousDataFixtures(regType: 1, data: [104, 0, 101, 0, 108, ···], expected: "hello-wrc") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_UsesFallback_ForUnknownTypes(regType: 5, bytes: "DE-AD", expected: "(type 5) DE-AD") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_UsesFallback_ForUnknownTypes(regType: 0, bytes: "01", expected: "(type 0) 01") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_UsesFallback_ForTruncatedNumericData(regType: 4, bytes: "78-56", expected: "(type 4) 78-56") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_UsesFallback_ForTruncatedNumericData(regType: 11, bytes: "88-77-66-55", expected: "(type 11) 88-77-66-55") [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DoesNotExpandExpandString [< 1 ms]
  Passed Wintap.Tests.RegistryPayloadDecoderTests.DecodeRegValue_DecodesSingleMultiSzValue [< 1 ms]

Test Run Successful.
Total tests: 52
     Passed: 52
 Total time: 0.5937 Seconds
```

Full test-project regression output:

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   217, Skipped:     0, Total:   217, Duration: 1 s - Wintap.Tests.dll (net8.0)
```

## Behavioral Notes
- REG_EXPAND_SZ values remain literal and are not expanded against Wintap's environment.
- Null and empty payloads decode to an empty string.
- Truncated DWORD/QWORD values and unsupported registry types use deterministic type-prefixed hex rendering.
- Event IDs outside 1-15 classify as `Unknown`.

## Deviations From Instruction
- The repository-root `dotnet build -c Release` failed with the documented `MSB4249` website-project issue. The instruction's project-scoped fallback build was used successfully.

## Follow-up Notes
- None
