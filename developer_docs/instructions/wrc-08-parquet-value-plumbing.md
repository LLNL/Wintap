# wrc-08 Parquet Value Plumbing (Registry EPL + Serializer)

**Date:** 2026-08-25
**Status:** Approved & Complete
**Author:** Engineer
**Architect Approval:** Approved 2026-08-25 — approval conveyed at Developer handoff 2026-08-25 (Architect); the formal stamp step was leapfrogged by the handoff, so this header is the faithful record of that sequence, not backdating.
**Completion:** Confirmed by Architect 2026-08-25 (feature accepted against the frozen brief criteria same day; audit filed at `developer_docs/audits/wrc-08-parquet-value-plumbing.md`, Status: Complete — 9/9 wrc-08 tests, 134/134 `Category~wrc`, 299/299 full suite). The group-by aggregation-granularity consequence (Implementation Note 1) stands **Architect-confirmed-by-acceptance** (wrc-08 live smoke test verdict, verbatim: "smoke test looks fantastic").

## Purpose

Final unit of `improve-windows-registry-collection` (plan:
`../Wintap-Analytics/wiki/work/improve-windows-registry-collection/implementation_plan.md`),
rolled in by Architect decision 2026-08-25 as a **scoped exception** to the
brief's Non-Goals (Esper/parquet), per the shc-03 widening precedent:
realize the value now rather than defer it to a follow-on feature. This is
**not** a criteria amendment — the frozen brief criteria are unchanged and
already satisfiable; this unit carries the captured values through to the
output format users actually query.

The gap, verified 2026-08-25:

- The wrc-06 sensor populates all four value fields on every emitted
  Registry `WintapMessage` — `Data`, `DataType`, `PreviousData`,
  `PreviousDataType` (`RegistrySensor.cs:189-194`; explicit
  `NONE`/`string.Empty` on non-value events per the wrc-05/wrc-06 approval
  stamps, `RegistrySensor.cs:284-287, 306-308`).
- `wintap/core/etl/esper/registry.epl` selects `registry.data` and
  `registry.dataType` but neither previous field.
- `RegistrySerializer.HandleSensorEvent` writes only `Reg_Data`; it never
  reads the `dataType` field the EPL already selects.
- Net effect: the Parquet output has **no `Reg_DataType`, no
  `Reg_PreviousData`, no `Reg_PreviousDataType`** — the feature's headline
  capability (pre-change values, QWORD/NONE typing) is invisible downstream.

## Working Branch

`develop-wrc`.

## Current State (grounded 2026-08-25 — reproduce-before-change baseline)

### `wintap/core/etl/esper/registry.epl` — entire current statement

```sql
select istream
  "Registry" as protocol,
  registry.path as path,
  registry.dataType as dataType,
  registry.valueName as valueName,
  registry.data as data,
  PidHash,
  ProcessName,
  PID,
  CAST(ActivityType, string) as activityType,
  min(eventTime) as firstSeen,
  max(eventTime) as lastSeen,
  count(*) as eventCount,
  'Registry' as MessageType,
  AgentId
from WintapMessage(CAST(messageType, string) = 'Registry').win:time_batch(10 sec) as msg
group by registry.path, registry.dataType, registry.valueName, registry.data, PidHash, PID, activityType, ProcessName
having count(*) > 0
```

