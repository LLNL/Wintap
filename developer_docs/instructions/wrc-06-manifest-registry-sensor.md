# wrc-06 Manifest-Only Registry Sensor Rewrite + Legacy Deletion

**Date:** 2026-08-25
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-25 (instruction approved as written:
Read events use `Data=""` / `DataType=NONE`; `Counter++` is included; the
legacy-parity unset `Registry.PID` behavior is retained)

## Purpose

Fourth production unit of `improve-windows-registry-collection` (plan:
`../Wintap-Analytics/wiki/work/improve-windows-registry-collection/implementation_plan.md`;
governing ADR:
`../Wintap-Analytics/wiki/decision/registry-provider-strategy.md`).

This is the unit the feature exists for: rewrite `RegistrySensor` as a
manifest-only, capture-mode sensor and delete the legacy machinery. Under
capture mode (wrc-04 enabler) the kernel puts the **full key path and the raw
value bytes in the event payload itself**, so the sensor needs no pointer
maps, no value caches, and no live-registry reads — it becomes a pure
function from event payload to `WintapMessage`. Frozen brief criteria 1, 3,
4, 5, and 7 are this unit's burden: legacy machinery deleted; full paths from
the event payload only; six-type decode including ExpandString plus
pre-change value on overwrite; typed numeric-ID dispatch with zero
`ToString()`/`Split` parsing; bounded memory (no caches of any kind).

Probe8 (Architect-run 2026-08-25, `C:\PUBLIC\wrc-poc\probe8.log`) grounds the
event contract used throughout: payload schemas, CreateKey path assembly
(0 unresolved across 364 CreateKeys), and the REG_NONE(0) first-write
encoding. Everything the Developer needs from it is reproduced below — no
POC access required.

## Working Branch

`develop-wrc`.

## Scope

- **Rewrite:** `wintap/platform/windows/sensor/etw/RegistrySensor.cs`
  (full replacement of the class body; class name, namespace, and the
  `EtwProviderCollector` base stay).
- **Modify (minimal hook):** `wintap/platform/windows/sensor/shared/EtwProviderSensor.cs`
  — one new `protected virtual` no-op method plus one call site (Implementation
  Note 5). Nothing else in that file changes.
- **Delete (four files):**
  - `wintap/platform/windows/sensor/etw/helpers/RegistryEventParsers.cs`
    (note: lives under `etw/helpers/`, not `etw/`) — `BaseEvent` string-split
    parsing and its five event classes
  - `wintap/platform/windows/sensor/etw/helpers/RegistryManager.cs` —
    the unbounded `RegParents`/`RegValueCache` dictionaries
  - `wintap/platform/windows/sensor/shared/models/RegistryEvent.cs` —
    the TOCTOU `GetData()` live re-read
  - `wintap/platform/windows/sensor/shared/models/KernelRegistryEvent.cs`

  Deletion safety re-verified 2026-08-25 by repo-wide search: `BaseEvent`'s
  only users are the deleted parser classes and `RegistryEvent`;
  `RegistryEvent` is constructed only in `RegistrySensor.cs` (lines 160,
  195 of the legacy file); `KernelRegistryEvent` has no users at all. If
  your own pre-deletion search finds a new reference that appeared since,
  **stop and raise to the Architect**.
- **New:** `tests/Wintap.Tests/RegistrySensorTests.cs`, all tests tagged
  `[Trait("Category", "wrc-06")]`.

Hard constraints for this feature, repeated in every wrc unit:

- **No new NuGet dependencies.** No `AllowUnsafeBlocks`, no project-file
  changes.
- **No scope beyond this instruction** — no changes to
  `RegistryPayloadDecoder.cs` (wrc-03), `RegistryCaptureEnabler.cs` (wrc-04),
  `WintapMessage.cs` (wrc-05), `App.config`/`Settings.settings`, or any
  Esper (.epl) / Serializer / parquet code.
- **No live ETW in unit tests**; no elevation-dependent tests; session names
  never `NT Kernel Logger`.
