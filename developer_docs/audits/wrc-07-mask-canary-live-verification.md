# Audit Artifact: wrc-07 Keyword-Mask Wire-Up, Capture-Loss Canary, and Live-Verification Support

**Date:** 2026-08-25
**Instruction:** `developer_docs/instructions/wrc-07-mask-canary-live-verification.md`
**Status:** Complete

## Scope Implemented
- Wired the Architect-final `0x5300`/`0x5700` keyword selection into both the TraceEvent provider enable and capture enabler, with verbose level and a five-minute periodic re-assert.
- Added the 60-second write-only `CaptureCanary` state machine with immediate and silent loss detection, re-assert callbacks, one-time recovery-failed escalation, recovery reporting, fail-open writes, and shutdown suppression.
- Added targeted sensor-side suppression for canary `SetValueKey` events and retained EventChannel's general self-PID backstop.
- Added automated seam tests for mask composition and selection, both loss modes, duplicate observations, escalation, recovery, write failure, matching, and shutdown behavior.

## Files Created
- `wintap/platform/windows/sensor/etw/helpers/RegistryCaptureCanary.cs`
- `tests/Wintap.Tests/RegistryCaptureCanaryTests.cs`
- `developer_docs/audits/wrc-07-mask-canary-live-verification.md`

## Files Modified
- `wintap/platform/windows/sensor/etw/RegistrySensor.cs`
- `wintap/platform/windows/sensor/shared/RegistryCaptureEnabler.cs` (comment only for this unit)

## Tests Run
- `dotnet build -c Release`
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --filter "Category=wrc-07" --logger "console;verbosity=detailed" -p:WarningLevel=0`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore --filter "Category~wrc"`
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" -c Release --no-build --no-restore`
- `git diff --check`
- Source search for `ulong.MaxValue` and live-registry read/open APIs in the registry sensor path.

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
  WintapAPI -> C:\PUBLIC\wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.

    16 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.71
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.05]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.11]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.12]   Starting:    Wintap.Tests
  Passed Wintap.Tests.RegistryCaptureCanaryTests.KeywordMasksComposeFromDocumentedProviderKeywords [3 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.EmptyKeyNameImmediatelySignalsLossAndStillSuppresses(keyName: null) [5 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.EmptyKeyNameImmediatelySignalsLossAndStillSuppresses(keyName: "") [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.MissingEventSignalsLossAtNextTick [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.NonMatchingEventDoesNotFulfillExpectationOrSignalImmediately [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.ThrowingWriteIsFailOpenAndNextTickRetriesWithoutFalseLoss [4 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.ConsecutiveFailedCycleEscalatesExactlyOnce [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.SelectKeywordMaskUsesArchitectApprovedValues [6 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.HealthyObservationAfterLossLogsOneRecoveryAndResetsState [1 ms]
[xUnit.net 00:00:00.18]   Finished:    Wintap.Tests
  Passed Wintap.Tests.RegistryCaptureCanaryTests.DuplicateMatchedEventDoesNotAdvanceCycleState [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.StopClearsPendingExpectationAndSuppressesShutdownAlarms [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.ConsecutiveEmptyKeyNameCyclesEscalateThenRecover [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.HealthyCycleWritesAndObservationFulfillsExpectation [< 1 ms]
  Passed Wintap.Tests.RegistryCaptureCanaryTests.MatcherRequiresWintapPidAndExactCanaryValueName [< 1 ms]

Test Run Successful.
Total tests: 14
     Passed: 14
 Total time: 0.6085 Seconds
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   125, Skipped:     0, Total:   125, Duration: 89 ms - Wintap.Tests.dll (net8.0)
```

```text
Test run for C:\PUBLIC\wintap\tests\Wintap.Tests\bin\Release\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   290, Skipped:     0, Total:   290, Duration: 1 s - Wintap.Tests.dll (net8.0)
```

```text
git diff --check: no whitespace errors (line-ending warnings only).
Forbidden registry-path search: no ulong.MaxValue and no live-registry read/open API matches.
```

## Behavioral Notes
- The production canary writes REG_SZ `<sequence>|<utcTicks>` to `HKLM\SOFTWARE\Wintap\Collectors\Registry\CaptureCanary`; it performs no registry reads. This write-only health signal does not violate criterion 3's ban on live-registry enrichment reads.
- Each successful write arms one expectation. Duplicate or delayed matched events are suppressed without advancing health state, and the non-reentrant timer prevents overlapping cycles.
- A matched empty `KeyName` detects loss immediately; an unobserved write detects loss at the next cycle. Each failed cycle requests capture re-assertion, while Error/RECOVERED logs are transition-only.
- Canary events are dropped in `RegistrySensor.HandleSetValue` before message construction. `EventChannel.Send`'s existing `StateManager.WintapPID` filter remains the second self-noise backstop.
- No live ETW, elevation, or real registry writes were used by automated tests.
- Architect live-verification evidence checklist: record branch/commit and both masks; capture assertion and re-assert count; at least three event-rate samples over at least 15 minutes compared with probe5/probe8; six-type write/overwrite/delete correctness from a non-Wintap elevated process; healthy canary cycles and false-loss count (optionally deliberate loss/recovery); ETW events lost plus Wintap CPU/memory observation.

## Deviations From Instruction
- The repository-root `dotnet build -c Release` failed with the documented `MSB4249` website-project issue. The instructed project-scoped fallback build passed.

## Follow-up Notes
- The Architect must perform and record the elevated lab-host live verification in `../Wintap-Analytics/wiki/work/improve-windows-registry-collection/verification.md`; that run is explicitly outside this Developer unit.
