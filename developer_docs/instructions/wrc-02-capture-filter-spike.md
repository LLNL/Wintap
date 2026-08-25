# wrc-02 Capture-Filter Spike (Retroactive Record)

**Date:** 2026-08-25 (records work executed 2026-08-23/24, pre-feature-open)
**Status:** Retroactive record — no Developer dispatch. This unit was already
executed as part of the pre-open POC spike and is documented here for the
audit trail. **Nothing in this file is pending work.**
**Author:** Engineer
**Architect Approval:** Not applicable — pre-open spike run in the main
session with the Architect executing all elevated commands, per the
POC-first/spikes-in-feature model. No estimates recorded (retroactive by
definition; excluded from the per-unit quality loop — see
`../../../Wintap-Analytics/wiki/work/improve-windows-registry-collection/metrics.md`).

## Purpose

Determine whether `Microsoft-Windows-Kernel-Registry`
(`70EB4F03-C1DE-4F73-A051-33D13D5413BD`) can be made to populate the
`KeyName`/`CapturedData`/`PreviousData` fields its manifest declares but the
kernel normally leaves empty (wrc-01 baseline: KeyName 0/49,679; CapturedData
0/58). Lead: a years-old conference note from a C++-developer colleague of
the Architect — "EVENT_FILTER_DESCRIPTOR (do not set SCHEMATIZED!) / System
Flags set to 1 / Ulong/long. Last byte all 1s" — against discouraging public
information (the gap is described publicly as an unfixed Microsoft
limitation; an SDK header comment claims filter Type is ignored unless the
provider opts in via `EventSetInformation` — evidently wrong for this
provider). No public documentation of the mechanism was found anywhere; the
ADR may be its first written documentation.

## What Was Executed

`C:\PUBLIC\wrc-poc\Program.cs`, CLI-parameterized probe series (elevated,
Architect-run): `WrcPoc.exe [seconds] [flagsHex] [filterTypeHex] [dataSize 4|8]`.

Procedure per probe: TraceEvent starts session `WrcPoc.ManifestRegistry` and
enables the provider normally; `EnableProviderWithSystemFlags` then acquires
the raw `TRACEHANDLE` (reflection: private `m_SessionHandle`, runtime type
TraceEvent-internal `SafeTraceHandle` — not a `SafeHandle`, direct cast
throws; held as `object`, `DangerousGetHandle()` invoked by reflection) and
P/Invokes `EnableTraceEx2` twice: `EVENT_CONTROL_CODE_DISABLE_PROVIDER`, then
`EVENT_CONTROL_CODE_ENABLE_PROVIDER` with `ENABLE_TRACE_PARAMETERS`
(Version 2, `FilterDescCount = 1`) carrying one `EVENT_FILTER_DESCRIPTOR`
whose payload is the probe's flags value. All successful probes used this
disable-then-enable sequence; enable-with-filter without a prior disable was
never isolated. `MatchAnyKeyword = ulong.MaxValue` throughout (the keyword
mask was deliberately NOT probed — open Architect input).

Verification instrumentation: self-test writes to `HKCU\Software\WrcPoc` of
six REG types (REG_SZ, REG_EXPAND_SZ, REG_DWORD, REG_QWORD, REG_BINARY,
REG_MULTI_SZ), each written twice (initial + different overwrite), then a
read, a value delete, and key deletion. `CapturedData` was verified against
the second write and `PreviousData` against the first, byte-for-byte via
`DecodeRegValue`.

## Results — POSITIVE

Probe matrix (Windows 11 Pro 22631, TraceEvent 3.1.23):