- **No live-registry reads anywhere in the sensor path.** Frozen criterion 3
  is scoped to **enrichment reads**: the sensor and its helpers must never
  read the registry to populate an event. (wrc-07 later adds a *write-only*
  capture-loss canary; a write is permitted there, reads never.)
- All tests carry the unit trait; audit required at
  `developer_docs/audits/wrc-06-manifest-registry-sensor.md`.

## Dependencies

**This unit cannot start until the wrc-03, wrc-04, and wrc-05 audits are
filed.** It consumes their interfaces exactly as approved:

- **wrc-03** (`developer_docs/instructions/wrc-03-payload-decode-core.md`,
  Approved 2026-08-25): `RegistryEventKind` (IDs 1–15 + `Unknown`) and
  `RegistryPayloadDecoder.KindFromEventId(int)` /
  `RegistryPayloadDecoder.DecodeRegValue(int regType, byte[] data)` in
  namespace `gov.llnl.wintap.platform.windows.collect.etw.helpers`.
  `DecodeRegValue` never throws; empty/null input returns `""` — which is
  exactly the first-write `PreviousData` encoding (see Note 3).
- **wrc-04** (`wrc-04-capture-enablement-engine.md`, Approved 2026-08-25):
  `RegistryCaptureEnabler` in
  `gov.llnl.wintap.platform.windows.collect.shared` with constructor
  `(TraceEventSession session, Guid providerId, ulong matchAnyKeyword,
  NativeEnableTraceEx2 nativeOverride = null)`, `EnableCapture()` (throws on
  guard/native-enable failure — fail loud), `Dispose()`, and the
  `DefaultKeywordMask` (`0x5300`) / `ReadKeywordMask` constants. This unit
  wires the enabler; it does **not** start the re-assert timer or touch
  `NotifyCaptureLossSuspected` (both wrc-07).
- **wrc-05** (`wrc-05-wintapmessage-registry-schema.md`, Approved
  2026-08-25): `DataTypeEnum` now `{ STRING, DWORD, BINARY, MULTI_SZ,
  EXPAND_SZ, QWORD, NONE }` (ordinals 0–6); `RegActivityObject` now carries
  `PreviousData` (string) and `PreviousDataType` (`DataTypeEnum`).
  **Per the wrc-05 approval stamp, this unit is obligated to set
  `PreviousDataType = NONE` explicitly wherever no previous value exists —
  never rely on the enum default (`STRING`, ordinal 0).** The same rule
  applies to `DataType` on kinds that carry no value data.
- ADR `registry-provider-strategy.md` — mechanism record items 3 and 5
  (first-write REG_NONE encoding; CreateKey path assembly) and the FINAL
  mask addendum.
- Read-only grounding: legacy `RegistrySensor.cs` (emission shape, lines
  252–286; `CollectRegistryRead` gate, line 62); `EtwProviderSensor.cs`
  (`Start()` lines 47–67); `DefaultHealthChecks.cs:165-168`
  (`IsQualifiedRegistry` — the egress predicate emitted paths must satisfy);
  `WindowsSubscriptionManager.cs:64-94` (a throwing sensor `Start()` is
  caught and logged per-sensor — the service survives, the sensor fails
  loudly).

## Implementation Notes

### 1. Event contract (probe8 payload schemas — normative)

The provider is `Microsoft-Windows-Kernel-Registry`
(`70EB4F03-C1DE-4F73-A051-33D13D5413BD`). Dispatch is on the **numeric event
ID** via `RegistryPayloadDecoder.KindFromEventId((int)obj.ID)` — never on
`OpcodeName`/`EventName` strings, never via `obj.ToString()` parsing
(frozen criterion 5). Payload schemas observed under capture (probe8, plus
probe1–6 for QueryValueKey):

| Kind (id) | Payload fields |
|---|---|
| CreateKey (1) | `BaseObject, KeyObject, Status, Disposition, BaseName, RelativeName` — **no `KeyName`** |
| DeleteKey (3) | `KeyObject, Status, KeyName` |
| SetValueKey (5) | `KeyObject, Status, Type, DataSize, KeyName, ValueName, CapturedData, PreviousDataType, PreviousDataSize, PreviousData` |
| DeleteValueKey (6) | `KeyObject, Status, KeyName, ValueName` |
| QueryValueKey (7) | `KeyObject, Status, InfoClass, DataSize, KeyName, ValueName, CapturedData` — **no `Type` field** |

