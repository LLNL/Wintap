# wpc-03 Snapshot Refresh Audit

**Date:** 2026-08-17
**Status:** Implemented

## Summary

Implemented live Windows process snapshot refresh in `WindowsProcessSensor` without wiring it into runtime startup. The new `InitializeSnapshotRefresh()` path clears the process DB, builds a snapshot with the three existing synthetic system seeds, computes snapshot `PidHash` / batch parent hashes, suppresses resolver-matched live Start duplicates, and emits `Refresh` process messages oldest-first.

## Files Changed

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
- `developer_docs/audits/wpc-03-snapshot-refresh.md`

## Notes

- `EventChannel.cs`, `ProcessResolver.cs`, `ProcessHash.cs`, `WintapMessage`, and `ProcessObject` were not modified.
- TraceEvent remains at `3.1.23`.
- No NuGet dependencies were added.
- Unit tests use the internal snapshot DTO and seams; they do not start ETW sessions, require administrator privileges, or depend on the host process list.
- Solution-level commands fail because `Wintap.sln` includes the existing `Wintap-Workbench` website project, producing `MSB4249`. Project-scoped fallbacks were used as allowed by the instruction.

## Verification

- `dotnet build -c Release`
  - Failed with documented `MSB4249` website-project issue.
- `dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0`
  - Passed.
  - Existing restore/package warnings remained: `NU1603`, `NU1903`, `NU1701`.
- `dotnet test --filter "Category=wpc-03"`
  - Failed with documented `MSB4249` website-project issue.
- `dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-03"`
  - Passed: 7 passed, 0 failed, 0 skipped.
