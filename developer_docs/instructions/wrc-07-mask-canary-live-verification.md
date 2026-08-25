# wrc-07 Keyword-Mask Wire-Up, Capture-Loss Canary, and Live-Verification Support

**Date:** 2026-08-25
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-25 (canary knobs confirmed as
proposed: `CaptureCanary` under `HKLM\SOFTWARE\Wintap\Collectors\Registry`,
60 s tick / 5 min re-assert, dual loss modes, self-noise handling as
specified)

> **Approval note for the Architect:** the canary knobs in this instruction
> (key path, value name, 60 s interval, 5 min re-assert interval, detection
> and escalation mechanics, self-noise handling) are **Engineer proposals**
> grounded in repo precedent and probe8 — they were deliberately deferred to
> this drafting per the plan's Open Question 4 and are yours to confirm or
> change at approval.

## Purpose

Final production unit of `improve-windows-registry-collection` (plan:
`../Wintap-Analytics/wiki/work/improve-windows-registry-collection/implementation_plan.md`;
governing ADR:
`../Wintap-Analytics/wiki/decision/registry-provider-strategy.md`).

Probe8 (Architect-run, 2026-08-25, `C:\PUBLIC\wrc-poc\probe8.log`;
`WrcPoc.exe 15 FFFFFFFF 1 4 5300`) proved the narrowed keyword mask composes
cleanly with the capture filter: KeyName 106/106, CapturedData 91/91, all
six REG types byte-perfect on both CapturedData and PreviousData, 0 events
lost, 470 events / 15 s (~31/s) with masked-out event types structurally
absent (vs. the ~16k/s firehose). **The mask decision is FINAL: `0x5300`
default; `0x5700` when `CollectRegistryRead` is true** (ADR addendum).

This unit does three things:

1. **Wires the final mask** into the sensor's use of the wrc-04
   `RegistryCaptureEnabler` (and the TraceEvent session enable), selected by
   the existing `Properties.Settings.Default.CollectRegistryRead` setting —
   the gate the legacy sensor already honors (`RegistrySensor.cs:62`). Never
   `ulong.MaxValue` anywhere in the live path (frozen criterion 6).
2. **Adds the capture-loss canary**: a periodic Wintap-owned registry write
   whose own `SetValueKey` event proves capture is alive; loss (either
   observed-with-empty-KeyName or not-observed-at-all) triggers the wrc-04
   `NotifyCaptureLossSuspected()` re-assert, with a recovery-failed log path
   (frozen criterion 2: loss detectable, recovers without service restart).
3. **Specifies the evidence the Architect-run live verification must
   record** in the feature's `verification.md` (frozen criteria 6 and 8).
   The Architect runs it manually per standing rules; this unit's automated
   tests cover mask-selection logic and the canary state machine through
   seams only — **no live ETW in unit tests**.

## Working Branch

`develop-wrc`.

## Scope

- **Modify:** `wintap/platform/windows/sensor/etw/RegistrySensor.cs` (the
  wrc-06 rewritten sensor): mask selection, `TraceEventFlags`/`EventLevel`,
  enabler mask argument, re-assert timer start, canary wire-in, canary
  self-noise suppression.
- **New:** `wintap/platform/windows/sensor/etw/helpers/RegistryCaptureCanary.cs`
  — the canary state machine (namespace
  `gov.llnl.wintap.platform.windows.collect.etw.helpers`, matching its
  sibling `RegistryPayloadDecoder` from wrc-03).
- **Modify (comment only):** `wintap/platform/windows/sensor/shared/RegistryCaptureEnabler.cs`
  — update the `DefaultKeywordMask`/`ReadKeywordMask` citation comment from
  "PENDING probe8" to confirmed-FINAL (see Implementation Note 1). No code
  change in that file.
- **New:** `tests/Wintap.Tests/RegistryCaptureCanaryTests.cs`, all tests
  tagged `[Trait("Category", "wrc-07")]` (mask-selection tests live here
  too — one test file for the unit).

**Pre-flight check (source-of-truth rule):** this instruction assumes the
wrc-06 sensor shape (enabler constructed and wired at sensor start; typed
event-ID dispatch in `Process_Event`). If the landed wrc-06 code differs
materially from what is described here, **stop and raise to the Architect**
— do not improvise the wiring.

Hard constraints for this feature, repeated in every wrc unit:

- **No new NuGet dependencies.**
- **No `AllowUnsafeBlocks`**, no project-file changes.
- **No scope beyond this instruction** — do not change the mask values
  (Architect-final), do not add Settings/App.config entries (knobs are named
  constants pending Architect direction), no Esper/Serializer/parquet
  changes, no Linux/macOS code.
