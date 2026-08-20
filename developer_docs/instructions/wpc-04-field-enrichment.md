# wpc-04 Field Enrichment

**Date:** 2026-08-17
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-17

## Purpose

Add field enrichment to the already-landed `WindowsProcessSensor` for the
`improve-windows-process-collection` feature. This unit improves the Start path
with SID/user, command-line, and executable-path enrichment while preserving the
resolver-backed lifecycle design from wpc-02 and the snapshot refresh behavior
from wpc-03.

This unit is intentionally narrow. It does not wire `WindowsProcessSensor` into
runtime startup, delete old sensors, add manifest Stop metrics, add boot ETL
coverage, or change the Wintap process-event schema.

## Scope

Implement this unit in the `wintap` repository.

Modify:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
- helper files under `wintap/platform/windows/sensor/etw/helpers/` only if they
  keep the sensor readable
- unit tests under `tests/Wintap.Tests/`, tagged:
  `[Trait("Category", "wpc-04")]`

The implementation must add Start/event enrichment with these source priorities:

- User:
  1. use the wpc-01 `ProcessTraceDataExtensions.TryGetUserSid` helper first;
  2. resolve extracted SID to account name via `LookupAccountSid`;
  3. cache SID-to-name lookups in a bounded in-memory cache;
  4. fall back to `OpenProcessToken` only when SID extraction returns `NoSid` or
     `Malformed` or an equivalent no-usable-SID state.
- Command line:
  1. use the ETW `ProcessTraceData.CommandLine` / payload command-line field
     first;
  2. fall back to a live PEB command-line read only when the ETW command line is
     empty.
- Path:
  1. prefer `QueryFullProcessImageName` on the live process handle;
  2. if that is unavailable, translate a device path from the ETW image payload
     to a Win32 path when possible;
  3. if translation is unavailable, keep the best ETW image value.

Enrichment failures must never drop Start, Refresh, or Stop lifecycle events.

Hard constraints for this feature, repeated verbatim:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

## Dependencies

- Existing project rules: `CLAUDE.md`
- Existing test project: `tests/Wintap.Tests/Wintap.Tests.csproj`
- Prior unit: `developer_docs/instructions/wpc-01-sid-helper.md`
  - provides `ProcessTraceDataExtensions.TryGetUserSid` and `SidParseStatus`.
- Prior unit and audit: `developer_docs/instructions/wpc-02-sensor-core.md`,
  `developer_docs/audits/wpc-02-sensor-core.md`
  - `WindowsProcessSensor` Start/Stop core is resolver-backed.
  - live Start `PidHash` uses the ETW ProcessStart timestamp as canonical.
- Prior unit and audit: `developer_docs/instructions/wpc-03-snapshot-refresh.md`,
  `developer_docs/audits/wpc-03-snapshot-refresh.md`
  - snapshot Refresh already has live process API helpers for create time,
    parent PID, path, PEB command line, and token user.

