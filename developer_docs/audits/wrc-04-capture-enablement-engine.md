# Audit Artifact: wrc-04 Capture-Mode Enablement Engine

**Date:** 2026-08-25
**Instruction:** `developer_docs/instructions/wrc-04-capture-enablement-engine.md`
**Status:** Complete

## Scope Implemented
- Added a guarded TraceEvent 3.1.23 session-handle acquisition path with distinct field-null and method-missing failures.
- Added the exact disable-then-enable `EnableTraceEx2` sequence with a pinned 4-byte `0xFFFFFFFF` capture descriptor, injected keyword mask, tolerant disable errors, and throwing enable errors.
- Added serialized periodic and capture-loss re-assertion paths, resilient timer-tick handling, logging, disposal, and successful re-assert observability.
- Added no-ETW tests for reflection compatibility, guard behavior, native arguments and ordering, marshaled filter contents, error handling, re-assertion, and x64 struct layout.

## Files Created
- `wintap/platform/windows/sensor/shared/RegistryCaptureEnabler.cs`
- `tests/Wintap.Tests/RegistryCaptureEnablerTests.cs`
- `developer_docs/audits/wrc-04-capture-enablement-engine.md`

## Files Modified
- None

## Tests Run
- `dotnet build -c Release`
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --filter "Category=wrc-04" --logger "console;verbosity=normal" -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore`

## Test Results

Root solution build (known website-project issue):

```text
C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.11
```

Project-scoped fallback build result:

```text
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.
    16 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.43
```

Filtered wrc-04 test output, including every test case:

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.11]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.11]   Starting:    Wintap.Tests
  Passed Wintap.Tests.RegistryCaptureEnablerTests.AcquireHandleRejectsValueWithoutDangerousGetHandle [12 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.TraceEventReflectionContractMatchesPinnedVersion [9 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.AcquireHandleUsesDuckTypedDangerousGetHandle [1 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.ReassertTickSurvivesFailureAndRetriesSuccessfully [2 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.EnableFailureThrowsAndDisableFailureIsTolerated [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.EnableCaptureUsesDisableThenEnableWithExpectedArguments [3 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.AcquireHandleRejectsNullFieldValue [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.CaptureLossNotificationImmediatelyReasserts [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.ReassertCaptureRepeatsSequenceAndIncrementsCount [< 1 ms]
[xUnit.net 00:00:00.18]   Finished:    Wintap.Tests
  Passed Wintap.Tests.RegistryCaptureEnablerTests.EnableCaptureMarshalsExactFourByteCaptureFilter [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureEnablerTests.NativeStructLayoutsMatchWindowsX64Abi [< 1 ms]

Test Run Successful.
Total tests: 11
     Passed: 11
 Total time: 0.5875 Seconds
```

Full test-project regression output:

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   242, Skipped:     0, Total:   242, Duration: 2 s - Wintap.Tests.dll (net8.0)
```

## Behavioral Notes
- Capture assertion is serialized so timer and capture-loss re-assertions cannot interleave their disable/enable pairs.
- The production constructor logs through `WintapLogger`; the injected-handle test seam accepts a no-op logger so unit tests do not require access to the production log directory.
- No live ETW session is created by the tests; kernel capture behavior remains assigned to wrc-07 live verification.

## Deviations From Instruction
- The repository-root `dotnet build -c Release` failed with the documented `MSB4249` website-project issue. The instruction's project-scoped fallback build was used successfully.

## Follow-up Notes
- None
