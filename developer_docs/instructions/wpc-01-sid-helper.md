# wpc-01 SID Extraction Helper

**Date:** 2026-08-17
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-17

## Purpose

Port the validated classic-kernel-ETW process UserSID extraction helper into
Wintap so later Windows process-collection units can populate process user
identity from `ProcessTraceData` without using the Windows Security event log.

This is the first unit of the `improve-windows-process-collection` feature. It
only introduces the helper and its unit tests. It does not wire the helper into
any sensor yet.

## Scope

Create a Windows ETW helper that extracts the skipped `UserSID` field from
classic NT Kernel Logger process events represented by TraceEvent's
`Microsoft.Diagnostics.Tracing.Parsers.Kernel.ProcessTraceData`.

Implement this unit in the `wintap` repository:

- Add production helper file:
  `wintap/platform/windows/sensor/etw/helpers/ProcessTraceDataExtensions.cs`
- Namespace:
  `gov.llnl.wintap.platform.windows.collect.etw.helpers`
- Port the validated logic from:
  `C:\PUBLIC\sid-extraction-test\ProcessTraceDataExtensions.cs`
- Add unit tests under `tests/Wintap.Tests/`, tagged:
  `[Trait("Category", "wpc-01")]`
- If needed for test compilation, update `tests/Wintap.Tests/Wintap.Tests.csproj`
  to reference `..\..\wintap\Wintap.csproj`. Do not add package references.

## Dependencies

- Existing project rules: `CLAUDE.md`
- Existing test project: `tests/Wintap.Tests/Wintap.Tests.csproj`
- Validated source helper: `C:\PUBLIC\sid-extraction-test\ProcessTraceDataExtensions.cs`
- TraceEvent package already shipped by Wintap; keep the existing version.

Hard constraints for this feature, repeated here so they are not missed:

- **No WintapMessage/ProcessObject schema changes.**
- **No PidHash formula changes.**
- **TraceEvent stays at 3.1.23.**
- **No new NuGet dependencies.**

## Implementation Notes

### Public API to add

Add the enum and extension class below in namespace
`gov.llnl.wintap.platform.windows.collect.etw.helpers`:

- `public enum SidParseStatus`
  - `Extracted` — a well-formed SID was extracted from the event payload.
  - `NoSid` — the event carried the 4-byte null-SID marker.
  - `Malformed` — the payload was too short or inconsistent with the expected
    layout.
- `public static class ProcessTraceDataExtensions`
  - `public static SidParseStatus TryGetUserSid(this ProcessTraceData data, out SecurityIdentifier? sid)`
  - `public static SidParseStatus TryGetUserSid(this ProcessTraceData data, out SecurityIdentifier? sid, out int postSidOffset)`

The extension overloads must preserve the validated behavior from the POC:

- Read `byte[] payload = data.EventData()`.
- Read `int ptrSize = data.PointerSize` per event.
- Read `int version = data.Version` per event.
- Return `Extracted`, `NoSid`, or `Malformed`; do not throw for malformed event
  payloads.
- Set `sid = null` unless extraction succeeds.
- Set `postSidOffset` to one byte past the SID field when the field is parsed as
  `Extracted` or `NoSid`; leave it `-1` for malformed payloads.
- Include a summary comment warning that TraceEvent recycles event instances, so
  the extension must be called inside the event callback.

### Required parsing rules

Use the same layout math as the validated helper. Define:

```text
HostOffset(bytes, nPtrs) = bytes + (PointerSize - 4) * nPtrs
```

The SID field offset is:

```text
version >= 4: HostOffset(28, 2)
version >= 3: HostOffset(24, 2)
version <  3: HostOffset(20, 1)
```

At `sidOffset`:

- If fewer than 4 payload bytes remain, return `Malformed`.
- If `BitConverter.ToInt32(payload, sidOffset) == 0`, this is the null-SID
  marker. Set `postSidOffset = sidOffset + 4` and return `NoSid`.
- Otherwise, skip the `TOKEN_USER` header: two pointers plus its fixed 8-byte
  base. In the POC this is:

```text
sidStart = sidOffset + HostOffset(8, 2)
```

Then parse the SID itself:

- Require at least 8 bytes from `sidStart`; otherwise return `Malformed`.
- Read `SubAuthorityCount` from `payload[sidStart + 1]`.
- Compute `sidLength = 8 + 4 * SubAuthorityCount`.
- If `SubAuthorityCount > 15`, return `Malformed`.
- If the payload is shorter than `sidStart + sidLength`, return `Malformed`.
- Construct `new SecurityIdentifier(payload, sidStart)`.
- If `SecurityIdentifier` throws `ArgumentException`, return `Malformed`.
- On success, set `postSidOffset = sidStart + sidLength` and return
  `Extracted`.

