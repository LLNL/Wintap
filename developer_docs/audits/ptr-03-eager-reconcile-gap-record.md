# ptr-03 Eager Reconcile And Gap Record Audit

**Date:** 2026-08-23
**Branch:** `process-tree-recovery`
**Instruction:** `developer_docs/instructions/ptr-03-eager-reconcile-gap-record.md`
**Status:** Complete

## Scope

Implemented ptr-03 on top of the existing ptr-01, ptr-04, and ptr-02 work.
Same-boot keep-path startup now records a bounded collection gap, constructs a
resolver-hash-aware live process set, and closes stale open real-process rows at
the gap end. Each closed row queues the distinct
`startup_reconciled_closed` telemetry metric.

Added the single-row `collection_heartbeat` and append-only `collection_gap`
tables and resolver, interface, and EventChannel operations. Heartbeats are
updated by successful maintenance sweeps and by the Windows process sensor's
clean shutdown path. No timer was added.

## Files Changed

- `wintap/core/infrastructure/ProcessResolver.cs`
- `wintap/core/infrastructure/IProcessResolver.cs`
- `wintap/core/infrastructure/EventChannel.cs`
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- `tests/Wintap.Tests/ProcessTreeRecoveryGapTests.cs`
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`
- `tests/Wintap.Tests/SystemIdentityProbeTests.cs`
- `developer_docs/audits/ptr-03-eager-reconcile-gap-record.md`

## Verification

The exact repository-root commands were run:

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-03"
dotnet test --filter "Category~ptr"
```

All three stop at the existing solution-level website-project issue:

```text
MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET
compiler is only available on the .NET Framework version of MSBuild.
```

The documented project-scoped fallbacks passed:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-03" -c Release --no-restore
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~ptr" -c Release --no-restore
```

Results:

```text
Build succeeded: 0 errors, 16 existing package warnings.
Category=ptr-03: 11 passed, 0 failed, 0 skipped.
Category~ptr: 35 passed, 0 failed, 0 skipped.
```

The warnings are existing NU1603, NU1903, and NU1701 package advisories.
`git diff --check` passed with only LF-to-CRLF working-copy notices.

## Coverage

- Heartbeat empty/read/update/single-row behavior and gap field persistence.
- Reconcile keep/close/exited/synthetic behavior, empty and null sets, and PID
  reuse where the old hash closes while the new hash stays open.
- Keep-path operation ordering, complete emitted live sets, resolver hashes for
  suppressed duplicates, heartbeat-present and heartbeat-absent gap bounds,
  rebuild-path exclusion, reconcile failure containment, and shutdown heartbeat
  failure containment.
- Instance telemetry queuing was not queried directly because there is no
  existing isolated instance-construction precedent. Per the instruction's
  fallback, static helper returned-list coverage verifies exactly which rows
  feed the instance telemetry loop.

## Manual Check

Not performed. The service restart check requires an elevated Windows host with
a live event store and a subsequent maintenance sweep. Automated tests do not
claim this operational validation.

## Constraints

No process/message schema, PidHash formula, dependency, retention sweep,
Linux, or macOS changes were made. Existing unrelated worktree changes and the
untracked `diagnostics/sid-extraction-test/` directory were not reverted or
modified.