Relevant sources to read and preserve:

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs`
  - extend the landed sensor; do not replace it with a new design.
- `wintap/platform/windows/sensor/etw/helpers/ProcessTraceDataExtensions.cs`
  - use this helper for classic kernel ETW UserSID extraction.
- `wintap/platform/windows/sensor/etw/ProcessSensor.cs`
  - old Security-log behavior being replaced later; read only for field-shape
    precedent, not as an event-log source to keep.
- `wintap/core/shared/ProcessHash.cs`
  - existing `GenPidHash(pid, fileTimeUtc)` formula; do not change it.
- `wintap/core/infrastructure/EventChannel.cs` and
  `wintap/core/infrastructure/ProcessResolver.cs`
  - read-only for this unit unless a compile-only adjustment is unavoidable.

## Design Constraints

### Durable decisions to preserve

- Unit naming is `wpc-nn`; tests for this unit use
  `[Trait("Category", "wpc-04")]`.
- Live Start `PidHash` uses the ETW ProcessStart timestamp as canonical.
- Refresh uses live process create times from the snapshot path.
- The sensor remains resolver-backed; do not add a sensor-owned PID instance map.
- ETW rundown remains rejected for Refresh.
- Runtime wire-in/removal is deferred to wpc-06.
- Manifest Stop metrics merge is deferred to wpc-05.
- Boot ETL coverage is deferred to wpc-07.
- Windows validation harness work is deferred to wpc-08.

### Event preservation

All enrichment is best-effort. A failure in SID parsing, account lookup, token
lookup, process open, path lookup, device-path translation, PEB read, protected
process access, or exited-process access must result in empty/default enrichment
fields, not a dropped lifecycle event and not an exception escaping the ETW
callback.

### No new identity store

Do not introduce a process-lifetime cache or PID instance map for this unit. A
bounded SID-to-name cache is allowed because it caches account-name translation,
not process identity.

## Implementation Notes

### Suggested internal enrichment model

Add an internal enrichment DTO and one testable enrichment method so unit tests do
not need live ETW events or administrator privileges.

Suggested shape:

```csharp
internal sealed class ProcessFieldEnrichment
{
    public string Name { get; init; }
    public string Path { get; init; }
    public string CommandLine { get; init; }
    public string User { get; init; }
}
```

Suggested testable core:

```csharp
internal ProcessFieldEnrichment EnrichStartFields(
    int pid,
    string etwImageFileName,
    string etwCommandLine,
    SecurityIdentifier sid,
    SidParseStatus sidStatus)