- **No live ETW in unit tests**; no elevation-dependent tests; session
  names never `NT Kernel Logger`.
- All tests carry the unit trait; audit required at
  `developer_docs/audits/wrc-07-mask-canary-live-verification.md`.

## Dependencies

- **wrc-04** (must be landed): `RegistryCaptureEnabler` with
  `DefaultKeywordMask`/`ReadKeywordMask` constants, mask as constructor
  parameter, `StartReassertTimer(TimeSpan)`, and the
  `NotifyCaptureLossSuspected()` seam this unit triggers.
- **wrc-06** (must be landed): the rewritten manifest-only `RegistrySensor`
  that constructs/wires the enabler at sensor start and dispatches on
  numeric event IDs. (wrc-05's schema arrives transitively via wrc-06.)
- ADR `registry-provider-strategy.md` — the FINAL mask addendum (probe8
  PASS record) and mechanism items 3/5 (REG_NONE first-write encoding;
  CreateKey `Join(BaseName, RelativeName)` path assembly).
- Probe8 evidence: `C:\PUBLIC\wrc-poc\probe8.log` (reproduced above where
  needed; the Developer does not need POC access).
- Read-only grounding: `RegistrySensor.cs:62` (legacy `CollectRegistryRead`
  gate — the setting this unit reuses); `EtwProviderSensor.cs:55`
  (`EnableProvider(EtwProviderId, EventLevel, TraceEventFlags)` — the
  TraceEvent-side enable the mask must also ride);
  `wintap/core/infrastructure/EventChannel.cs:220,252` (self-PID drop);
  `wintap/platform/windows/sensor/etw/FileSensor.cs:205` (sensor-side
  self-PID skip precedent);
  `wintap/platform/windows/sensor/shared/BaseWindowsSensor.cs:134-136` and
  `wintap/core/shared/Env.cs` (`RegistryCollectorPath` =
  `SOFTWARE\Wintap\Collectors` — the Wintap-owned registry key precedent);
  `wintap/core/shared/StateManager.cs:72,99` (`WintapPID`).

## Implementation Notes

### 1. Mask selection and wiring (probe8's exact verified configuration)

Probe8 applied `MatchAnyKeyword = 0x5300` on **both** enable calls (the
TraceEvent session enable and the `EnableTraceEx2` capture enable). Wire
production identically:

```csharp
// In RegistrySensor (internal static, for direct testing via InternalsVisibleTo):
// FINAL (Architect 2026-08-25; probe8 PASS — see the ADR addendum).
internal static ulong SelectKeywordMask(bool collectRegistryRead) =>
    collectRegistryRead ? RegistryCaptureEnabler.ReadKeywordMask
                        : RegistryCaptureEnabler.DefaultKeywordMask;
```

- **Sensor constructor:** set
  `TraceEventFlags = SelectKeywordMask(Properties.Settings.Default.CollectRegistryRead);`
  and `EventLevel = TraceEventLevel.Verbose;` so the initial
  `EnableProvider` call (`EtwProviderSensor.cs:55`) carries the mask and the
  level the enabler will re-assert (the enabler's disable-then-enable is
  authoritative afterwards, but both calls carrying the mask is the probe8
  configuration and today's sensor rides defaults — never rely on defaults).
- **Enabler construction site (from wrc-06):** pass
  `SelectKeywordMask(Properties.Settings.Default.CollectRegistryRead)` as
  the enabler's `matchAnyKeyword` constructor argument (replacing whatever
  interim constant wrc-06 wired).
- **Re-assert timer:** after `EnableCapture()`, call
  `StartReassertTimer(ReassertInterval)` with
  `internal static readonly TimeSpan ReassertInterval = TimeSpan.FromMinutes(5);`
  *(proposed knob — Architect confirms; rationale: re-assert is two cheap
  native calls, and the canary independently detects loss within ~60 s).*
- **`RegistryCaptureEnabler.cs` comment update (comment only):** replace the
  `// PENDING probe8: ...` lines on the mask constants with:
  `// CONFIRMED FINAL by probe8 (2026-08-25): narrowed MatchAnyKeyword`
  `// composes cleanly with the capture filter (probe8.log; ADR addendum).`
- `QueryValueKey` event **processing** stays gated on
  `Properties.Settings.Default.CollectRegistryRead` exactly as wrc-06 wired
  it — under `0x5300` those events do not arrive at all (probe8: masked-out
  types structurally absent), so the mask is the primary volume control and
  the processing gate is defense in depth.

### 2. Capture-loss canary — proposed mechanics (Architect approves the knobs)

**Canary key (proposed):** `HKLM\SOFTWARE\Wintap\Collectors\Registry` —
i.e. `Env.RegistryCollectorPath + "\\" + SensorName`, the exact key
`BaseWindowsSensor.Averager_Elapsed` already writes `EventsPerSecond` /
`LastUpdate` to (`BaseWindowsSensor.cs:134-136`), so Wintap already owns and
writes this path as a service. **Canary value name (proposed):**
`CaptureCanary`; payload REG_SZ `"<sequence>|<utcTicks>"` (content
immaterial — the *event*, not the stored value, is the signal). The kernel
path the event will carry:
`\REGISTRY\MACHINE\SOFTWARE\Wintap\Collectors\Registry`.

**Interval (proposed):** 60 s (`System.Timers.Timer`, the namespace's
established pattern). Detection latency is therefore ≤ ~60 s for the
silent-loss mode, immediate for the empty-KeyName mode.

**Class shape** — `RegistryCaptureCanary` (new file), seams mirroring the
wrc-04 style:

```csharp
/// <summary>
/// Periodic canary write to a Wintap-owned registry key; the canary's own
/// SetValueKey ETW event proves the capture mode is populating KeyName.
/// Two loss modes: event arrives with empty KeyName (capture cleared), or
/// event never arrives (provider/session dead). ADR: registry-provider-strategy.md.
/// </summary>
internal sealed class RegistryCaptureCanary : IDisposable
{
    internal const string CanaryValueName = "CaptureCanary";
    internal static readonly TimeSpan CanaryInterval = TimeSpan.FromSeconds(60);

    // ctor seams: writeAction (production: Microsoft.Win32.Registry.SetValue
    // on HKLM\SOFTWARE\Wintap\Collectors\Registry), wintapPid (production:
    // StateManager.WintapPID), onLossSuspected (production:
    // enabler.NotifyCaptureLossSuspected), log sink (production: WintapLogger).
    internal RegistryCaptureCanary(Action writeAction, int wintapPid,
        Action onLossSuspected /*, log seam per wrc-04 pattern */);

    /// <summary>Pure matcher: is this SetValueKey event our canary?
    /// Match = processId == wintapPid AND valueName == CanaryValueName.</summary>
    internal bool IsCanaryEvent(int processId, string valueName);

    /// <summary>Sensor feeds every SetValueKey event here (before emit).
    /// Returns true when the event was the canary (sensor must then
    /// suppress emission). Empty/absent keyName on a matched event =>
    /// immediate loss handling.</summary>
    internal bool Observe(int processId, string valueName, string keyName);

    /// <summary>Timer-tick body, exposed for tests: (1) if the previous
    /// expectation is unfulfilled => loss handling (silent-loss mode);
    /// (2) perform the canary write (fail-open: a throwing writeAction is
    /// logged at Info and the timer keeps running); (3) arm a new
    /// expectation.</summary>
    internal void OnCanaryTick();

    internal void Start();   // starts the timer; first tick immediate
    internal void Stop();
    internal long LossCount { get; }       // observability
    internal bool RecoveryFailed { get; }  // state for tests/logs
    public void Dispose();
}
```

**State machine (normative):**

1. **Healthy path:** tick writes and arms an expectation; `Observe` sees the
   matched event with a **non-empty** `keyName` → expectation fulfilled.
2. **Loss mode A — empty KeyName:** `Observe` matches but `keyName` is null
   or empty → *immediately* invoke `onLossSuspected` (wrc-04 re-asserts on
   the spot), increment `LossCount`, log **one Error line** (transition
   style: only on entering the lost state).
3. **Loss mode B — event absent:** at the next `OnCanaryTick`, the previous
   expectation is unfulfilled → same loss handling as mode A. This covers
   provider-disabled/session-dead where no event arrives at all.
4. **Recovery-failed escalation:** if the cycle *after* a loss also fails
   (either mode), set `RecoveryFailed` and log **one Error**
   `"registry capture recovery FAILED — capture filter re-assert did not restore KeyName population"`
   — then stay quiet (no per-cycle repeats) until recovery.
5. **Recovery:** a healthy cycle after any loss logs **one Info**
   `RECOVERED` line with the outage duration and clears the state.
   Transition-only logging throughout (shc precedent: never per-failure
   noise).

**Self-noise handling (two grounded layers):**

1. **Existing egress filter (precedent, no change needed):**
   `EventChannel.Send` silently discards any event whose PID equals
   `StateManager.WintapPID` (`EventChannel.cs:252`; documented at `:220`) —
   canary telemetry, like Wintap's existing self-writes (e.g. the
   `EventsPerSecond` stats writes), can never egress even if the sensor
   emitted it.
