# Process Identity & Attribution Contract (Stage 0 Lock)

**Date:** 2026-06-30
**Status:** Accepted

## Context
The architecture assessment (`documentation/design/architecture-assessment.md`)
proposed a canonical cross-platform contract for process identity, lineage, and
attribution, plus invariants INV-001..INV-005 and open questions OQ-001..OQ-005.
This was driven by multi-platform expansion (Linux sensors built by a separate
team), the need for clean, testable sensor/core integration points, and
high-volume attribution concerns.

Stage 0 of the staging plan ("Document and lock the contract") required turning
the candidate contract into accepted decisions. The Architect (with colleagues)
made the calls in session; this ADR records the locked contract. The assessment's
Decisions Log entries DEC-001..DEC-004 are the canonical append-only record;
this ADR is the wiki-side companion.

## Decision
The locked Stage 0 contract:

1. **Core owns `PidHash` generation** (and `ParentPidHash`). Sensors do NOT
   supply `PidHash` or `ParentPidHash`; core derives both from the canonical
   identity key `(PID, start_time)`. Any sensor-provided value is ignored or
   overridden. (Resolves OQ-001 → DEC-001.)

2. **Flat sensor-supplied field set.** Sensors supply only attributes knowable at
   event time, as a single flat list applying to all activity types — a field is
   simply absent when not knowable rather than being a hard per-activity-type
   requirement:
   `EventTime`, `ReceiveTime`, `PID`, `ProcessName`, `ProcessPath`,
   `ActivityType`, `ParentPID`, `CommandLine`, `Arguments`, `User`, `MD5`, `SHA2`.
   The list deliberately excludes `PidHash`/`ParentPidHash` (per decision 1).
   (Resolves OQ-002 → DEC-002.)

3. **Durable-store backfill for Stop/parent identity.** A Stop event (which may
   not carry the original start time) resolves its identity and backfills
   required fields, including `ParentPidHash`, from a durable store rather than
   carrying them on the event. This is precisely what makes the flat field set in
   decision 2 workable. The identity key stays `(PID, start_time)`. (Resolves
   OQ-004 → DEC-003.)

4. **DuckDB is the single substrate to start — for BOTH durable persistence AND
   hot-path attribution lookups.** DuckDB is already the current `ProcessResolver`
   implementation. Start there for everything and let testing/measurement dictate
   whether a more complex design is warranted. The memory-first cache (from
   AA-006 / OQ-003 / OQ-005 / staging-plan Stage 2) is a **deferred
   optimization** — NOT adopted now, revisited only if testing reveals a
   throughput/latency ceiling. (DEC-004.)

5. **Invariants are mechanism-agnostic.** INV-001..INV-005 were reworded to
   assert correctness, not to prescribe the memory-first mechanism, so Stage 1
   contract tests stay valid regardless of whether the substrate is DuckDB or a
   future in-memory cache.

## Options Considered
- **PidHash ownership — sensor-owned vs core-owned.** Sensor-owned was rejected:
  the two-team model makes consistent per-sensor hashing error-prone, and
  producers already derive it from inconsistent timestamps (assessment AA-009).
  Core-owned centralizes the rule. **Chosen: core-owned.**
- **Minimal field set — per-activity-type required sets vs one flat list.** The
  per-activity-type approach (as originally sketched in OQ-002) was set aside in
  favor of a single flat "supply what you know" list, which is simpler for
  platform teams to implement and to assert in tests, and is viable because core
  backfills from the store. **Chosen: flat list.**
- **Stop-event identity — carry start time on the event vs durable-store
  backfill.** Carrying start time is not achievable across all producers
  (AA-008/AA-009). **Chosen: durable-store backfill.**
- **Substrate — start with DuckDB everywhere vs build memory-first attribution
  now.** Building memory-first now adds a second identity store and coupling
  before measurement justifies it. **Chosen: DuckDB for everything to start;
  memory-first deferred.**

## Tradeoffs
- Core takes on identity-derivation and Stop/parent reconciliation
  responsibility, including a backfill path that must be correct and tested.
- Starting with DuckDB on the hot path accepts the known per-event DB-lookup cost
  (assessment AA-006) as a starting point, pending measurement.
- Sensors become simpler (describe observations, not derived identity).

## Consequences
- **Easier:** a clear, testable sensor/core contract; Stage 1 contract tests can
  now encode INV-001..INV-005 directly; platform (Linux) sensor authors have an
  unambiguous field list and need not implement hashing.
- **Harder / deferred:** if measurement later reveals a throughput/latency
  ceiling, the memory-first optimization (staging-plan Stages 2 and 4) is
  revisited — but the mechanism-agnostic invariants mean the contract tests
  survive that change.
- The substrate decision (DuckDB) is a starting point, explicitly subject to
  revision on evidence.

## Supersedes / Superseded By
None. This ADR locks the Stage 0 contract; future ADRs may revisit the substrate
(DEC-004) if measurement warrants.
