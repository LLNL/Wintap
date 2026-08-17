# Wintap Wiki — Log

Append each session entry at the **top** of this file.

---

## 2026-08-17 — wpc-01 SID helper completed
**Decisions made:** None. Developer completed the approved wpc-01 unit as
specified: added the classic kernel ETW `ProcessTraceData` UserSID extraction
helper, added the internal payload-parser test seam, and added synthetic xUnit
coverage for extracted SIDs, null-SID markers, and malformed payload guards.
No sensor wiring, schema changes, PidHash/process hash changes, TraceEvent
upgrade, or new NuGet dependencies were introduced.
**ADRs written or updated:** None.
**Wiki pages updated:** None beyond this log; no new architecture decision was
made by the implementation.
**Instructions written:** None.
**Audit artifact:** `developer_docs/audits/wpc-01-sid-helper.md` (Status:
Complete).
**Verification:** `dotnet build -c Release` passed from
`C:\PUBLIC\wintap\tests\Wintap.Tests`; `dotnet test --filter "Category=wpc-01"`
passed with 9/9 tests selected and passing.
**Open questions:** None for wpc-01. Next Windows process-collection unit can
wire the helper into the chosen process sensor path.

## 2026-08-17 — wpc-01 SID helper instruction drafted
**Decisions made:** Architect approved the wpc-01 instruction for Developer
handoff. Drafted the first implementation unit for the
improve-windows-process-collection feature from the already-settled Analytics
feature design. Kept the unit narrow: port the validated classic kernel ETW
`ProcessTraceData` UserSID parser and add synthetic payload tests only; no
sensor wiring, account-name lookup, token fallback, command-line fallback, or
PidHash/process schema changes.
**ADRs written or updated:** None.
**Wiki pages updated:** None beyond this log.
**Instructions written:** `developer_docs/instructions/wpc-01-sid-helper.md`
(Status: Approved — Architect approval 2026-08-17).
**Scratch notes:** `dave-wiki/sources/2026-08-17-wpc-01-sid-helper-notes.md`.
**Open questions:** None for wpc-01. The Developer can implement wpc-01 and
verify with `dotnet build -c Release` plus
`dotnet test --filter "Category=wpc-01"`.

## 2026-06-30 — Stage 0 locked (process identity & attribution contract)
**Decisions made:** Locked the Stage 0 canonical contract from the architecture
assessment (all Accepted, recorded by the Architect in session — not re-opened):
- **OQ-001 — RESOLVED (DEC-001):** Core owns `PidHash`/`ParentPidHash`
  generation. Sensors do NOT supply them.
- **OQ-002 — RESOLVED (DEC-002):** Sensors supply a single flat field list of
  event-time-knowable attributes (`EventTime`, `ReceiveTime`, `PID`,
  `ProcessName`, `ProcessPath`, `ActivityType`, `ParentPID`, `CommandLine`,
  `Arguments`, `User`, `MD5`, `SHA2`) — fields absent when unknowable;
  `PidHash`/`ParentPidHash` excluded.
- **OQ-004 — RESOLVED (DEC-003):** Stop events resolve identity and backfill
  required fields (incl. `ParentPidHash`) from a durable store; identity key
  stays `(PID, start_time)`. This is what makes the flat field set viable.
- **NEW (DEC-004):** DuckDB is the single substrate to start — for BOTH durable
  persistence AND hot-path attribution lookups. Memory-first cache (AA-006 /
  OQ-003 / OQ-005 / staging Stage 2) is a **deferred optimization**, revisited
  only if measurement reveals a ceiling. OQ-003/OQ-005 marked DEFERRED.
- **Invariants reworded** to be mechanism-agnostic so Stage 1 tests survive a
  later substrate change.
**ADRs written or updated:**
`dave-wiki/wiki/decisions/2026-06-30-process-identity-attribution-contract.md`
(Accepted).
**Wiki pages updated:** None beyond the ADR and this log.
**Assessment updated:** `documentation/design/architecture-assessment.md` —
appended DEC-001..DEC-004 to the (previously empty) Decisions Log; reworded
INV-001..INV-005 (dropped the "without a DB query" / DB-mechanism clauses);
annotated OQ-001/OQ-002/OQ-004 as RESOLVED and OQ-003/OQ-005 as DEFERRED with
pointers to the Decisions Log.
**Instructions written:** None (Stage 1 instruction intentionally deferred).
**Open questions / follow-up:**
- Stage 1 (contract tests encoding the reworded INV-001..INV-005, no OS deps)
  is the recommended next instruction — the contract is now locked enough to
  write it against.
- DEC-004 (DuckDB substrate) is explicitly evidence-gated: revisit the
  memory-first optimization if/when measurement shows a throughput/latency
  ceiling.

## 2026-06-30 — P1.1 design settled (test harness)
**Decisions made:** Resolved both open questions from the bootstrap entry.
- **Open Question 1 (placement) — RESOLVED:** Per-target test projects mirroring
  the multi-target agents (`tests/Wintap.Tests/`, `tests/Lintap.Tests/`,
  `tests/Mactap.Tests/`). Architect's pick (Option C over the Engineer's
  single-project recommendation).
- **Open Question 2 (first behavior) — RESOLVED:** A real test against
  `WintapMessage` — construct it and assert `MessageType` matches the
  `MessageTypeEnum` passed to the constructor (Option B), not a smoke test.
- **Reconciliation:** Because `WintapMessage` lives in cross-platform
  `shared/WintapAPI` (net8.0, no OS deps) while the agent projects are OS-bound,
  P1.1 stands up **only** `tests/Wintap.Tests/` now, referencing
  `shared/WintapAPI` (not `wintap/Wintap.csproj`). Lintap/Mactap siblings are
  deferred to a follow-up unit **P1.2**. This honors the per-target convention
  while keeping the first test cross-platform and free of Windows-only package
  drag. Flagged to the Architect as it slightly shapes the chosen approach.
**ADRs written or updated:** `dave-wiki/wiki/decisions/2026-06-30-test-project-structure-and-first-test.md`
(Accepted).
**Wiki pages updated:** None beyond the ADR and this log.
**Instructions written:** `developer_docs/instructions/P1.1-xunit-test-harness.md`
(Status: Draft — awaiting Architect approval).
**Open questions:**
- P1.2 owed: stand up `tests/Lintap.Tests/` and `tests/Mactap.Tests/` siblings;
  decide per-sibling whether they reference shared code vs. the OS-bound agent
  (and accept that OS-bound test runs are platform-gated).
- When the first genuinely Windows-agent-specific behavior is tested,
  `tests/Wintap.Tests/` will additionally reference `wintap/Wintap.csproj` and
  its `dotnet test` becomes Windows-only — revisit the project's reference set
  then.

## 2026-06-30 — Methodology bootstrap
**Decisions made:** Adopted the Architect / Engineer / Developer role-separated
workflow with **per-developer wikis** (this one is `dave-wiki/`) to avoid git
conflicts — each dev only edits their own wiki directory. Adapted from the
OpenCode setup and the Karpathy LLM-wiki pattern. Verification gate: stand up
xUnit first (unit P1.1). Role boundaries enforced via subagent tool limits +
instruction discipline. Instruction/design/audit docs live under
`developer_docs/`.
**ADRs written or updated:** None yet.
**Wiki pages updated:** None yet — wiki is empty and awaiting first ingest.
**Instructions written:** None yet. Recommended next: P1.1 — stand up xUnit
test project under `tests/`.
**Open questions:** Where exactly should the xUnit project sit relative to the
multi-target agents (Wintap/Lintap/Mactap)? What is the first behavior worth
covering with tests?