```

The actual `ProcessTraceData` callback should call
`ProcessTraceDataExtensions.TryGetUserSid` inside the ETW callback, then pass the
primitive SID/status plus ETW image and command-line strings into the testable
core. Keep any exact method names consistent with the existing
`WindowsProcessSensor` style.

If keeping helper logic in `WindowsProcessSensor.cs` makes the file too large,
move narrow helpers to `wintap/platform/windows/sensor/etw/helpers/` under the
existing Windows ETW helper namespace. Do not introduce a project-wide
abstraction layer.

### Minimal seams for tests

Add only the seams needed to test fallback selection and cache behavior:

- SID account lookup seam, defaulting to a `LookupAccountSid` implementation.
- Token user lookup seam by PID or process handle, defaulting to `OpenProcess` +
  `OpenProcessToken` / existing token identity logic.
- PEB command-line lookup seam, defaulting to the existing PEB read helper.
- full process image path lookup seam, defaulting to `OpenProcess` +
  `QueryFullProcessImageName`.
- device-path translation seam, defaulting to a local `QueryDosDevice`-based
  translation helper.

These seams may be constructor parameters like the existing resolver/emit/snapshot
seams, or small internal delegates on a helper object. Keep them internal and
optional. Production defaults must use Windows APIs and no new package.

If existing wpc-02 or wpc-03 tests need constructor updates because of new
optional seams, keep their behavior unchanged and preserve their existing traits.

### User enrichment

In the real ETW Start callback:

1. Call `data.TryGetUserSid(out SecurityIdentifier sid)` from the wpc-01 helper
   while still inside the callback; TraceEvent recycles event instances.
2. If status is `Extracted` and `sid != null`:
   - resolve the SID via the bounded SID cache;
   - cache key should be `sid.Value`;
   - lookup value should be `DOMAIN\\Name` when available;
   - if account lookup fails, use `sid.Value` rather than opening the process
     token just because account-name lookup failed.
3. If status is `NoSid` or `Malformed`, fall back to token lookup for the live
   process.
4. If token lookup fails, leave `User` empty.

Bounded cache requirements:

- Cache SID strings to resolved account names.
- Use a fixed maximum size; 1024 entries is acceptable unless the surrounding
  code has an existing constant pattern to follow.
- When capacity is exceeded, evict deterministically. A simple FIFO queue plus
  dictionary is sufficient; do not add a new caching package.
- Cache only successful SID/account translations. Do not permanently cache
  transient failures.

Use `LookupAccountSid` through P/Invoke. Prefer built-in framework types and
Win32 APIs already available in the repo. Do not add a NuGet dependency.

### Command-line enrichment

For Start events:

1. If the ETW command-line field is non-empty/non-whitespace, use it as
   `Process.CommandLine` and `Process.Arguments`.
2. If the ETW command-line field is empty, attempt a live PEB command-line read.
3. If the PEB read fails or returns empty, leave command line and arguments empty.

The existing wpc-03 snapshot code already includes a PEB command-line read helper.
Reuse or factor that logic rather than creating a second inconsistent parser.
PEB reads must remain best-effort and protected-process safe.

### Path and name enrichment

For Start events:

1. Try `QueryFullProcessImageName` on a live process handle for the PID.
2. If that fails and the ETW image value is a device path, translate it to a
   Win32 drive path using a local `QueryDosDevice` mapping helper.
3. If translation fails, use the ETW image value as the path.
4. Set process name from `Path.GetFileName(path)` when available; otherwise use
   the ETW image value.

Device-path translation should be best-effort and local to this unit. Example:
build a mapping from drive roots (`C:`, `D:`, ... from
`Environment.GetLogicalDrives()`) to their `QueryDosDevice` targets, then replace
the longest matching device prefix. Do not add a dependency for this.

### Integrating with Start emission

Update the ProcessStart handling path so the emitted Start message uses enriched
fields:

- `ProcessName` and `Process.Name`: enriched name.
- `ProcessPath` and `Process.Path`: enriched path.
- `Process.CommandLine` and `Process.Arguments`: enriched command line.
- `Process.User`: enriched user.

Keep these existing wpc-02 Start behaviors unchanged:

- constructor event time is the canonical ETW Start time UTC;
- `ActivityType = Start`;
- `PidHash = ProcessHash.GenPidHash(pid, etwStartTimeUtc.ToFileTimeUtc())` via
  the existing unchanged formula/seam;
- parent PID comes from the ETW payload;
- `ParentPidHash` remains empty unless already trivially resolved by existing
  code; do not add a PID map.

If the existing internal `EmitStart` method is used by tests, either preserve it
as a simple wrapper or add an overload that accepts already-enriched primitive
fields. Existing wpc-02 tests should continue to pass.

### Refresh and Stop behavior

Do not broaden this unit into a redesign of Refresh or Stop.

- Refresh already uses live snapshot helpers from wpc-03. It is acceptable to
  factor shared PEB/token/path helper code so Start and Refresh use the same
  implementation, but do not change Refresh create-time, ordering, synthetic
  seed, or dedup semantics.
- Stop identity must remain resolver-backed from wpc-02. Do not add manifest
  resource metrics, correlation windows, or metric counters in this unit.

### Error handling

- Catch enrichment exceptions per field and continue with empty/default values.
- Do not let enrichment exceptions escape ETW callbacks.
- Do not require live process access for Start emission. Short-lived and
  protected-process Starts must still emit from ETW payload data.
- Log per-field failures at Debug level unless surrounding code uses a more
  specific convention.

## Tests

Add wpc-04 tests under `tests.Wintap.Tests`. Tests must not start live ETW
sessions, must not require administrator privileges, and must not depend on the
host's current process list.

Use internal seams/DTOs to test pure selection behavior.

Required tests:

1. SID cache behavior:
   - two enrichments for the same extracted SID call the SID account lookup seam
     once and reuse the cached account name.
2. SID cache boundedness:
   - when more than the configured maximum entries are inserted, older entries
     are evicted deterministically or the cache size never exceeds the maximum.
3. User fallback matrix:
   - `Extracted` SID uses SID lookup/cache and does not call token fallback;
   - `NoSid` calls token fallback;
   - `Malformed` calls token fallback;
   - token fallback failure leaves `User` empty without dropping the event.
4. Command-line fallback matrix:
   - non-empty ETW command line wins and does not call the PEB fallback;
   - empty ETW command line calls PEB fallback;
   - empty/failing PEB fallback leaves command line empty.
5. Path fallback matrix:
   - `QueryFullProcessImageName` result wins;
   - when full path lookup fails, a device-path translation result is used;
   - when both fail, the ETW image value is retained.
6. Start message uses enriched fields:
   - emitted Start has enriched `ProcessName`, `ProcessPath`,
     `Process.CommandLine`, `Process.Arguments`, and `Process.User`;
   - Start `PidHash` still uses the ETW Start timestamp from wpc-02.
7. Enrichment exception safety:
   - exceptions from lookup seams do not prevent Start emission and produce
     empty/default enrichment fields.

Existing wpc-02 and wpc-03 tests should continue to pass after constructor seam
updates, but this unit's required verification filter is `Category=wpc-04`.

## Acceptance Criteria

1. `WindowsProcessSensor` Start handling calls the wpc-01 SID extraction helper
   inside the ETW callback and feeds SID/status into a testable enrichment path.
2. Extracted SIDs are resolved with `LookupAccountSid` and cached in a bounded
   SID-to-name cache.
3. `NoSid` and `Malformed` SID statuses fall back to live token lookup.
4. Token lookup failure leaves `User` empty and does not drop the event.
5. Non-empty ETW command line wins over PEB fallback.
6. Empty ETW command line attempts PEB fallback and still emits if PEB read fails.
7. Path enrichment prefers `QueryFullProcessImageName`, then device-path
   translation, then ETW image value.
8. Start messages use enriched name, path, command line, arguments, and user.
9. Live Start `PidHash` behavior from wpc-02 remains unchanged: ETW ProcessStart
   timestamp is canonical and the existing formula is used.
10. Refresh create-time, ordering, synthetic seed, and dedup behavior from wpc-03
    remain unchanged.
11. Stop resolver-backed identity behavior from wpc-02 remains unchanged.
12. No sensor-owned PID instance map is added.
13. No manifest Stop metrics, runtime wire-in/removal, boot ETL, or validation
    harness work is implemented in this unit.
14. Unit tests under `tests/Wintap.Tests` are tagged
    `[Trait("Category", "wpc-04")]` and cover the required cases.
15. No `WintapMessage` or `ProcessObject` schema changes are made.
16. No `PidHash` formula or `ProcessHash` behavior is changed.
17. TraceEvent remains at 3.1.23.
18. No new NuGet dependencies are added.
19. `dotnet build -c Release` succeeds, or the documented project-scoped fallback
    succeeds if the known solution-level website-project issue is encountered.
20. `dotnet test --filter "Category=wpc-04"` succeeds and selects the wpc-04
    tests, or the documented project-scoped fallback succeeds if the known
    solution-level website-project issue is encountered.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wpc-04"
```