### Testability seam

`ProcessTraceData` is not convenient to instantiate from unit tests. To keep
this unit testable without live ETW or new dependencies, add an internal pure
parser method that the public extension calls, for example:

```csharp
internal static SidParseStatus TryGetUserSidFromPayload(
    byte[] payload,
    int pointerSize,
    int version,
    out SecurityIdentifier? sid,
    out int postSidOffset)
```

The public extension methods must delegate to this method after reading
`EventData()`, `PointerSize`, and `Version` from `ProcessTraceData`. The pure
method must contain the same parsing logic as the validated POC; this is a
test seam, not a new architecture.

If the test project cannot access the internal method, either:

- add `[assembly: InternalsVisibleTo("Wintap.Tests")]` in an appropriate
  production assembly file, or
- make the payload parser public but clearly document it as intended for tests
  and parser reuse.

Prefer `InternalsVisibleTo` if it fits the existing project style.

### Unit tests to add

Add a test class such as `tests/Wintap.Tests/WindowsProcessSidExtractionTests.cs`.
Every test must include:

```csharp
[Trait("Category", "wpc-01")]
```

Use synthetic byte-array payload fixtures. Do not start ETW sessions. Do not
require administrator privileges.

The fixtures should build payloads from a known SID, e.g. `S-1-5-18`, by using
`SecurityIdentifier.GetBinaryForm(...)`. Place the binary SID at the computed
`sidStart` after a nonzero marker / `TOKEN_USER` header area. The header bytes
do not need to represent a real token pointer; only the offset and SID bytes are
under test.

Required test coverage:

1. `Extracted` for version 3, pointer size 4.
2. `Extracted` for version 3, pointer size 8.
3. `Extracted` for version 4, pointer size 4.
4. `Extracted` for version 4, pointer size 8.
5. `NoSid` when the 4-byte marker at `sidOffset` is zero.
6. `Malformed` when the payload is shorter than `sidOffset + 4`.
7. `Malformed` when the marker is nonzero but the payload is too short for the
   `TOKEN_USER` skip plus SID header.
8. `Malformed` when `SubAuthorityCount > 15`.
9. `Malformed` when `SubAuthorityCount` implies a SID length longer than the
   payload.

For successful extraction tests, assert:

- status is `SidParseStatus.Extracted`;
- `sid` is not null;
- `sid.Value` or `sid.ToString()` matches the expected SID string;
- `postSidOffset` equals `sidStart + sidLength`.

For `NoSid`, assert:

- status is `SidParseStatus.NoSid`;
- `sid` is null;
- `postSidOffset == sidOffset + 4`.

For `Malformed`, assert:

- status is `SidParseStatus.Malformed`;
- `sid` is null;
- `postSidOffset == -1`.

## Acceptance Criteria

1. `wintap/platform/windows/sensor/etw/helpers/ProcessTraceDataExtensions.cs`
   exists with the namespace and public API specified above.
2. The helper preserves the validated POC parsing behavior for V3/V4 process
   payload layouts, 4-byte and 8-byte pointer sizes, null-SID markers, and
   malformed payload guards.
3. The helper compiles against the TraceEvent version already used by Wintap;
   no TraceEvent upgrade is made.
4. No new NuGet dependencies are added anywhere in the solution.
5. No `WintapMessage` or `ProcessObject` schema is changed.
6. No `PidHash` formula or `ProcessHash` behavior is changed.
7. Unit tests exist under `tests/Wintap.Tests/`, are tagged
   `[Trait("Category", "wpc-01")]`, and cover all required cases listed above.
8. `dotnet build -c Release` succeeds.
9. `dotnet test --filter "Category=wpc-01"` succeeds and selects the wpc-01
   tests.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wpc-01"
```

## Out of Scope

- Do not wire this helper into `ProcessSensor`, `KernelProcessSensor`, or a new
  `WindowsProcessSensor` in this unit.
- Do not add SID-to-account-name lookup, SID caching, or `LookupAccountSid`.
- Do not add `OpenProcessToken` fallback behavior.
- Do not add command-line extraction or PEB-reading logic.
- Do not change process Start/Stop/Refresh event emission.
- Do not change `WintapMessage`, `ProcessObject`, Esper EPL, DuckDB schema, or
  downstream analytics models.
- Do not change the `PidHash` formula or any `ProcessHash` code.
- Do not update TraceEvent or add any NuGet package.
- Do not introduce live ETW tests, admin/elevation-dependent tests, or boot ETL
  replay tests.