Field access is typed `PayloadByName` only (the pattern the legacy
`RegSetValueEvent` already used): `obj.PayloadByName("KeyName") as string`,
`as byte[]` for `CapturedData`/`PreviousData`,
`Convert.ToInt32(...)` for `Type`/`PreviousDataType`. PID is
`obj.ProcessID` (the legacy path parsed it out of `obj.ToString()` — that
class of parsing is banned). Missing/null payload fields must degrade to
`""` / `Array.Empty<byte>()` / `0`, never throw.

**No pointer fields are ever used for path resolution.** `KeyObject` /
`BaseObject` are ignored. Probe8's spike-1b "46.2% KeyObject-map resolution"
stat measured the POC's obsolete map machinery, not this design — under
capture the path is always in the event (KeyName populated 106/106 value-op
events; CreateKey assembly 0 unresolved across 364). There are **no
dictionaries, caches, or maps of any kind in this sensor** (frozen
criterion 7): the rewrite's only instance state is the enabler reference and
the injected seams.

All kinds not listed above (OpenKey, CloseKey, QueryKey, Enumerate*, hive
family, `Unknown`, …) are **ignored** — no emission, no logging, no
bookkeeping. Under the mask they are structurally absent anyway (probe8);
ignoring them is defense in depth.

### 2. Path handling (pure helpers)

Kernel paths arrive rooted `\REGISTRY\...`
(e.g. `\REGISTRY\MACHINE\SOFTWARE\Waves Audio\MaxxAudio\General`). Today's
downstream contract — and the sensor-health `path_unqualified` check
(`DefaultHealthChecks.IsQualifiedRegistry`) — expects the legacy emitted
form: **lowercase, leading backslash trimmed**, i.e.
`registry\machine\software\waves audio\maxxaudio\general`.

```csharp
/// <summary>Kernel path -> emitted form: trim leading '\', lowercase.
/// Null/whitespace -> "".</summary>
internal static string NormalizeKeyPath(string kernelPath);

/// <summary>CreateKey (id 1) carries no KeyName; assemble from the event:
/// RelativeName starting with '\' is already absolute (probe8: 6/364) ->
/// use it alone; otherwise Join(BaseName, RelativeName) (probe8: 348/364,
/// BaseName populated under capture, e.g. '\REGISTRY\MACHINE'). Returns ""
/// (unqualified) when neither rule yields a rooted path.</summary>
internal static string AssembleCreateKeyPath(string baseName, string relativeName);
```

`AssembleCreateKeyPath` normative rules, in order:

1. `relativeName` starts with `\` → candidate = `relativeName`.
2. else `baseName` non-empty → candidate =
   `baseName.TrimEnd('\\') + "\\" + relativeName.TrimStart('\\')`
   (when `relativeName` is empty, candidate = `baseName` alone).
3. else → return `""`.

Then `NormalizeKeyPath(candidate)`.

**Qualification gate (all kinds):** after normalization, a path is emittable
only if it equals `"registry"` or starts with `"registry\"` (the exact
`IsQualifiedRegistry` predicate). An event whose path fails the gate — empty
`KeyName`, unassemblable CreateKey — is **dropped, not emitted**, with no
per-event logging (an empty-KeyName flood is precisely the capture-loss
condition; wrc-07's canary detects and logs it transition-only — a per-event
log line here would be a log storm). This closes the sweep-queue item
"ungated Registry CreateKey/DeleteKey/DeleteValue emit sites" by
construction: relative fragments can never egress.

### 3. Data-type mapping and value decode

REG-type-int → `DataTypeEnum` mapping lives **here** (wrc-03's decoder
deliberately does not reference WintapAPI):

```csharp
/// <summary>Native REG type -> DataTypeEnum. 0 (REG_NONE) and every
/// unrecognized type map EXPLICITLY to NONE — never the enum default
/// (wrc-05 approval obligation). Never throws.</summary>
internal static WintapMessage.DataTypeEnum MapDataType(int nativeType);
```