2. **Targeted sensor-side suppression (this unit adds):** in the sensor's
   `SetValueKey` handler, call `canary.Observe(...)` **first**; when it
   returns true, return without building/emitting a `WintapMessage`
   (precedent for sensor-side self-PID skip: `FileSensor.cs:205`). Do NOT
   blanket-drop all Wintap-PID registry events at the sensor — EventChannel
   remains the single policy point for general self-noise; the sensor drop
   is canary-targeted only.

**Criterion-3 note (record in the audit):** the canary **writes** the live
registry; frozen criterion 3 forbids live-registry **reads to enrich
events**. A write-only canary does not violate it, and wrc-06's
no-live-registry-access assertion is scoped to enrichment reads (plan,
wrc-06 grounding note). The canary write is the sole permitted
`Microsoft.Win32.Registry` use in the sensor path, and it must never read.

**Lifecycle:** create and `Start()` the canary at sensor start after
`EnableCapture()` succeeds; `Stop()`/`Dispose()` on sensor `Stop()` so no
loss alarms fire across shutdown (shc liveness precedent).

### 3. Live-verification evidence contract (Architect-run; frozen criteria 6, 8)

This unit ships the checklist; the **Architect** performs the run (elevated,
lab host, not concurrent with other features' live ETW) and records results
in `../Wintap-Analytics/wiki/work/improve-windows-registry-collection/verification.md`.
The run must record, minimally:

1. **Config under test:** branch/commit; mask in effect (`0x5300`, and a
   second short window with `CollectRegistryRead=true` → `0x5700`); capture
   filter asserted (Wintap.log Info line from the wrc-04 enabler);
   re-assert interval and count.
2. **Event-rate evidence with mask+capture on:** steady-state Registry
   events/s from the sensor's own 10 s averager — the `EventsPerSecond`
   value under `HKLM\SOFTWARE\Wintap\Collectors\Registry`
   (`BaseWindowsSensor.cs:134-136`; note it is only persisted when
   `Profile != "Developer"` — otherwise use the Wintap.log Debug line
   `"Registry, total events over 10/sec: ..."`), sampled at least 3 times
   over ≥ 15 minutes.
3. **Comparison to the probe baselines (already on record):** firehose with
   capture ≈ 240k / 15 s ≈ 16k/s (probe5); masked with capture = 470 / 15 s
   ≈ 31/s (probe8). State the measured production rate against both.
4. **Correctness spot-check (POC self-test pattern):** six-type writes +
   overwrite + delete executed from a **non-Wintap elevated shell**
   (PowerShell `New-ItemProperty`/`Set-ItemProperty` or a POC-style helper)
   — Wintap's self-PID filter would hide Wintap-issued writes — and the
   corresponding emitted `WintapMessage`s verified for full path, decoded
   `Data`, `PreviousData` on the overwrite, and
   `PreviousDataType = NONE` + empty `PreviousData` on first writes.
5. **Canary health:** at least one healthy canary cycle logged; zero false
   loss detections over the observation window; (optional, Architect's
   call) a deliberate capture clear via the POC to demonstrate
   detect → re-assert → RECOVERED end-to-end.
6. **Session integrity:** ETW events-lost counter for the session (probe8
   baseline: 0) and a one-line CPU/memory observation for the Wintap
   process.

This record is the feature's availability-anchor candidate.

### 4. Tests — `tests/Wintap.Tests/RegistryCaptureCanaryTests.cs`

All `[Trait("Category", "wrc-07")]`; no live ETW, no elevation, no real
registry writes (canary tests inject `writeAction`), no running timers
(drive `OnCanaryTick()` directly).

1. **Mask selection:** `SelectKeywordMask(false) == 0x5300UL`;
   `SelectKeywordMask(true) == 0x5700UL`; both equal the wrc-04 constants.
2. **Mask composition (documents the keyword table):**
   `RegistryCaptureEnabler.DefaultKeywordMask == (0x100UL | 0x200UL | 0x1000UL | 0x4000UL)`
   and `ReadKeywordMask == (DefaultKeywordMask | 0x400UL)`.
3. **Matcher purity:** `IsCanaryEvent` true only for (wintapPid,
   `"CaptureCanary"`); false for wrong PID, wrong value name, null value
   name.
4. **Healthy cycle:** `OnCanaryTick()` invokes the injected write and arms
   an expectation; `Observe(pid, "CaptureCanary", "\\REGISTRY\\MACHINE\\...")`
   returns true and fulfills it; next tick → no loss callback, write fires
   again.
5. **Loss mode A:** matched `Observe` with empty/null keyName → loss
   callback exactly once, `LossCount == 1`, `Observe` still returns true
   (suppression unaffected by loss).
6. **Loss mode B:** tick, no `Observe`, tick again → loss callback fired at
   the second tick.
7. **Non-matching events:** `Observe` with wrong PID/value name returns
   false and neither fulfills the expectation nor triggers loss.
8. **Recovery-failed escalation:** loss, then the next cycle also fails →
   `RecoveryFailed == true` and exactly one recovery-failed Error line;
   further failed cycles add no new Error lines (transition-only).
9. **Recovery:** after any loss, a healthy cycle → exactly one Info
   RECOVERED line, `RecoveryFailed == false`, state fully reset.
10. **Fail-open write:** injected `writeAction` throws →
    `OnCanaryTick()` does not propagate, logs at Info, and the next tick
    retries.
11. **Sensor suppression wiring:** the sensor's SetValueKey path drops
    canary-matched events before emission — test via the wrc-06 sensor test
    seam (feed a canary-shaped event; assert no `WintapMessage` is
    produced). If wrc-06 landed no such seam, cover the logic via
    `Observe`'s return-true contract (tests 4–5) and verify the sensor
    wiring by code review in the audit — do not invent a new sensor seam
    beyond what this instruction specifies.

## Acceptance Criteria

1. The live registry path never uses `ulong.MaxValue` or an unfiltered
   enable: `TraceEventFlags`, the enabler constructor argument, and the
   re-assert path all carry `SelectKeywordMask(...)`'s value (0x5300/0x5700
   only). Frozen criterion 6's configuration half is satisfied; the
   measurement half is the Architect-run record (Note 3).
