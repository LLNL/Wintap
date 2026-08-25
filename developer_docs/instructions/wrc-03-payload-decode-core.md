# wrc-03 Registry Payload Decode Core

**Date:** 2026-08-25
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-25

## Purpose

First production unit of the `improve-windows-registry-collection` feature
(abbreviation `wrc`; plan:
`../Wintap-Analytics/wiki/work/improve-windows-registry-collection/implementation_plan.md`;
governing ADR:
`../Wintap-Analytics/wiki/decision/registry-provider-strategy.md`).

The new manifest-only registry sensor (wrc-06) dispatches on **numeric event
IDs** (TDH supplies no friendly names for `Microsoft-Windows-Kernel-Registry`
— events arrive as `EventID(n)`) and decodes raw registry value bytes from
the capture-mode `CapturedData`/`PreviousData` payload fields. This unit
builds that decode core as **pure, standalone code**: an event-ID
classification enum and a per-REG-type byte decoder, ported from the POC's
`DecodeRegValue`, which was verified byte-perfect against self-test writes of
all six interesting REG types (including the ExpandString case the legacy
sensor always decodes to `""`).

No ETW, no elevation, no schema dependency, no wiring. The wpc-01
pure-parser pattern: land the risk-free logic first with exhaustive fixtures.

## Working Branch

`develop-wrc` (already created).

## Scope

Exactly one new production file, plus tests:

- **New:** `wintap/platform/windows/sensor/etw/helpers/RegistryPayloadDecoder.cs`
- **New:** `tests/Wintap.Tests/RegistryPayloadDecoderTests.cs`, all tests
  tagged `[Trait("Category", "wrc-03")]`

**No existing file may change.** Placement grounding: non-registry helpers
already live in this folder (`BootProcessTraceHelper.cs`,
`ProcessTraceDataExtensions.cs`); use the folder's existing namespace
`gov.llnl.wintap.platform.windows.collect.etw.helpers` (see
`RegistryManager.cs:13`). The legacy files sharing the folder
(`RegistryEventParsers.cs`, `RegistryManager.cs`) are slated for deletion in
wrc-06 — do **not** touch or reference them.

Hard constraints for this feature, repeated in every wrc unit:

- **No new NuGet dependencies.**
- **No scope beyond this instruction** — no wiring, no refactors of
  neighboring code, no anticipatory abstractions.
- **No changes to `shared/WintapAPI/WintapMessage.cs`** (that is wrc-05) and
  **no changes to `RegistrySensor.cs` / `EtwProviderSensor.cs`** (wrc-06/04).
- **No Esper (.epl), Serializer, or parquet changes.**
- All tests carry the xUnit trait for this unit; an audit artifact is
  required at `developer_docs/audits/wrc-03-payload-decode-core.md`.

## Dependencies

- ADR `registry-provider-strategy.md` (Accepted) — mechanism and evidence.
- Retroactive spike record
  `developer_docs/instructions/wrc-02-capture-filter-spike.md` — source of
  the verified decode semantics and the probe5 fixtures reproduced below.
  The Developer does NOT need access to `C:\PUBLIC\wrc-poc\`; everything
  required is in this instruction.
- No dependency on wrc-04/wrc-05; this unit is deliberately schedulable
  first.

## Implementation Notes

### 1. Event-ID classification

TDH gives this provider no event names, so the sensor keys off numeric IDs.
The ID→operation mapping (from the provider manifest, confirmed against live
events in the POC):

```csharp
/// <summary>
/// Event IDs of Microsoft-Windows-Kernel-Registry
/// (70EB4F03-C1DE-4F73-A051-33D13D5413BD). TDH supplies no friendly names
/// for this provider (events arrive as "EventID(n)"), so the sensor
/// dispatches on the numeric ID. Values match the provider manifest.
/// </summary>
internal enum RegistryEventKind
{
    Unknown = 0,
    CreateKey = 1,
    OpenKey = 2,
    DeleteKey = 3,
    QueryKey = 4,
    SetValueKey = 5,
    DeleteValueKey = 6,
    QueryValueKey = 7,
    EnumerateKey = 8,
    EnumerateValueKey = 9,
    QueryMultipleValueKey = 10,
    SetInformationKey = 11,
    FlushKey = 12,
    CloseKey = 13,
    QuerySecurityKey = 14,
    SetSecurityKey = 15,
}
```

```csharp
internal static class RegistryPayloadDecoder
{
    /// <summary>IDs 1–15 map to their kind; everything else (including the
    /// hive-notification family observed as raw IDs in the POC) returns
    /// Unknown. Never throws.</summary>
    internal static RegistryEventKind KindFromEventId(int eventId);