| nativeType | DataTypeEnum |
|---|---|
| 0 (REG_NONE) | `NONE` |
| 1 (REG_SZ) | `STRING` |
| 2 (REG_EXPAND_SZ) | `EXPAND_SZ` |
| 3 (REG_BINARY) | `BINARY` |
| 4 (REG_DWORD) | `DWORD` |
| 7 (REG_MULTI_SZ) | `MULTI_SZ` |
| 11 (REG_QWORD) | `QWORD` |
| anything else | `NONE` |

Value rendering is exclusively `RegistryPayloadDecoder.DecodeRegValue` —
this unit adds **no** decode logic. First-write semantics (probe8 raw dumps;
ADR mechanism item 3): the kernel encodes "no previous value" as
`PreviousDataType = 0` (REG_NONE), `PreviousDataSize = 0`,
`PreviousData = byte[0]` — which flows through
`MapDataType(0) == NONE` and `DecodeRegValue(0, byte[0]) == ""` with no
special-casing. The legacy always-`""` ExpandString bug is gone by
construction (decoder returns the stored literal, e.g. `%TEMP%\wrc`).

### 4. Emission contract (preserves today's downstream shape exactly)

Emission is the legacy `sendRegEventToEsper` construction contract
(legacy lines 252–286) minus the string-parsing: build
`new WintapMessage(obj.TimeStamp, pid, WintapMessage.MessageTypeEnum.Registry)`,
attach a `RegActivityObject`, set `ActivityType` **directly as an enum
value** (no `Enum.TryParse` of strings — the legacy TryParse-of-`""` throw
path is deleted), send via the emit seam (production:
`EventChannel.Send`). Per kind:

| Kind | ActivityType | Registry.Path | ValueName | Data | DataType | PreviousData | PreviousDataType |
|---|---|---|---|---|---|---|---|
| CreateKey (1) | `CreateKey` | assembled (Note 2) | `""` | `""` | `NONE` | `""` | `NONE` |
| DeleteKey (3) | `DeleteKey` | from `KeyName` | `""` | `""` | `NONE` | `""` | `NONE` |
| SetValueKey (5) | `Write` | from `KeyName` | payload `ValueName` | `DecodeRegValue(Type, CapturedData)` | `MapDataType(Type)` | `DecodeRegValue(PreviousDataType, PreviousData)` | `MapDataType(PreviousDataType)` |
| DeleteValueKey (6) | `DeleteValue` | from `KeyName` | payload `ValueName` | `""` | `NONE` | `""` | `NONE` |
| QueryValueKey (7) | `Read` — only when `CollectRegistryRead` | from `KeyName` | payload `ValueName` | `""` | `NONE` | `""` | `NONE` |

Contract notes (record in the audit):

- **Every `NONE` and `""` above is set explicitly** — never left to default
  (`DataType`/`PreviousDataType` default to `STRING`, `PreviousData` to
  `null`; the wrc-05 tests pin those defaults precisely so this obligation
  is visible).
- **Read carries no data by design.** QueryValueKey's payload has no `Type`
  field (schema above), so its `CapturedData` cannot be type-decoded, and
  criterion 3 forbids the legacy fallback (live read / value cache). Legacy
  Read `Data` came from exactly that machinery, which is deleted. Read
  emits path + value name only. *(Flagged Engineer decision — Architect
  confirms at approval; the alternative, a type-blind hex rendering of
  unverified CapturedData, was rejected as unverified and misleading.)*
- **`Registry.PID` is left unset (0), matching the legacy emission exactly**
  — legacy set only `Path`/`ValueName`/`Data`/`DataType`; the message-level
  `PID` from the constructor is the attribution downstream uses. Preserving
  the wart preserves the parquet contract. *(Architect may direct setting
  it at approval; do not decide unilaterally.)*
- **The `CollectRegistryRead` gate is checked in the event path** (legacy
  line 62 parity), via the injected gate seam (Note 6). Interim note: until
  wrc-07 wires `ReadKeywordMask`, QueryValueKey events do not arrive at all
  under the enabler's `DefaultKeywordMask` — the gate is defense in depth
  now, primary semantics after wrc-07.