If the repository-root commands still fail because `Wintap.sln` includes the
existing `Wintap-Workbench` website project (`MSB4249` under .NET SDK MSBuild),
run the closest project-scoped equivalents and document the deviation in
`developer_docs/audits/wpc-04-field-enrichment.md`:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wpc-04"
```

## Out of Scope

- Do not wire `WindowsProcessSensor` into `WindowsSubscriptionManager` yet.
- Do not delete `ProcessSensor.cs` or `KernelProcessSensor.cs` yet.
- Do not remove `System.Diagnostics.Eventing.Reader` usage yet.
- Do not change settings entries for `ProcessSensor` or `KernelProcessSensor`.
- Do not implement manifest-provider ProcessStop metric correlation.
- Do not add Stop CPU cycles, IO counts, commit charge, hard faults, or token
  elevation metrics.
- Do not add QA counter interval/shutdown logging beyond narrow counters/seams
  directly needed to test this unit's enrichment behavior.
- Do not add boot ETL / Global Logger handling.
- Do not replay ETL files.
- Do not change live Start create-time canonicalization from the ETW
  ProcessStart timestamp.
- Do not add per-Start `GetProcessTimes` lookups for live ETW Starts.
- Do not add a sensor-owned PID instance map.
- Do not change Refresh create-time, ordering, synthetic seed, or dedup semantics.
- Do not change Stop resolver-backed identity semantics.
- Do not add live ETW, admin-only, timing-sensitive, or reboot-dependent tests.
- Do not change Linux/macOS paths.
- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change `ProcessHash` or the `PidHash` formula.
- Do not upgrade TraceEvent or add any NuGet package.
