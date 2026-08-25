# wrc-01 KCB Correlation Spike (Retroactive Record)

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

Answer the make-or-break question for a hybrid registry sensor design: **can
a classic-kernel KCB rundown seed a pointer-to-path map usable against
manifest-provider (`Microsoft-Windows-Kernel-Registry`) events?** If classic
`KeyHandle` addresses matched manifest `BaseObject`/`KeyObject` pointers, a
rundown-seeded map could supply full key paths without live bookkeeping.

## What Was Executed

`C:\PUBLIC\wrc-poc\Program.cs` (TraceEvent 3.1.23, Windows 11 Pro 22631,
elevated, run by the Architect):

1. **KCB harvest** — classic-kernel session `WrcPoc.KernelRundown` (never
   `NT Kernel Logger`) enabled with `KernelTraceEventParser.Keywords.Registry`
   to an ETL file for ~1 s, then stopped — KCB rundown events are emitted at
   session stop. Handlers for `RegistryKCBCreate` /
   `RegistryKCBRundownBegin` / `RegistryKCBRundownEnd` built
   `kcbMap: KeyHandle → KeyName`.
2. **Live manifest session** — `WrcPoc.ManifestRegistry` subscribed to
   `70EB4F03-C1DE-4F73-A051-33D13D5413BD` via `RegisteredTraceEventParser`
   (the same decode path Wintap's `EtwProviderCollector` uses). Every
   `CreateKey`/`OpenKey` `BaseObject` and every value-op `KeyObject` was
   looked up in `kcbMap` (and in a live map maintained from
   CreateKey/OpenKey, i.e. "RegParents done right", as the comparison
   baseline).
3. Self-test registry writes to `HKCU\Software\WrcPoc` provided known-key
   traffic whose base key (`HKCU\Software`) predates the live session —
   exactly the case the rundown map exists to solve.

## Results — NEGATIVE (decisive)

From `C:\PUBLIC\wrc-poc\run.log` (baseline run; session reported
`events lost: 0`, so not a loss artifact):

| Measurement | Value |
|---|---|
| KCB map entries harvested | 6,339 |
| Create/Open BaseObject resolved via KCB map | **0** (6,268 unresolved; 46.2% overall resolution, all via absolute `RelativeName`/live map) |
| Value-op KeyObject resolved via KCB map | **0** (19,827 unresolved; 60.1% overall, all via live map) |
| Replication in probe3.log | 0 via KCB map; 25,259 value-op lookups unresolved |

**Conclusion: classic-kernel KCB `KeyHandle` addresses and manifest-provider
`KeyObject`/`BaseObject` pointers are disjoint namespaces — zero joins across
~26k lookup attempts.** The hybrid classic+manifest design is dead, and with
it any FileSensor-style rundown-mirroring machinery for registry.

Corroborating structural evidence: KCB rundown arrives only at session stop
(perfview issue #928), and TraceEvent's own registry key-name tracking is
disabled for real-time sources and marked suspect by its authors (perfview
`KernelTraceEventParser.cs:2989`, `:5447`).

Baseline facts also established in this run (motivating wrc-02):

- `KeyName` populated on **0 / 49,679** value-op events under a normal
  provider enable; `SetValueKey` `CapturedData` on **0 / 58** — with
  `DataSize` correct and payload zero-length (kernel knows the size, does not
  copy the bytes).
- Even a correctly-maintained live pointer map ceilings at 46.2%/60.1%
  resolution — the legacy `RegParents` approach cannot be fixed into
  correctness.
- Event volume all-keywords: ~16-17k events/s on a quiet dev box;
  `SetValueKey` only tens per 15 s window.
- Payload schemas per event ID recorded (IDs 1–14 + hive family) — TDH
  supplies no friendly names for this provider (dispatch must be by numeric
  ID).

## Evidence

- `C:\PUBLIC\wrc-poc\run.log` (primary), `C:\PUBLIC\wrc-poc\probe3.log`
  (replication), `C:\PUBLIC\wrc-poc\Program.cs` (harness).
- Durable record: `../../../Wintap-Analytics/wiki/decision/registry-provider-strategy.md`
  (Alternatives Considered §1).

## Consequences for the feature

Grounds ADR decision 1 (manifest-only sensor; drop all KCB/classic-rundown
machinery) and eliminates the hybrid alternative permanently. See the ADR and
`wiki/work/improve-windows-registry-collection/implementation_plan.md`.

## Test Command

Not applicable — retroactive record of an already-executed spike; no code
landed in the wintap repo and no tests exist or are owed for this unit.

## Out of Scope

Everything. This record creates no work. The follow-on capture-filter spike
is recorded separately as `wrc-02-capture-filter-spike.md`.