- **Expected behavioral delta (document in the audit, do not "fix" back):**
  the legacy sensor almost certainly never emitted `CreateKey`/`DeleteKey`/
  `DeleteValue` at all — `sendRegEventToEsper` passed `dataType = ""` for
  them and `Enum.TryParse("")` fails, throwing `ArgumentException`
  (legacy lines 273–282) before `EventChannel.Send`, swallowed by the
  `Process_Event` catch. Likewise legacy `Write` threw for ExpandString /
  MultiString / QWord (`RegistryValueKind.ToString()` names don't parse to
  `EXPAND_SZ`/`MULTI_SZ`, and QWORD didn't exist). The rewrite makes all
  five activity types genuinely flow — expect a downstream Registry volume
  increase (probe8 scale: 364 CreateKey + 91 Write + 9 DeleteValue +
  6 DeleteKey per 15 s ≈ 31/s total).

### 5. Capture-enabler wire-in (session hook)

`EtwProviderCollector.Start()` holds the live `TraceEventSession` in a
private field; the enabler needs it at enable time. Add the minimal hook to
`EtwProviderSensor.cs`:

```csharp
/// <summary>Called with the live session immediately after EnableProvider,
/// before the listener thread starts. Default: no-op. Lets a subclass
/// attach session-level configuration (wrc: registry capture mode).</summary>
protected virtual void OnEtwSessionStarted(TraceEventSession session) { }
```

with exactly one call site in `Start()`, immediately after the existing
`traceEventSession.EnableProvider(...)` line (line 55). **No other change to
that file.**

`RegistrySensor` overrides it:

```csharp
protected override void OnEtwSessionStarted(TraceEventSession session)
{
    captureEnabler = new RegistryCaptureEnabler(session,
        ManifestRegistryProviderGuid, RegistryCaptureEnabler.DefaultKeywordMask);
    captureEnabler.EnableCapture();
    // Re-assert timer + CollectRegistryRead-selected mask: wrc-07.
}
```

- `ManifestRegistryProviderGuid` is a `private static readonly Guid` of the
  provider GUID string above; keep the string `EtwProviderId` assignment for
  the base class.
- **Interim mask:** the hardcoded `DefaultKeywordMask` (`0x5300`,
  probe8-verified) is deliberate; wrc-07 replaces it with
  `SelectKeywordMask(CollectRegistryRead)` and also sets
  `TraceEventFlags`/`EventLevel` on the TraceEvent-side enable. In this
  unit, leave `TraceEventFlags`/`EventLevel` at their defaults (today's
  behavior) — the enabler's disable-then-enable is authoritative
  immediately afterwards, within `Start()`, before the listener thread
  begins consuming.
- **Fail loud:** do not catch around `EnableCapture()`. A guard or native
  failure propagates out of `Start()`; `WindowsSubscriptionManager` catches
  per-sensor and logs `"... problem loading sensor: ..."` at Warn — the
  sensor fails at start (ADR Option A intent: break at sensor start, never
  silent data loss), the service survives.
- `Stop()`: override to `captureEnabler?.Dispose();` then `base.Stop()`.
- Do **not** start the re-assert timer (`StartReassertTimer`) — wrc-07.

### 6. Class shape and test seams (wpc/shc sensor-test pattern)

Tests reach `internal` members via the existing
`InternalsVisibleTo("Wintap.Tests")` and construct the sensor directly —
the established wpc pattern (`WindowsProcessSensorTests` constructs
`WindowsProcessSensor` with injected delegates). Seams are constructor
injection, minimal:

```csharp
internal class RegistrySensor : EtwProviderCollector
{
    public RegistrySensor() : this(null, null) { }

    /// <summary>Test seams: emit (production: EventChannel.Send) and the
    /// Read gate (production: Properties.Settings.Default.CollectRegistryRead).</summary>
    internal RegistrySensor(Action<WintapMessage> emit, Func<bool> collectRegistryRead)
    {
        SensorName = "Registry";
        EtwProviderId = "70EB4F03-C1DE-4F73-A051-33D13D5413BD";
        this.emit = emit ?? EventChannel.Send;
        this.collectRegistryRead = collectRegistryRead
            ?? (() => Properties.Settings.Default.CollectRegistryRead);
    }

    public override void Process_Event(TraceEvent obj);   // thin extraction shell
    protected override void OnEtwSessionStarted(TraceEventSession session);
    public override void Stop();

    // Per-kind handlers: primitives in, emit-or-drop out. These are the
    // unit-test surface AND the seam wrc-07's canary suppression hooks into
    // (HandleSetValue). Each builds per the Note 4 table, applies the Note 2
    // qualification gate, and calls emit exactly once or not at all.
    internal void HandleCreateKey(DateTime ts, int pid, string baseName, string relativeName);
    internal void HandleDeleteKey(DateTime ts, int pid, string keyName);
    internal void HandleDeleteValue(DateTime ts, int pid, string keyName, string valueName);
    internal void HandleSetValue(DateTime ts, int pid, string keyName, string valueName,
        int type, byte[] capturedData, int previousDataType, byte[] previousData);
    internal void HandleQueryValue(DateTime ts, int pid, string keyName, string valueName);

    internal static string NormalizeKeyPath(string kernelPath);
    internal static string AssembleCreateKeyPath(string baseName, string relativeName);
    internal static WintapMessage.DataTypeEnum MapDataType(int nativeType);
}
```

`Process_Event` responsibilities only: call `base.Process_Event(obj)`;
return unless `obj.ProviderGuid == ManifestRegistryProviderGuid`;
`Counter++`; classify via `KindFromEventId((int)obj.ID)`; extract the
kind's payload fields (Note 1); call the handler (`HandleQueryValue` only
when `collectRegistryRead()` — gate placement mirrors legacy line 62);
wrap in the legacy-style outer `try/catch` → `WintapLogger` Debug (per-event
fail-open). The `Counter++` line is a **deliberate addition**: nothing
increments `BaseWindowsSensor.Counter` today, so the base 10-second averager
always computes 0 events/s — and the approved wrc-07 live-verification
evidence contract reads exactly that averager for the production rate
measurement. One line makes the existing statistics real. *(Flagged
Engineer addition — Architect confirms at approval.)*