    /// <summary>Decodes a raw registry value payload per REG type.
    /// Normative behavior in the table below. Never throws.</summary>
    internal static string DecodeRegValue(int regType, byte[] data);
}
```

`KindFromEventId`: return `(RegistryEventKind)eventId` when `eventId` is in
[1, 15], else `RegistryEventKind.Unknown`. Do not build a dictionary; a
range check suffices and is allocation-free on the hot path.

### 2. `DecodeRegValue` — normative decode table

Port of the POC decoder verified byte-perfect in probe3/probe5, with two
deliberate production adaptations called out below. REG type constants are
the Windows registry native types carried in the events' `Type` /
`PreviousDataType` fields.

| regType | Meaning | Decode |
|---|---|---|
| any | `data` null or length 0 | return `string.Empty` |
| 1 | REG_SZ | `Encoding.Unicode.GetString(data).TrimEnd('\0')` |
| 2 | REG_EXPAND_SZ | **identical to REG_SZ** — decode the stored string; **never** expand environment variables (the legacy sensor's expansion attempt is the source of its always-`""` ExpandString bug and would be wrong anyway: expansion context belongs to the writing process, not Wintap) |
| 3 | REG_BINARY | `BitConverter.ToString(data)` (hex-dash, e.g. `DE-AD-BE-EF`) |
| 4 | REG_DWORD | if `data.Length >= 4`: `"0x" + BitConverter.ToUInt32(data, 0).ToString("X8")`; else fall through to the unknown-type rendering (truncation guard) |
| 7 | REG_MULTI_SZ | `string.Join("|", Encoding.Unicode.GetString(data).Split('\0').Where(s => s.Length > 0))` |
| 11 | REG_QWORD | if `data.Length >= 8`: `"0x" + BitConverter.ToUInt64(data, 0).ToString("X16")`; else fall through to the unknown-type rendering |
| anything else | unknown/other REG type | `$"(type {regType}) " + BitConverter.ToString(data)` |

Facts the decode relies on (POC-verified): strings arrive **UTF-16LE with
terminating NULs included in `DataSize`** (`hello-wrc` = `byte[20]`);
MULTI_SZ arrives as NUL-separated UTF-16LE with a trailing double-NUL;
DWORD/QWORD arrive little-endian.

Deliberate adaptations from the POC (which was console-display code):

1. **Empty input returns `string.Empty`, not the POC's `"(no data)"`.**
   Downstream (wrc-06) represents "no previous value" (first write:
   `PreviousDataType = 0`, `PreviousData = byte[0]`) as an empty string plus
   the `NONE` data type — a display placeholder must not leak into emitted
   data.
2. **Explicit length guards replace the POC's catch-all `try/catch`.** A
   truncated DWORD/QWORD payload deterministically falls back to the
   unknown-type hex rendering instead of surfacing a locale-dependent
   exception message. The method must never throw for any input; if you keep
   a defensive outer `try/catch`, its fallback must be the unknown-type
   rendering, not an exception message string.

### 3. Test fixtures — verbatim probe5 raw event dumps

These bytes were captured live from the kernel with capture mode on
(probe5, 2026-08-24) and verified byte-perfect against known self-test
writes. Use them **verbatim** as the test fixtures; do not re-derive them.

Initial writes (first `SetValueKey` per name; `PreviousDataType = 0`,
`PreviousDataSize = 0`, `PreviousData = byte[0]`):

| Type | DataSize | CapturedData bytes | Expected decode |
|---|---|---|---|
| 1 (SZ) | 20 | `68-00-65-00-6C-00-6C-00-6F-00-2D-00-77-00-72-00-63-00-00-00` | `hello-wrc` |
| 2 (EXPAND_SZ) | 22 | `25-00-54-00-45-00-4D-00-50-00-25-00-5C-00-77-00-72-00-63-00-00-00` | `%TEMP%\wrc` |
| 4 (DWORD) | 4 | `78-56-34-12` | `0x12345678` |
| 11 (QWORD) | 8 | `88-77-66-55-44-33-22-11` | `0x1122334455667788` |
| 3 (BINARY) | 4 | `DE-AD-BE-EF` | `DE-AD-BE-EF` |
| 7 (MULTI_SZ) | 36 | `61-00-6C-00-70-00-68-00-61-00-00-00-62-00-65-00-74-00-61-00-00-00-67-00-61-00-6D-00-6D-00-61-00` | `alpha|beta|gamma` |

Overwrites (second `SetValueKey` per name; `PreviousData*` carries the
first write's bytes — decode both sides):

| Type | CapturedData bytes | Expected decode | PreviousData bytes | Expected previous decode |
|---|---|---|---|---|
| 1 | `67-00-6F-00-6F-00-64-00-62-00-79-00-65-00-2D-00-77-00-72-00-63-00-00-00` (24) | `goodbye-wrc` | the SZ row above (20) | `hello-wrc` |
| 2 | `25-00-54-00-4D-00-50-00-25-00-5C-00-77-00-72-00-63-00-32-00-00-00` (22) | `%TMP%\wrc2` | the EXPAND_SZ row above (22) | `%TEMP%\wrc` |
| 4 | `21-43-65-87` | `0x87654321` | `78-56-34-12` | `0x12345678` |
| 11 | `11-22-33-44-55-66-77-88` | `0x8877665544332211` | `88-77-66-55-44-33-22-11` | `0x1122334455667788` |
| 3 | `CA-FE-BA-BE` | `CA-FE-BA-BE` | `DE-AD-BE-EF` | `DE-AD-BE-EF` |
| 7 | `64-00-65-00-6C-00-74-00-61-00-00-00-65-00-70-00-73-00-69-00-6C-00-6F-00-6E-00-00-00-00-00` (30) | `delta|epsilon` | the MULTI_SZ row above (36) | `alpha|beta|gamma` |

Note the MULTI_SZ overwrite fixture ends in `00-00-00-00` (terminator NUL +
list-terminating double-NUL): the empty-segment filter must swallow those,
yielding exactly `delta|epsilon`.

Example key path observed on all these events (for any test that wants a
realistic `KeyName`):
`\REGISTRY\USER\S-1-5-21-823518204-879983540-682003330-1134\Software\WrcPoc`.

### 4. Tests — `tests/Wintap.Tests/RegistryPayloadDecoderTests.cs`

All `[Trait("Category", "wrc-03")]`; none require elevation, ETW, the
registry, or any environment state. Reach the `internal` types via the
existing `InternalsVisibleTo("Wintap.Tests")`.

1. **Six-type decode matrix, initial writes** (`[Theory]`): the six initial
   fixture rows decode to their expected strings.
2. **Six-type decode matrix, overwrites** (`[Theory]`): the six overwrite
   `CapturedData` rows decode to their expected strings.
3. **PreviousData decode matrix** (`[Theory]`): the six overwrite
   `PreviousData` rows decode to the initial writes' expected strings
   (create-vs-overwrite evidence, per frozen criterion 4).
4. **Empty/null:** `DecodeRegValue(t, null)` and `DecodeRegValue(t, new byte[0])`
   return `""` for every type in {1, 2, 3, 4, 7, 11, 0, 99}.
5. **Truncation guards:** type 4 with `byte[2] { 0x78, 0x56 }` →
   `(type 4) 78-56`; type 11 with `byte[4] { 0x88, 0x77, 0x66, 0x55 }` →
   `(type 11) 88-77-66-55`. No exception.
6. **Unknown type fallback:** type 5 (REG_DWORD_BIG_ENDIAN, not decoded)
   with `DE-AD` → `(type 5) DE-AD`; type 0 with `01` → `(type 0) 01`.
7. **ExpandString is not expanded:** type 2 fixture decodes to the literal
   `%TEMP%\wrc` — assert the result contains `%TEMP%` (regression pin on the
   legacy bug class).
8. **SZ without terminating NUL:** type 1 with `68-00-69-00` (`hi`, no NUL)
   → `hi` (TrimEnd is a no-op; decoder must not require the NUL).
9. **MULTI_SZ single string:** type 7 with `61-00-00-00-00-00` → `a`.
10. **Event-ID dispatch matrix** (`[Theory]`): IDs 1–15 map to their named
    kinds (spot-assert all 15); IDs 0, 16, 999, and −1 map to `Unknown`.

## Acceptance Criteria

1. `RegistryPayloadDecoder.cs` exists at the specified path/namespace with
   exactly the two members specified (enum + static class with
   `KindFromEventId`/`DecodeRegValue`); no other production file changed
   (verify with `git diff --stat`).
2. `DecodeRegValue` implements the normative table exactly, including the
   two adaptations (empty → `""`; deterministic truncation fallback), and
   never throws for any input exercised by the tests.
3. All twelve probe5 fixture rows (six initial, six overwrite, six previous)
   decode byte-for-byte to the expected strings, including the ExpandString
   literal and the MULTI_SZ trailing-NUL handling.
4. `KindFromEventId` covers IDs 1–15 and returns `Unknown` otherwise,
   allocation-free.
5. No ETW, registry, elevation, or environment dependency anywhere in the
   unit; no reference to `RegistryEventParsers.cs`, `RegistryManager.cs`,
   `WintapMessage`, or any sensor class.
6. All wrc-03 tests pass; the full test project passes (no regression).
7. Hard constraints upheld (no NuGet, no schema change, no Esper/parquet
   change); audit filed at
   `developer_docs/audits/wrc-03-payload-decode-core.md`.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wrc-03"
```

If the repository-root commands fail with the known `MSB4249`
website-project issue, use the documented project-scoped fallbacks and note
the deviation in the audit:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-03"
```

## Out of Scope

- Any change to `RegistrySensor.cs`, `EtwProviderSensor.cs`,
  `RegistryEventParsers.cs`, `RegistryManager.cs`,
  `shared/models/RegistryEvent.cs`, `shared/models/KernelRegistryEvent.cs`
  (deletion of the legacy files is wrc-06).
- Any change to `shared/WintapAPI/WintapMessage.cs` (wrc-05) — the decoder
  returns plain strings/ints and must not reference WintapAPI enums.
- ETW enablement, P/Invoke, session handling (wrc-04); keyword masks,
  canary, live verification (wrc-07).
- Mapping REG type ints to `DataTypeEnum`, activity-type mapping, or any
  WintapMessage emission (wrc-06).
- Live ETW tests of any kind; tests requiring elevation.