Observed anomaly, do **not** imitate: `AgentId` is selected without being
grouped (works today under NEsper's leniency). The new fields you add MUST
appear in both the select list and the `group by` clause.

### `RegistrySerializer.HandleSensorEvent` — current column list (insertion order)

`AgentId, ActivityType, ProcessName, Reg_Data, EventCount, FirstSeenMs,
LastSeenMs, PID, PidHash, HostHame, Reg_Path, Reg_Value, Reg_Id_Hash,
MessageType, EventTime` (15 columns; `RegistrySerializer.cs:36-51`).

Sibling column-naming convention confirmed: registry payload columns carry
the `Reg_` prefix (`Reg_Data`, `Reg_Path`, `Reg_Value`, `Reg_Id_Hash`);
FileSerializer uses `File_` the same way. New columns therefore are
**`Reg_DataType`, `Reg_PreviousData`, `Reg_PreviousDataType`**.

Enum-writing convention confirmed: serializers write enum-typed values as
**enum-name strings**, never ordinals — `ActivityType` is
`CAST(ActivityType, string)` in the EPL then `.ToString()` in the
serializer; an un-cast enum-typed EventBean field (`dataType` today) comes
back as the boxed `DataTypeEnum`, whose `.ToString()` is also the name.
Parquet values for the new type columns are therefore the strings
`"STRING" | "DWORD" | "BINARY" | "MULTI_SZ" | "EXPAND_SZ" | "QWORD" | "NONE"`.
This matters downstream: queries filter on names, not numbers.

Preserved warts (existing-column contract — do NOT fix in this unit): the
`HostHame` column-name typo; `FirstSeenMs`/`LastSeenMs` naming (differs from
FileSerializer's `FirstSeen`/`LastSeen`); `EventTime` from
`DateTime.FromFileTimeUtc(firstSeen)` (differs from FileSerializer's
`GetUnixNowTime()`); `Registry.PID` unset upstream (wrc-06 approved wart).

### The `publish/esper/registry.epl` copy — determined to be a build artifact

`publish/` is gitignored (`.gitignore:174`) — it is `dotnet publish` output,
**not** an independently maintained copy, and is **out of scope**. Mechanism:
`Wintap.Common.props:130-133` copies `core\etl\esper\*.epl` as Content to
`<output>\esper\` (Link + PreserveNewest), and
`Serializer.readQueryFromFile` (`Serializer.cs:439-468`) prefers that
on-disk `esper\registry.epl` over the embedded resource. Consequence worth
recording in the audit: a deployed host picks up the new EPL only via a
rebuilt/republished deployment (the normal path); never hand-edit a
`publish/` tree.

### Schema-inference note (why this is additive-safe)

`ParquetWriter.DetermineSchemaFromExpando` (`ParquetWriter.cs:262-289`)
derives the parquet schema from the first ExpandoObject of each flushed
batch — new always-present members become new columns automatically; no
ParquetWriter change is needed. Existing column names and types are
untouched; only relative column position shifts, which name-based consumers
(DuckDB `union_by_name`, Wintappy) do not observe.

## Scope

Exactly two production files changed, plus tests:

- `wintap/core/etl/esper/registry.epl` — Implementation Note 1.
- `wintap/core/etl/extract/RegistrySerializer.cs` — Implementation Notes 2–3.
- **New:** `tests/Wintap.Tests/RegistryParquetPlumbingTests.cs`, all tests
  tagged `[Trait("Category", "wrc-08")]`.

Hard constraints:

- The scoped exception covers **exactly** these two production files —
  no other `.epl` file, no other serializer, no `ParquetWriter`, no
  `WintapMessage` (wrc-05 landed; additive schema is done), no sensor files,
  no Wintappy/DBT changes.
- **Additive-only:** every existing parquet column keeps its exact name,
  type, and value semantics. New columns are always present on every row.
- **No new NuGet dependencies.**
- Audit required at `developer_docs/audits/wrc-08-parquet-value-plumbing.md`.

## Dependencies

- wrc-05 (schema: `PreviousData`/`PreviousDataType`, `QWORD`/`NONE` —
  Approved, audited) and wrc-06 (sensor populates all four fields with
  explicit `NONE`/`""` — Approved, audited). This unit executes after
  wrc-07; nothing in it blocks on wrc-07's mask/canary content.
- Architect decision 2026-08-25 (wiki log entry of the same date): wrc-08
  rolled in as a scoped Non-Goals exception; NOT a criteria amendment.

## Implementation Notes

### 1. `registry.epl` — add the two previous-value fields to select AND group by

Replace the file content with exactly:

```sql
select istream
  "Registry" as protocol,
  registry.path as path,
  registry.dataType as dataType,
  registry.valueName as valueName,
  registry.data as data,
  registry.previousData as previousData,
  registry.previousDataType as previousDataType,
  PidHash,
  ProcessName,
  PID,
  CAST(ActivityType, string) as activityType,
  min(eventTime) as firstSeen,
  max(eventTime) as lastSeen,
  count(*) as eventCount,
  'Registry' as MessageType,
  AgentId
from WintapMessage(CAST(messageType, string) = 'Registry').win:time_batch(10 sec) as msg
group by registry.path, registry.dataType, registry.valueName, registry.data, registry.previousData, registry.previousDataType, PidHash, PID, activityType, ProcessName
having count(*) > 0
```

Notes:

- The new fields follow the existing `registry.dataType` style (un-cast;
  NEsper resolves `previousData`/`previousDataType` case-insensitively
  against the wrc-05 properties, same as `dataType`→`DataType` today).
- Both new fields are added to `group by` (required for non-aggregated
  select items; the `AgentId` leniency is not to be extended). **Intended
  semantic consequence:** within a 10 s batch, writes identical in
  (path, dataType, valueName, data, pidhash, pid, activity, name) but
  differing in previous value now aggregate as separate rows with their own
  `eventCount` — more faithful, deliberately accepted by the Architect as
  part of this unit.
- Do not touch any other `.epl` file. `reg-activity.epl`
  (`WintapETL.cs:165`) does not exist and its `RegWorker_DoWork` handler has
  no callers — dead path, out of scope (sweep-queue candidate, already
  noted by the Engineer).

### 2. `RegistrySerializer.cs` — extract an order-preserving, testable mapping seam

Replace the body of `HandleSensorEvent` so the EventBean→ExpandoObject
mapping lives in a new `internal static` method (testable without
constructing the serializer — the base ctor deploys Esper statements and
must never run in unit tests):

```csharp
protected override void HandleSensorEvent(EventBean sensorEvent)
{
    try
    {
        base.HandleSensorEvent(sensorEvent);
        IdGenerator idGen = new IdGenerator();
        string hostname = HostSerializer.Instance.HostId.Hostname;
        string agentId = StateManager.AgentId.ToString();
        ExpandoObject flatMsg = BuildFlatMessage(
            name => sensorEvent[name],
            agentId,
            hostname,
            (regPath, regValue) => idGen.GenKeyForRegistry_Entry(transform.Transformer.context, hostname, agentId, regPath, regValue));
        this.Save(flatMsg);
        sensorEvent = null;
        flatMsg = null;
    }
    catch (Exception ex)
    {
        WintapLogger.Log.Append("Error creating Registry data object for pid: " + sensorEvent["PID"] + ", exception: " + ex.Message, LogLevel.Info);
    }
}
```

### 3. `RegistrySerializer.cs` — the mapping method (three new columns)

```csharp
internal static ExpandoObject BuildFlatMessage(
    Func<string, object> field,
    string agentId,
    string hostname,
    Func<string, string, string> regIdHash)
{
    dynamic flatMsg = new ExpandoObject();
    flatMsg.AgentId = agentId;
    flatMsg.ActivityType = field("activityType").ToString();
    flatMsg.ProcessName = field("ProcessName").ToString();
    flatMsg.Reg_Data = field("data").ToString();
    flatMsg.Reg_DataType = field("dataType")?.ToString() ?? WintapMessage.DataTypeEnum.NONE.ToString();
    flatMsg.Reg_PreviousData = field("previousData")?.ToString() ?? string.Empty;
    flatMsg.Reg_PreviousDataType = field("previousDataType")?.ToString() ?? WintapMessage.DataTypeEnum.NONE.ToString();
    flatMsg.EventCount = Int32.Parse(field("eventCount").ToString());
    flatMsg.FirstSeenMs = (long)field("firstSeen");
    flatMsg.LastSeenMs = (long)field("lastSeen");
    flatMsg.PID = Int32.Parse(field("PID").ToString());
    flatMsg.PidHash = field("PidHash").ToString();
    flatMsg.HostHame = hostname;
    flatMsg.Reg_Path = field("path").ToString().ToLower();
    flatMsg.Reg_Value = field("valueName").ToString();
    flatMsg.Reg_Id_Hash = regIdHash(flatMsg.Reg_Path, flatMsg.Reg_Value);
    flatMsg.MessageType = "PROCESS_REGISTRY";
    flatMsg.EventTime = DateTime.FromFileTimeUtc((long)field("firstSeen"));
    return flatMsg;
}
```

Rules embodied above — follow them exactly:

- **Existing columns keep their exact expressions** (same names, same
  direct `.ToString()` null behavior, same insertion order, same
  `HostHame` typo, same lowercased `Reg_Path`, same `Reg_Id_Hash`
  position via the `regIdHash` callback). The only behavioral change to
  existing columns is none.
- **New columns are inserted immediately after `Reg_Data`** (value-column
  grouping; position is cosmetic — consumers are name-based).
- **New columns are null-guarded; existing ones are not.** Rationale: the
  wrc-06 sensor always sets all four fields, but plugin-authored Registry
  messages predating wrc-05 may leave `PreviousData == null` (wrc-05 test 5
  pinned that default). Null previous data ⇒ `""`; null type field ⇒
  `"NONE"` (the schema's no-value marker per the wrc-05 stamp). Known and
  accepted: a plugin object that never sets `PreviousDataType` serializes
  `"STRING"` (the pinned value-type default ordinal 0) — that is wrc-05's
  documented default, not this unit's concern.
- `WintapMessage` is already in scope via
  `using gov.llnl.wintap.collect.models;` (`RegistrySerializer.cs:9`) —
  no new usings beyond what the compiler requires for `Func<>`
  (`System` is already imported).

## Tests — `tests/Wintap.Tests/RegistryParquetPlumbingTests.cs`

All `[Trait("Category", "wrc-08")]`. Honest-coverage statement: there is no
Esper-runtime test precedent in `tests/Wintap.Tests` (shc-02 deliberately
tested `EventChannel.Send` *around* Esper via the `skipEsperSend` seam), and
standing up a live Esper runtime is out of scope. Coverage here is
three-layered: (A) the shipped EPL artifact's text, (B) a standalone
*compile* of that artifact against the real `WintapMessage` event type —
no runtime, no deploy — and (C) the serializer mapping via the new static
seam. End-to-end row content is covered by the Architect's live-data
verification addendum (below), not by unit tests.

Read the EPL exactly as production ships it — the embedded resource, not a
repo-relative source path:

```csharp
using Stream s = typeof(gov.llnl.wintap.core.infrastructure.EventChannel)
    .Assembly.GetManifestResourceStream("gov.llnl.wintap.core.etl.esper.registry.epl");
```

(`typeof(...)` does not trigger static initializers; do NOT touch any
`EventChannel` static member in these tests.)

### A. EPL text regression

1. Select list contains `registry.previousData as previousData`,
   `registry.previousDataType as previousDataType`, and still
   `registry.dataType as dataType` and `registry.data as data`.
2. The `group by` line contains `registry.previousData` and
   `registry.previousDataType` (guards the select-without-group-by mistake).

### B. EPL standalone compile smoke

3. The embedded `registry.epl` text compiles via
   `EPCompilerProvider.Compiler.Compile(eplText, new CompilerArguments(cfg))`
   with `cfg` built inline mirroring `EventChannel.cs:169-178` exactly:
   `Configuration` + `Common.EventMeta.ClassPropertyResolutionStyle =
   PropertyResolutionStyle.CASE_INSENSITIVE` +
   `Common.AddEventType(typeof(WintapMessage))` +
   `Compiler.ByteCode.IsAllowSubscriber = true` +
   `Compiler.ByteCode.SetAccessModifiersPublic()` +
   `Compiler.ByteCode.BusModifierEventType = EventTypeBusModifier.BUS`.
   Assert no exception and a non-null compiled unit. (NEsper's compiler is
   already a transitive dependency via the Wintap project reference; add no
   package.) **Documented fallback:** if this compile hits an unforeseeable
   NEsper environment issue unrelated to the EPL content, record the exact
   failure in the audit, keep tests A/C, and note that compile coverage
   falls to the live addendum — do not fight it into place with runtime or
   deploy calls.

### C. `BuildFlatMessage` mapping (dictionary-backed `Func<string, object>`)

Fixture: a `Dictionary<string, object>` with e.g. `activityType="Write"`,
`ProcessName="testproc"`, `data="hello-wrc-2"`,
`dataType=WintapMessage.DataTypeEnum.STRING` (boxed enum, exactly what the
un-cast EPL field yields), `previousData="hello-wrc"`,
`previousDataType=WintapMessage.DataTypeEnum.STRING`, `eventCount=3L`,
`firstSeen=133700000000000000L`, `lastSeen=133700000100000000L`, `PID=1234`,
`PidHash="abc123"`, `path=@"REGISTRY\MACHINE\SOFTWARE\Wintap\Test"`,
`valueName="TestValue"`; `agentId="agent-1"`, `hostname="testhost"`,
`regIdHash=(p, v) => p + "|" + v`. Cast the result to
`IDictionary<string, object>` for assertions.

4. **Column contract:** the result has exactly the 18 members
   `AgentId, ActivityType, ProcessName, Reg_Data, Reg_DataType,
   Reg_PreviousData, Reg_PreviousDataType, EventCount, FirstSeenMs,
   LastSeenMs, PID, PidHash, HostHame, Reg_Path, Reg_Value, Reg_Id_Hash,
   MessageType, EventTime` in that insertion order (pins both the additive
   columns and the preserved existing contract, `HostHame` wart included).
5. **Enum-name convention:** `Reg_DataType == "STRING"` and
   `Reg_PreviousDataType == "STRING"` from the boxed enums; a variant with
   `dataType=QWORD`, `previousDataType=NONE` yields `"QWORD"`/`"NONE"`
   (names, never ordinals).
6. **First-write encoding:** `previousData=""`,
   `previousDataType=WintapMessage.DataTypeEnum.NONE` ⇒
   `Reg_PreviousData == ""` and `Reg_PreviousDataType == "NONE"`.
7. **Null guards (new columns only):** `previousData=null`,
   `previousDataType=null`, `dataType=null` ⇒ `""`, `"NONE"`, `"NONE"`
   respectively — no exception.
8. **Existing-column semantics preserved:** `Reg_Path` is the lowercased
   input path; `Reg_Id_Hash` equals the stub applied to the *lowercased*
   path and `Reg_Value`; `MessageType == "PROCESS_REGISTRY"`;
   `EventTime == DateTime.FromFileTimeUtc(firstSeen)`;
   `FirstSeenMs`/`LastSeenMs` are the raw longs; `EventCount` parsed to
   `int`; `HostHame == "testhost"`; `AgentId == "agent-1"`.
9. **Value passthrough:** `Reg_Data == "hello-wrc-2"`,
   `Reg_PreviousData == "hello-wrc"` (current and previous values are
   independent columns, overwrite case).

If the test class needs the shc `Env.SetDataRoot`-before-`WintapLogger`
fixture guidance, something is wrong — these tests must not touch
`WintapLogger`, `EventChannel` statics, `StateManager`, `HostSerializer`,
or construct any `Serializer`. Static method + inline `Configuration` only.

## Architect Verification Addendum (required statement, executed post-implementation)

After this unit lands and a branch build produces live data, the Architect's
`verification.md` addendum for wrc-08 is **a single live-data DuckDB query**
showing the three new columns populated — completing the feature's
availability-anchor evidence. Template (adjust the data-root glob to the
host's layout; serializer output name is `registryserializer`):

```sql
SELECT ActivityType, Reg_Path, Reg_Value, Reg_DataType,
       Reg_PreviousDataType, Reg_Data, Reg_PreviousData
FROM read_parquet('<data-root>/parquet/raw_sensor/registryserializer/**/*.parquet', union_by_name=true)
WHERE ActivityType = 'Write'
ORDER BY (Reg_PreviousData <> '') DESC
LIMIT 20;
```

Pass condition: at least one overwrite row with `Reg_PreviousData`
non-empty and a real `Reg_PreviousDataType`; first-write rows show
`Reg_PreviousDataType = 'NONE'` with empty `Reg_PreviousData`. The
Developer's audit notes that this addendum is owed; the Developer does not
run it.

## Acceptance Criteria

1. `git diff --stat` shows exactly two production files changed
   (`wintap/core/etl/esper/registry.epl`,
   `wintap/core/etl/extract/RegistrySerializer.cs`) plus the new test file.
2. The EPL diff is exactly the two select lines and the two group-by terms
   specified — nothing else in the file changes.
3. `BuildFlatMessage` produces the 18-column contract of test 4; the 15
   pre-existing columns keep their exact names, expressions, and insertion
   order; the three new columns are `Reg_DataType`, `Reg_PreviousData`,
   `Reg_PreviousDataType` with enum-name string values.
4. New columns are null-guarded (`""` / `"NONE"`); existing columns'
   behavior is bit-identical to the pre-change code.
5. No other `.epl`, serializer, `ParquetWriter`, `WintapMessage`, sensor,
   or `publish/` file is touched; no NuGet change.
6. All wrc-08 tests pass; the whole `Category~wrc` suite passes; the full
   test project passes (no regression).
7. Audit filed at `developer_docs/audits/wrc-08-parquet-value-plumbing.md`,
   including: the publish-copy-is-a-build-artifact note, the group-by
   aggregation-granularity consequence, the compile-smoke outcome (or
   documented fallback), and the owed Architect live-query addendum.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wrc-08"
dotnet test --filter "Category~wrc"
```

If the repository-root commands fail with the known `MSB4249`
website-project issue, use the documented project-scoped fallbacks and note
the deviation in the audit:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-08"
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category~wrc"
```

## Out of Scope

- Any other `.epl` file (file/tcp/udp/process/default/esper-context) or any
  other serializer — the scoped exception is registry-only.
- `publish/**` (gitignored `dotnet publish` output — regenerated, never
  edited), `ParquetWriter`, `WintapMessage`, `RegistrySensor`, or any
  sensor/enabler/canary file.
- The dead `RegWorker_DoWork`/`FileWorker_DoWork` handlers and the
  nonexistent `reg-activity.epl`/`file-activity.epl` they reference
  (`WintapETL.cs:158-166`) — observed, recorded as a sweep-queue candidate,
  not touched.
- Fixing the `HostHame` typo, `FirstSeenMs`/`LastSeenMs` naming, the
  `EventTime` source inconsistency with FileSerializer, or the EPL's
  ungrouped `AgentId` — existing-contract warts stay.
- Renaming existing columns, changing enum rendering to ordinals, hex/
  rendering convention changes, or any Wintappy/DBT/analytics-side change.
- Standing up a live Esper runtime or live ETW in unit tests; the live-data
  query is Architect-run.
- No new NuGet packages; no Linux/macOS-specific code.