### 7. Legacy deletion mechanics

1. Delete the four files listed in Scope (`git rm`).
2. Remove the now-dead `using gov.llnl.wintap.platform.windows.collect.shared.models;`
   from `RegistrySensor.cs` (the `etw.helpers` using stays — the decoder
   lives there).
3. Verify: solution builds; repo-wide search for `RegistryManager`,
   `RegKeyEvent`, `RegSetValueEvent`, `RegDeleteValueEvent`,
   `RegDeleteKeyEvent`, `RegCloseEvent`, `BaseEvent`, `RegistryEvent`,
   `KernelRegistryEvent`, `RegParents`, `RegValueCache` finds no remaining
   source references. Record the search output in the audit.

### 8. Tests — `tests/Wintap.Tests/RegistrySensorTests.cs`

All `[Trait("Category", "wrc-06")]`; no ETW, no elevation, no live-registry
reads or writes, no `TraceEvent` construction (handlers take primitives —
that is the honest seam; `Process_Event`'s extraction shell is covered by
code review in the audit, not by faked TraceEvents). Constructing
`RegistrySensor` runs the `BaseWindowsSensor` ctor (10 s averager timer,
`CacheStatistics`): apply the standing test-fixture guidance —
`Env.SetDataRoot(...)` before any code path that first-touches
`WintapLogger` (shc-02/shc-03 precedent; see
`WindowsStateManagerDriveMapTests.cs:169` / `EventChannelHealthWireInTests.cs:168`)
— and always construct with an injected `emit` list so nothing reaches
`EventChannel`. Reuse the wrc-03 instruction's probe5 byte fixtures
verbatim; do not re-derive bytes.

