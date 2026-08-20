# Dispatch Prompt — Engineer — wpc-09 (minor bug sweep)

> Paste the block below to the **Engineer** subagent. It produces (a) a wiki
> update recording the 2026-08-18 overnight smoke findings and (b) the
> instruction document `developer_docs/instructions/wpc-09-bug-sweep.md`.
> After you (Architect) approve that instruction, dispatch the **Developer**
> with a pointer to the approved instruction file.

---

Draft the instruction document for unit wpc-09 (minor bug sweep) of the
improve-windows-process-collection feature. Write it to
`developer_docs/instructions/wpc-09-bug-sweep.md`. This is the final code
unit of the feature before close-out. Unit wpc-08 (formal validation
harness) is being skipped — the Architect validated slice 2 manually on
2026-08-18 (full process tree back to kernel, usernames on all records,
stable overnight run). Do not renumber; the sweep is wpc-09.

Also update the wiki first (see "Wiki updates" below) so the findings are
recorded independently of the instruction.

## Feature context (read all of these first)

- `C:\PUBLIC\Wintap-Analytics\wiki\work\improve-windows-process-collection\implementation_plan.md`
- `C:\PUBLIC\Wintap-Analytics\wiki\work\improve-windows-process-collection\smoke-followups-2026-08-17.md`
- `C:\PUBLIC\wintap\developer_docs\audits\wpc-07-boot-etl-coverage.md`
- `C:\PUBLIC\Wintap-Analytics\wiki\log.md` (2026-08-18 wpc-07 closeout entry)

## New findings from the 2026-08-18 Architect smoke test (overnight run)

1. **Boot-trace arming did not trigger from config alone.** The Architect set
   `EnableBootProcessTrace = True` in `Wintap.dll.config` and restarted
   Wintap; the GlobalLogger registry values were never written and the boot
   trace did not arm. Manual registry setup was required, after which the
   boot replay path worked end-to-end.

   **Root-cause hypothesis (verify in code before accepting):**
   `BootProcessTraceHelper.ArmForNextBoot()` is called only from
   `WindowsSubscriptionManager.Stop()`, and `Properties.Settings.Default` is
   loaded at process start. Enable-then-restart means the *stopping* instance
   still holds `False` in memory and skips arming; arming would only occur on
   the second stop. Consequence: the first boot after enabling is never
   covered.

2. **Disarm gap on disable (found by main-session code review, same
   lifecycle).** `WindowsSubscriptionManager.Start()` only calls
   `StopOwnedBootSessionDisarmAndGetReplayPath()` when the setting is true.
   If a user arms (setting true, clean stop) and then sets the setting to
   false, nothing ever disarms: the GlobalLogger stays armed on every boot,
   the kernel session runs unattended writing the ETL, and Wintap never stops
   it. Verify and cover this in the same fix.

3. **Missing parent process warnings** — a handful over the overnight run.
   May be the same early-lifetime races already noted in
   `smoke-followups-2026-08-17.md` item 3, or genuinely unresolvable parents
   (parent exited before snapshot). The instruction should direct the
   Developer to triage from the wintap logs first, then either fix
   resolution, or rate-limit/annotate the warning if it is expected behavior.

4. **Process-name parsing errors** — a couple over the overnight run.
   Possibly the same root as the DuckDB unterminated-quote command-line
   errors (smoke-followups item 4). Triage from logs; if it is the DuckDB
   escaping gap, fix the parameterization/escaping in the insert path for
   command lines rather than sanitizing the data.

## Sweep scope (proposed — adjust with justification if the code says otherwise)

In scope for wpc-09:

- **Boot-trace arm/disarm lifecycle fix** (findings 1 and 2). Candidate
  direction for you to weigh in the instruction: arm at startup (after the
  existing detect/stop/disarm/replay sequence) whenever the setting is true,
  so an enabled machine is always armed for the next boot — this also covers
  crash/power-loss, which arm-at-stop never does. And always run the
  disarm/stop-owned-session check at startup regardless of the setting, so
  disabling the feature cleans up. If you conclude the semantics change the
  documented opt-in contract in a way that needs an Architect ruling, say so
  explicitly at the top of the instruction rather than deciding unilaterally.
- **Missing-parent warnings triage + fix or annotate** (finding 3, merges
  smoke-followups item 3).
- **Process-name / DuckDB command-line escaping** (finding 4, merges
  smoke-followups item 4).
- **Cosmetic QA-counter logger tag** (`[WindowsProcessSensor..ctor]`,
  smoke-followups item 5) — first check whether the optional wpc-07 rider
  already fixed it; include only if still broken.

Explicitly out of scope (record as backlog, do not include):

- SensSensor null-value load failure (smoke-followups item 1) — pre-existing,
  unrelated to the process path.
- Missing SignedS3UrlAdapter (smoke-followups item 2) — deployment/config
  gap in the upload path.

## Code to read for the instruction's references

- `wintap/platform/windows/sensor/etw/helpers/BootProcessTraceHelper.cs`
- `wintap/platform/windows/infrastructure/WindowsSubscriptionManager.cs`
  (`Start()` lines ~29–45, `Stop()` lines ~121–129)
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs` (parent
  resolution and replay paths)
- The DuckDB insert path for process command lines (locate it; it is outside
  the sensor)
- `tests/Wintap.Tests/WindowsProcessSensorTests.cs`

## Tests and verification

- Tag new tests `[Trait("Category", "wpc-09")]`. The arm/disarm decision
  logic should be made unit-testable (the registry and session calls are the
  seam boundary — pure decision logic behind them gets tests; actual registry
  writes stay manual-smoke territory). Escaping fixes get tests with
  hostile command-line samples (unterminated quotes, embedded quotes).
- Gate: `dotnet build wintap\Wintap.csproj -c Release` (repo-root Release
  build has the pre-existing MSB4249 Workbench failure — fallback is
  pre-approved), `dotnet test --filter "Category=wpc-09"`, full
  `Category~wpc` regression, and unfiltered test-project run.
- Manual smoke (elevated, executed by the Architect, results pasted into the
  audit): enable setting → restart wintap → **verify GlobalLogger registry
  values exist without manual intervention** → reboot → verify replay log
  lines and boot-time processes in telemetry → disable setting → restart →
  verify GlobalLogger `Start` is 0 and no kernel session persists. Write the
  exact procedure and pass criteria into the instruction.
- Audit artifact: `developer_docs/audits/wpc-09-bug-sweep.md`.

## Constraints and discipline

The instruction must be self-contained: the Developer will not read the
Analytics wiki. Carry over the standing constraints verbatim: no
WintapMessage/ProcessObject schema changes, no PidHash formula changes,
TraceEvent stays at 3.1.23, no new NuGet dependencies. Fix scope is exactly
the items listed in scope — do not clean up unrelated code; flag it instead.
The Developer files the audit and does not touch any wiki path.

Do not modify source, tests, or anything under `developer_docs/audits/`.
Do not update the implementation plan Done Checklist (that happens at
closeout).

## Wiki updates (do these in the same run, before or after drafting)

- Add a dated section (2026-08-18) to
  `wiki/work/improve-windows-process-collection/smoke-followups-2026-08-17.md`
  (or a new dated page if cleaner) recording findings 1–4 above and the
  overnight-run validation summary.
- Record the wpc-08 skip decision and its rationale (manual validation
  accepted 2026-08-18) in the implementation plan notes — do not check or
  remove the wpc-08 row; mark it skipped.
- Append the standard entry to `wiki/log.md`.
- Per the metrics mini-lab, capture a sealed pre-implementation time
  estimate for wpc-09 in
  `wiki/work/improve-windows-process-collection/metrics.md` when the
  instruction is drafted.