| Probe | filter Type | payload | size | KeyName populated | SetValueKey CapturedData | Verdict |
|---|---|---|---|---|---|---|
| baseline (run.log) | none | — | — | 0 / 49,679 | 0 / 58 | not capturing (DataSize correct, payload empty) |
| probe1 | 0x1 | 0xFFFFFFFFFFFFFFFF | 8 | 0 / 24,172 | 0 | silent no-op (success return, zero effect) |
| probe2 | 0x80000001 | 0xFFFFFFFFFFFFFFFF | 8 | 0 / 20,742 | 0 | silent no-op |
| probe3 | 0x1 | 0xFFFFFFFF | **4** | 72,601 / 72,604 | 73 / 73 | **WORKS** |
| probe4 | 0x80000001 | 0xFFFFFFFF | **4** | 16,807 / 16,807 | 79 / 79 | **WORKS** (Type irrelevant) |
| probe5 | 0x1 | 0xFFFFFFFF | 4 | 94,068 / 94,068 | 170 / 170 | PreviousData verified: all 6 REG types byte-perfect on overwrite |
| probe6 | 0x1 | 8-byte no-op payload | 8 | 35,762 / 35,762 | 66 / 66 | capture state STICKY across disable + filterless re-enable |

Findings:

1. **The mechanism:** a 4-byte ULONG payload of `0xFFFFFFFF` on the filter
   descriptor at enable time turns on kernel-side capture. **Size must be
   exactly 4** (8 bytes = silent no-op with success return); **Type value is
   irrelevant** (0x1 and 0x80000001 identical). The field note was wrong on
   type constant and payload width, right in the essentials.
2. **KeyName** carries the full absolute `\REGISTRY\...` path on every event
   declaring the field (probe5: 94,068/94,068) — e.g.
   `\REGISTRY\USER\<SID>\Software\WrcPoc` on the self-test events.
3. **CapturedData** carries the actual value bytes on `SetValueKey` (and
   rides QueryValueKey/EnumerateKey/EnumerateValueKey/QueryKey/
   SetInformationKey). Byte-perfect for all six REG types in probe3 and
   probe5 (all self-test rows `OK`). Strings arrive UTF-16LE with terminating
   NULs included in `DataSize` (`hello-wrc` = `byte[20]`).
4. **PreviousData/PreviousDataType/PreviousDataSize** on overwrites carry the
   pre-change value, byte-perfect for all six types (probe5, all `OK`);
   empty with type/size 0 on first write — correct create-vs-overwrite
   semantics.
5. **Payload growth:** self-test `SetValueKey` `EventDataLength` 58–60 bytes
   baseline → 210–272 with capture on (probe5 raw dumps: 228/230/210 …).
6. **Sticky global provider state (probe6):** after an explicit
   `EVENT_CONTROL_CODE_DISABLE_PROVIDER` and re-enable with a known-no-op
   8-byte descriptor, capture remained fully active — the flag lives in
   kernel-side provider state, not per-session enable state.

Not established (recorded as unknowns in the ADR): reboot persistence;
deliberate clear via a 4-byte value-0 descriptor (probe7 designed but **not
run** — Architect decision 2026-08-25); enable-with-filter without prior
disable; keyword→event-ID mapping; overhead under a narrowed mask.

## Evidence

- `C:\PUBLIC\wrc-poc\probe1.log` … `probe6.log`, `run.log` (baseline),
  `C:\PUBLIC\wrc-poc\Program.cs` (`EnableProviderWithSystemFlags`,
  `DecodeRegValue`, self-test), `C:\PUBLIC\wrc-poc\CONTINUATION.md`
  (spike-session handoff note).
- Durable standalone mechanism record:
  `../../../Wintap-Analytics/wiki/decision/registry-provider-strategy.md`.

## Consequences for the feature

Grounds ADR decisions 2–5 (capture-mode enable, periodic re-assert,
keyword-mask-as-config, typed event-ID parsing with per-REG-type decode) and
supplies wrc-03's test fixtures (probe5 raw dumps) and wrc-04's exact
descriptor/call shape. The POC's reflection-based handle acquisition is
explicitly NOT the production approach — that is the ADR's OPEN
session-handle sub-decision.

## Test Command

Not applicable — retroactive record of an already-executed spike; no code
landed in the wintap repo and no tests exist or are owed for this unit.

## Out of Scope

Everything. This record creates no work. Production units wrc-03…wrc-07 are
proposed in
`../../../Wintap-Analytics/wiki/work/improve-windows-registry-collection/implementation_plan.md`
and are individually gated on Architect approval.
