# wrc-05 WintapMessage Registry Schema Extension

**Date:** 2026-08-25
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-25 (append-only ordinals confirmed,
including the unset-object `PreviousDataType` default-ordinal behavior pinned
by test; wrc-06 obligated to set `NONE` explicitly)

## Purpose

Third production unit of `improve-windows-registry-collection` (plan:
`../Wintap-Analytics/wiki/work/improve-windows-registry-collection/implementation_plan.md`).

The capture mode delivers two things the current schema cannot represent:
**REG_QWORD values** (decoded and byte-verified in the POC, but
`DataTypeEnum` has no `QWORD` member) and **pre-change values on
`SetValueKey` overwrites** (`PreviousData*` payload fields — a capability
the legacy sensor never had). The Architect's schema decision (2026-08-25)
is a **minimal, additive-only extension**; this unit implements it exactly.

Downstream impact statement (for the Architect's review and the audit):
Parquet and Esper/EPL consumers will see **new-but-optional columns** —
additive, not breaking. Existing `RegActivityObject` members, member order,
and existing `DataTypeEnum` member ordinals are unchanged. Per the frozen
brief Non-Goals, the Developer must **not** modify Esper (.epl), Serializer,
or parquet code in this unit.

## Working Branch

`develop-wrc`.

## Scope

Exactly one production file changed, plus tests:

- `shared/WintapAPI/WintapMessage.cs` — the two edits in Implementation
  Notes 1–2, nothing else.
- **New:** `tests/Wintap.Tests/WintapMessageRegistrySchemaTests.cs`, all
  tests tagged `[Trait("Category", "wrc-05")]`.

Hard constraints for this feature, repeated in every wrc unit:

- **Additive only.** No member of `WintapMessage` or any nested class may be
  removed, renamed, retyped, or reordered. New enum members append at the
  end so existing ordinals are stable.
- **No new NuGet dependencies.**
- **No scope beyond this instruction** — no Esper/Serializer/parquet
  changes, no sensor changes, no other WintapAPI types touched.
- All tests carry the unit trait; audit required at
  `developer_docs/audits/wrc-05-wintapmessage-registry-schema.md`.

## Dependencies

- Architect schema decision 2026-08-25 (recorded in the implementation plan
  and the wrc log entry): `DataTypeEnum` gains `QWORD` and `NONE`;
  `RegActivityObject` gains `PreviousData` (string, decoded identically to
  `Data`) and `PreviousDataType` (`DataTypeEnum`). Nothing else changes.
  `ActivityTypeEnum` already has every needed value (`CreateKey`,
  `DeleteKey`, `DeleteValue`, `Write`, `Read` — `WintapMessage.cs:68`).
- Current shape grounding: `DataTypeEnum` at `WintapMessage.cs:71` is
  `{ STRING, DWORD, BINARY, MULTI_SZ, EXPAND_SZ }`; `RegActivityObject` at
  lines 225–232 is `{ Path, DataType, ValueName, Data, PID }` extending
  `WintapBase`.
- No dependency on wrc-03/wrc-04; schedulable in any order relative to them.

## Implementation Notes

### 1. `DataTypeEnum` (line 71) — append two members

```csharp
public enum DataTypeEnum { STRING, DWORD, BINARY, MULTI_SZ, EXPAND_SZ, QWORD, NONE };
```

Existing ordinals preserved (`STRING=0 … EXPAND_SZ=4`); `QWORD=5`,
`NONE=6`. Append order is normative — downstream consumers may have
persisted ordinals.

Member semantics (document with a brief `///` comment on the enum or the
new members):

- `QWORD` — REG_QWORD (native type 11), representable for the first time.
- `NONE` — "no value data": deletes, key-only operations, unrecognized
  native types, and the **absent-previous-value encoding** on first writes
  (`PreviousDataType = NONE`, `PreviousData = ""`). This replaces the
  legacy sensor's throw-on-unparseable-type behavior
  (`RegistrySensor.cs:281`) as the future non-throwing fallback — the
  behavioral switch itself happens in wrc-06, not here.

### 2. `RegActivityObject` (lines 225–232) — add two properties

Add after `Data`, before `PID`:

```csharp
public string PreviousData { get; set; }
public DataTypeEnum PreviousDataType { get; set; }
```

- `PreviousData` — pre-change value on `SetValueKey` overwrites, decoded to
  string **identically to `Data`** (same decoder, wrc-03); empty string on
  first write / non-SetValue events.
- `PreviousDataType` — the pre-change value's data type; `NONE` when there
  is no previous value.

Default-value note (record in the audit, no code impact here): a
default-constructed `RegActivityObject` has `PreviousDataType == STRING`
(enum ordinal 0), exactly as `DataType` already defaults to `STRING` on
events that never set it. wrc-06 must set `PreviousDataType = NONE`
explicitly wherever no previous value exists; do NOT reorder the enum to
make `NONE` the zero default — ordinal stability wins (constraint above).

### 3. Serializer visibility (grounding, not a change)

Wintap's serialization paths discover payload members by reflection over
public instance properties; two new public read/write properties on
`RegActivityObject` and two appended enum members are the entire
serializer-visible surface of this unit. Do not add attributes, custom
serialization, or converters.

## Tests — `tests/Wintap.Tests/WintapMessageRegistrySchemaTests.cs`

All `[Trait("Category", "wrc-05")]`; follow the existing plain
construction-assertion pattern of `tests/Wintap.Tests/WintapMessageTests.cs`
(P1.1).

1. **Enum ordinal stability:** `(int)` of `STRING, DWORD, BINARY, MULTI_SZ,
   EXPAND_SZ, QWORD, NONE` == `0, 1, 2, 3, 4, 5, 6` respectively (guards
   accidental reordering — the additive-only contract).
2. **Enum string round-trip:** `Enum.Parse<WintapMessage.DataTypeEnum>` of
   `"QWORD"` and `"NONE"` (and case-insensitive `"qword"`, matching the
   legacy sensor's `Enum.TryParse(dataType, true, ...)` usage) round-trips
   through `ToString()`.
3. **Registry message round-trip:** construct
   `new WintapMessage(DateTime.UtcNow, 1234, MessageTypeEnum.Registry)` with
   a `RegActivityObject` setting all seven properties (`Path`, `DataType =
   QWORD`, `ValueName`, `Data = "0x1122334455667788"`, `PreviousData =
   "0x8877665544332211"`, `PreviousDataType = QWORD`, `PID`); assert every
   value reads back.
4. **First-write encoding:** `RegActivityObject` with `PreviousData = ""`
   and `PreviousDataType = NONE` reads back as set.
5. **Defaults documented:** a default-constructed `RegActivityObject` has
   `PreviousData == null` and `PreviousDataType == STRING` (pins the
   default-value note above so wrc-06's explicit-`NONE` obligation is
   visible in a test).
6. **Reflection visibility:** `typeof(WintapMessage.RegActivityObject)`
   exposes public instance properties named `PreviousData` (type `string`)
   and `PreviousDataType` (type `WintapMessage.DataTypeEnum`) with public
   getters and setters (what the reflection-based serializer enumerates).
7. **Additive-only guard:** `RegActivityObject` still exposes public
   properties `Path`, `DataType`, `ValueName`, `Data`, `PID` with unchanged
   types.

## Acceptance Criteria

1. `git diff --stat` shows exactly one production file changed
   (`shared/WintapAPI/WintapMessage.cs`) plus the new test file.
2. The `WintapMessage.cs` diff is exactly the two edits specified: two enum
   members appended at line 71 and two properties added to
   `RegActivityObject` — no other line of the file changes (doc comments on
   the new members excepted).
3. Enum ordinals 0–4 unchanged; `QWORD=5`, `NONE=6` (test 1).
4. Both new properties are public instance auto-properties, serializer
   visible by reflection (test 6).
5. No Esper (.epl), Serializer, parquet, or sensor file changed; no NuGet
   change; the WintapAPI project still builds for all targets (it is shared
   cross-platform — run the solution/project builds below).
6. All wrc-05 tests pass; the full test project passes (no regression).
7. Audit filed at
   `developer_docs/audits/wrc-05-wintapmessage-registry-schema.md`,
   including the downstream additive-columns statement and the
   `PreviousDataType` default note.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wrc-05"
```

If the repository-root commands fail with the known `MSB4249`
website-project issue, use the documented project-scoped fallbacks and note
the deviation in the audit:

```powershell
dotnet build "shared\WintapAPI\WintapAPI.csproj" -c Release
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-05"
```

## Out of Scope

- Emitting the new fields from any sensor (wrc-06); changing
  `RegistrySensor.cs`'s throw-on-parse-failure behavior (wrc-06).
- Any Esper EPL, `Serializer`, `ParquetWriter`, or Wintappy/DBT change —
  downstream consumers pick up the additive columns without code changes;
  assessing their use of `PreviousData` is analytics-side follow-up, not
  this feature.
- Any other `WintapMessage` member, enum, or nested class; `ActivityTypeEnum`
  is explicitly untouched (already sufficient).
- A nested previous-value object, hex/rendering convention changes, or any
  alternative schema shape — the Architect chose the minimal two-field
  extension; do not redesign it.
- No new NuGet packages; no Linux/macOS-specific code.