1. **MapDataType matrix** (`[Theory]`): 0→`NONE`, 1→`STRING`,
   2→`EXPAND_SZ`, 3→`BINARY`, 4→`DWORD`, 7→`MULTI_SZ`, 11→`QWORD`;
   5, 6, 8, 12, 99, −1 → `NONE`.
2. **NormalizeKeyPath:** `\REGISTRY\MACHINE\SOFTWARE\Waves Audio\MaxxAudio\General`
   → `registry\machine\software\waves audio\maxxaudio\general`; input
   without leading `\` unchanged except lowercasing; null/empty/whitespace
   → `""`.
3. **CreateKey assembly, relative (probe8 348/364):**
   `AssembleCreateKeyPath("\\REGISTRY\\MACHINE", "Software\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager")`
   → `registry\machine\software\microsoft\windows\currentversion\capabilityaccessmanager`.
4. **CreateKey assembly, absolute RelativeName (probe8 6/364):**
   RelativeName `\Registry\Machine\Software\WrcAbs` with any BaseName
   (including empty) → `registry\machine\software\wrcabs`.
5. **CreateKey assembly edges:** empty RelativeName + BaseName
   `\REGISTRY\USER` → `registry\user`; empty BaseName + relative
   RelativeName → `""`; BaseName with trailing backslash joins without a
   doubled separator.
6. **HandleCreateKey emits the full contract row:** exactly one message;
   `MessageType == Registry`, `ActivityType == CreateKey`, ctor PID, Path
   from test 3's assembly, `ValueName == ""`, `Data == ""`,
   `DataType == NONE`, `PreviousData == ""`, `PreviousDataType == NONE`.
7. **Unqualified CreateKey drops:** empty BaseName + relative RelativeName
   → zero messages emitted.
8. **First write (probe8 raw dump, SZ):** `HandleSetValue` with type 1,
   the `hello-wrc` fixture bytes, `previousDataType 0`,
   `previousData byte[0]` → `ActivityType == Write`, `Data == "hello-wrc"`,
   `DataType == STRING`, `PreviousData == ""`,
   `PreviousDataType == NONE` — and assert `(int)PreviousDataType == 6`
   (explicitly `NONE`, not the default ordinal 0).
9. **Overwrite (probe8 raw dump, SZ):** `goodbye-wrc` bytes with previous
   `hello-wrc` bytes, both type 1 → `Data == "goodbye-wrc"`,
   `PreviousData == "hello-wrc"`, both types `STRING`.
10. **Six-type Write matrix** (`[Theory]`, wrc-03 overwrite fixtures): per
    type (1, 2, 3, 4, 7, 11) the emitted `Data`/`DataType` and
    `PreviousData`/`PreviousDataType` match the fixture expectations —
    including the EXPAND_SZ literal `%TEMP%\wrc` (never expanded) and the
    MULTI_SZ `alpha|beta|gamma` join (frozen criterion 4 at the sensor
    level).
11. **Empty-KeyName drop:** `HandleSetValue`/`HandleDeleteKey`/
    `HandleDeleteValue`/`HandleQueryValue` with null/empty `keyName` →
    zero messages (capture-loss shape; detection is wrc-07's canary).
12. **DeleteKey / DeleteValue contract rows:** paths normalized from
    KeyName; `DeleteValue` carries the payload ValueName, `DeleteKey`
    carries `""`; all four data fields explicit `""`/`NONE`.
13. **Read gate:** sensor constructed with `collectRegistryRead: () => false`
    — `Process_Event`-level gating is review-covered, so drive the gate the
    way the shell does: the test calls `HandleQueryValue` only per the gate
    seam's value and asserts the seam is what the shell consults (assert via
    a sensor whose gate seam flips a recorded flag when read). With the gate
    true, `HandleQueryValue` emits `Read` with `Data == ""`,
    `DataType == NONE`.
14. **Qualification predicate on everything emitted:** across all emitting
    tests, every message satisfies the health-check predicate: `Path` equals
    `"registry"` or starts with `"registry\"` (mirror of
    `DefaultHealthChecks.IsQualifiedRegistry`).