2. `RegistryCaptureEnabler.cs` mask-constant comment updated to
   probe8-FINAL; **no code change** in that file (`git diff` shows comment
   lines only).
3. The re-assert timer is started at sensor start with `ReassertInterval`
   (5 min proposed) and stopped on sensor stop.
4. `RegistryCaptureCanary` implements the state machine of Note 2 exactly:
   both loss modes trigger `NotifyCaptureLossSuspected`, recovery-failed
   escalates once, recovery logs once, transition-only logging, fail-open
   canary writes, `Stop()` suppresses shutdown alarms.
5. Canary events never egress: sensor-side targeted suppression is wired
   (or the audit documents the review-verified wiring per test 11's
   fallback), and the audit records the EventChannel self-PID backstop and
   the criterion-3 write-only note.
6. No new Settings/App.config entries; no NuGet changes; no
   `AllowUnsafeBlocks`; only the three named production files change
   (`git diff --stat`).
7. All wrc-07 tests pass; the full test project passes (no regression);
   feature-wide `dotnet test --filter "Category~wrc"` passes.
8. Audit filed at
   `developer_docs/audits/wrc-07-mask-canary-live-verification.md`,
   including the verification.md evidence checklist handed to the Architect.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wrc-07"
dotnet test --filter "Category~wrc"
```

If the repository-root commands fail with the known `MSB4249`
website-project issue, use the documented project-scoped fallbacks and note
the deviation in the audit:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-07"
```

## Out of Scope

- Changing the mask values or the keyword table (Architect-final; ADR
  addendum), or adding any config surface for them.
- Running the live verification itself (Architect-run; this unit only ships
  the evidence contract), probe7, `EVENT_CONTROL_CODE_CAPTURE_STATE`, or any
  attempt to clear/restore the sticky capture flag.
- Any change to `RegistryPayloadDecoder` (wrc-03), the enabler's code
  (wrc-04 — comment-only touch permitted above), `WintapMessage.cs`
  (wrc-05), or the wrc-06 dispatch/decode/emit logic beyond the canary
  suppression call and the mask/timer wiring named here.
- Registry **reads** of any kind in the sensor path (criterion 3); general
  self-PID filtering changes in EventChannel or the sensor.
- Esper (.epl), Serializer, parquet, Wintappy changes; Linux/macOS code;
  new NuGet packages; live ETW or elevation in unit tests.