15. **No-enrichment by construction:** handlers fed a nonexistent key path
    (e.g. `\REGISTRY\MACHINE\SOFTWARE\WrcDoesNotExist\...`) emit fully
    populated messages — proving no live lookup backs any emitted field.
    (The audit additionally records `grep` evidence that the rewritten
    `RegistrySensor.cs` contains no `Microsoft.Win32` reference.)
16. **Emit-once discipline:** for each handler, a single call emits at most
    one message (count the injected list).

## Acceptance Criteria

1. `RegistrySensor.cs` is a full rewrite: numeric-ID dispatch via
   `RegistryPayloadDecoder.KindFromEventId`; **zero** `OpcodeName`/event-name
   string dispatch, `obj.ToString()` parsing, or `Split` parsing; **zero**
   dictionaries/caches/maps; **zero** `Microsoft.Win32` (registry API)
   references; `ActivityType` set as enum values, never parsed from strings.
2. The four legacy files are deleted; the repo-wide reference search of
   Note 7 is clean; the solution builds (deletion verified by build).
3. Emission matches the Note 4 table exactly, with every `NONE`/`""` set
   explicitly (never enum/field defaults); first-write events carry
   `PreviousDataType = NONE` + `PreviousData = ""` per the probe8 REG_NONE(0)
   finding.
4. CreateKey paths are assembled from `BaseName`/`RelativeName` including
   the absolute-RelativeName case; all other emitted kinds take their own
   `KeyName`; every emitted path satisfies the `IsQualifiedRegistry`
   predicate; unqualified events are dropped, never emitted.
5. The `CollectRegistryRead` gate is preserved on Read (seam-injected,
   default reads the setting).
6. The wrc-04 enabler is constructed in `OnEtwSessionStarted` with
   `DefaultKeywordMask` and `EnableCapture()` is called with no surrounding
   catch (fail-loud at sensor start); `Stop()` disposes it; the re-assert
   timer is NOT started (wrc-07).
7. The `EtwProviderSensor.cs` diff is exactly the virtual no-op hook plus
   one call site after `EnableProvider` (verify with `git diff`).
8. `Counter` is incremented once per provider event in `Process_Event`
   (the base averager's rate line becomes meaningful for wrc-07's
   evidence contract).
9. All wrc-06 tests pass; `Category~wrc` passes; the full test project
   passes (no regression).
10. Hard constraints upheld (no NuGet, no schema/decoder/enabler changes,
    no Esper/Serializer/parquet changes, no config changes); audit filed at
    `developer_docs/audits/wrc-06-manifest-registry-sensor.md` including:
    the deletion-search output, the `Microsoft.Win32` grep evidence, the
    behavioral-delta note (legacy throw-swallowed CreateKey/DeleteKey/
    DeleteValue and ExpandString/MultiString/QWord writes; expected volume
    increase), the Read-carries-no-data note, and the `Registry.PID`
    legacy-parity note.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wrc-06"
dotnet test --filter "Category~wrc"
```

If the repository-root commands fail with the known `MSB4249`
website-project issue, use the documented project-scoped fallbacks and note
the deviation in the audit:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-06"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wrc"
```

## Out of Scope

- Keyword-mask selection off `CollectRegistryRead`, `TraceEventFlags`/
  `EventLevel` wiring, the re-assert timer start, the capture-loss canary,
  and `NotifyCaptureLossSuspected` wiring (all wrc-07).
- Any change to `RegistryPayloadDecoder.cs` (wrc-03),
  `RegistryCaptureEnabler.cs` (wrc-04), or `WintapMessage.cs` (wrc-05).
- Any Esper (.epl), Serializer, `ParquetWriter`, or Wintappy change; any
  `App.config`/`Settings.settings` change; any other sensor or
  `EventChannel` change (the hook in `EtwProviderSensor.cs` is the sole
  shared-file touch).
- Live ETW or elevation in unit tests; faked `TraceEvent` objects; live
  registry reads or writes anywhere (the write-only canary arrives in
  wrc-07).
- Sweep-queue items adjacent to this code (dead `Serializer.Listen`,
  WintapAlert self-PID drop, etc.) — do not clean them up here.
- No new NuGet packages; no `AllowUnsafeBlocks`; no Linux/macOS code.
