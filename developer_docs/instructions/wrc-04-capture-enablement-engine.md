# wrc-04 Capture-Mode Enablement Engine

**Date:** 2026-08-25
**Status:** Approved
**Author:** Engineer
**Architect Approval:** Approved 2026-08-25 (IntPtr/GCHandle-pinned adaptation
confirmed — `AllowUnsafeBlocks` stays disabled)

## Purpose

Second production unit of `improve-windows-registry-collection` (plan:
`../Wintap-Analytics/wiki/work/improve-windows-registry-collection/implementation_plan.md`;
governing ADR:
`../Wintap-Analytics/wiki/decision/registry-provider-strategy.md`).

The ADR's capture mode makes the kernel populate
`KeyName`/`CapturedData`/`PreviousData` on `Microsoft-Windows-Kernel-Registry`
events: enable the provider via `EnableTraceEx2` carrying one
`EVENT_FILTER_DESCRIPTOR` whose payload is a 4-byte ULONG `0xFFFFFFFF`
(size must be exactly 4 — 8 bytes is a silent no-op; the descriptor Type
value is irrelevant), using the proven **disable-then-enable** sequence.
This unit builds that enablement engine as standalone code with test seams.
**No wiring into any sensor happens here** (wrc-06 wires it; wrc-07 wires
the final keyword mask and the capture-loss detector).

**Session-handle decision (Architect, 2026-08-25): Option A — guarded
reflection.** `EnableTraceEx2` needs the session's raw `TRACEHANDLE`;
TraceEvent's `TraceEventSession` does not expose it. The engine reflects the
private `m_SessionHandle` field exactly as the POC's proven implementation
does, **failing loudly at three distinct guard points** (field missing,
handle null, method missing) so a future TraceEvent bump breaks at sensor
start — never as silent data loss. The repo pins **TraceEvent 3.1.23**
(`wintap/Wintap.csproj` line 22), the exact version the POC verified
against. Option B (native session ownership) was rejected as more new
P/Invoke surface plus a rework of `Stop()`'s attach-by-name
(`EtwProviderSensor.cs:74`); Option C (upstream TraceEvent change) is an
optional parallel track, not a blocker.

## Working Branch

`develop-wrc`.

## Scope

Exactly one new production file, plus tests:

- **New:** `wintap/platform/windows/sensor/shared/RegistryCaptureEnabler.cs`
  — namespace `gov.llnl.wintap.platform.windows.collect.shared` (the
  namespace of its future caller `EtwProviderCollector`, see
  `EtwProviderSensor.cs:20`). Placed in `sensor/shared/` because it operates
  on a `TraceEventSession`, not on events.
- **New:** `tests/Wintap.Tests/RegistryCaptureEnablerTests.cs`, all tests
  tagged `[Trait("Category", "wrc-04")]`.

**No existing file may change.** In particular, do **not** modify
`EtwProviderSensor.cs`, `RegistrySensor.cs`, `App.config`, or
`Settings.settings` — wiring and configuration are later units.

Hard constraints for this feature, repeated in every wrc unit:

- **No new NuGet dependencies.**
- **Do not add `<AllowUnsafeBlocks>` to any project file.** The POC used
  `unsafe` pointer structs; Wintap does not enable unsafe code
  (`Wintap.Common.props` / `Wintap.csproj` — verified 2026-08-25), so this
  instruction specifies the `IntPtr`/pinned-buffer equivalent below. The
  marshaled memory layout is identical.
- **No scope beyond this instruction**; no wiring, no config surface.
- **No live ETW in unit tests** — session creation requires elevation and is
  Architect-run live verification (wrc-07).
- All tests carry the unit trait; audit required at
  `developer_docs/audits/wrc-04-capture-enablement-engine.md`.

## Dependencies

- ADR `registry-provider-strategy.md` — mechanism record (descriptor,
  sensitivity matrix, sticky-state finding) and the session-handle decision
  (Option A, 2026-08-25).
- Retroactive spike record
  `developer_docs/instructions/wrc-02-capture-filter-spike.md`. The proven
  reference implementation is the POC's `EnableProviderWithSystemFlags`
  (`C:\PUBLIC\wrc-poc\Program.cs:179-221`); everything needed from it is
  reproduced in this instruction — the Developer does not need the POC.
- Read-only grounding: `wintap/platform/windows/sensor/shared/EtwProviderSensor.cs`
  (`Start()` creates the `TraceEventSession` at line 51 and enables the
  provider at line 55 — the live session object the enabler will be handed
  in wrc-06).
- Keyword-mask decision (Architect, 2026-08-25): default `0x5300`, `0x5700`
  when `CollectRegistryRead` is true — **pending live confirmation
  ("probe8")** that a narrowed `MatchAnyKeyword` composes with the capture
  filter. Consequence for this unit: the mask is a **constructor parameter**,
  never read from config here and never hardcoded into a live path; wrc-07
  wires the final value after probe8.

## Implementation Notes

### 1. Native interop declarations (IntPtr adaptation of the POC layout)

```csharp
private const uint EVENT_CONTROL_CODE_DISABLE_PROVIDER = 0;
private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
private const byte TRACE_LEVEL_VERBOSE = 5;          // TraceEventLevel.Verbose
private const int EnableTimeoutMs = 10000;           // POC value

[StructLayout(LayoutKind.Sequential)]
internal struct EVENT_FILTER_DESCRIPTOR
{
    public IntPtr Ptr;        // POC: byte* — IntPtr is layout-identical
    public int Size;          // MUST be exactly 4 (8 = silent no-op)
    public int Type;          // value irrelevant (0x1 and 0x80000001 both work); use 0x1
}

[StructLayout(LayoutKind.Sequential)]
internal struct ENABLE_TRACE_PARAMETERS
{
    public uint Version;              // 2 = ENABLE_TRACE_PARAMETERS_VERSION_2 (FilterDescCount honored)
    public uint EnableProperty;       // 0
    public uint ControlFlags;         // 0
    public Guid SourceId;             // Guid.Empty
    public IntPtr EnableFilterDesc;   // POC: EVENT_FILTER_DESCRIPTOR* — pointer to pinned descriptor
    public int FilterDescCount;       // 1 when a filter is attached, else 0
}

[DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
private static extern int EnableTraceEx2(
    ulong TraceHandle, in Guid ProviderId, uint ControlCode, byte Level,
    ulong MatchAnyKeyword, ulong MatchAllKeyword, int Timeout,
    in ENABLE_TRACE_PARAMETERS EnableParameters);
```

Pinning contract (replaces the POC's stack pointer to a local): allocate the
4-byte payload (`BitConverter.GetBytes(0xFFFFFFFFu)` — little-endian, low
byte first, exactly as the POC's in-memory ULONG) and the
`EVENT_FILTER_DESCRIPTOR` each via `GCHandle.Alloc(..., GCHandleType.Pinned)`;
set `filter.Ptr` to the payload's pinned address and
`parameters.EnableFilterDesc` to the descriptor's pinned address; free both
handles in a `finally` after `EnableTraceEx2` returns. This matches the
POC's lifetime exactly — its `flagsLocal` stack variable was only valid for
the duration of the call, and probe6 proved the kernel samples the flags at
enable time (capture state is sticky provider state afterwards).

### 2. Class shape and seams

```csharp
/// <summary>
/// Enables the undocumented capture mode of a manifest ETW provider
/// (Microsoft-Windows-Kernel-Registry) on a live TraceEventSession:
/// disable-then-enable with a 4-byte 0xFFFFFFFF EVENT_FILTER_DESCRIPTOR
/// via EnableTraceEx2, plus periodic re-assert. Mechanism record:
/// ../Wintap-Analytics/wiki/decision/registry-provider-strategy.md
/// </summary>
internal sealed class RegistryCaptureEnabler : IDisposable
{
    // Test seam: signature mirrors the DllImport; production default invokes it.
    internal delegate int NativeEnableTraceEx2(
        ulong traceHandle, in Guid providerId, uint controlCode, byte level,
        ulong matchAnyKeyword, ulong matchAllKeyword, int timeout,
        in ENABLE_TRACE_PARAMETERS parameters);

    internal RegistryCaptureEnabler(TraceEventSession session, Guid providerId,
        ulong matchAnyKeyword, NativeEnableTraceEx2 nativeOverride = null);

    /// <summary>Guarded reflection (Option A). Throws InvalidOperationException
    /// with an actionable message at each of three guard points.</summary>
    internal static ulong GetSessionHandle(TraceEventSession session);

    /// <summary>Pure guard core, split out for testability: takes the value
    /// reflected from m_SessionHandle. Guards 2 and 3 live here.</summary>
    internal static ulong AcquireHandleFromFieldValue(object sessionHandleFieldValue);

    /// <summary>Disable-then-enable with the capture filter attached.
    /// Acquires the handle on first use (lazily, so construction is cheap
    /// and guard failures surface at sensor start).</summary>
    internal void EnableCapture();

    /// <summary>Re-runs the identical disable-then-enable sequence (probe6:
    /// capture is sticky global provider state that another consumer may
    /// clear by mechanisms we have not observed — ADR decision 3).</summary>
    internal void ReassertCapture();

    /// <summary>Capture-loss seam: callers (the wrc-07 canary) report a
    /// suspected loss; the engine re-asserts immediately. Mechanics of
    /// DETECTION are out of scope here by design.</summary>
    internal void NotifyCaptureLossSuspected();

    internal void StartReassertTimer(TimeSpan interval);
    internal void StopReassertTimer();
    /// <summary>Timer-tick body, exposed for tests (no timer needed).</summary>
    internal void OnReassertTick();

    internal long ReassertCount { get; }   // observability for wrc-07/logs
    public void Dispose();                 // stops the timer; nothing else
}
```

Behavioral requirements:

1. **Guard messages (fail loud, actionable).** Each guard throws
   `InvalidOperationException` naming what broke and the version pin, e.g.:
   - guard 1 (field missing): `"TraceEventSession.m_SessionHandle field not
     found via reflection — TraceEvent version changed? Wintap pins
     TraceEvent 3.1.23 (wintap/Wintap.csproj); registry capture mode cannot
     start."`
   - guard 2 (field value null): same style, `"m_SessionHandle is null —
     session not started"`.
   - guard 3 (method missing): same style, `"DangerousGetHandle() not found
     on TraceEvent's internal SafeTraceHandle"`.
   All three messages MUST contain the strings `TraceEvent` and `3.1.23`
   (asserted by tests). Reflection detail from the POC: the field's runtime
   type is TraceEvent's **internal `SafeTraceHandle`, which is not a
   `System.Runtime.InteropServices.SafeHandle`** (a direct cast throws) —
   hold it as `object` and invoke its public `DangerousGetHandle()` by
   reflection; convert the result with `Convert.ToUInt64`.
2. **Disable-then-enable sequence (never enable-only).** `EnableCapture` /
   `ReassertCapture` issue, in order: (a)
   `EVENT_CONTROL_CODE_DISABLE_PROVIDER` with an `ENABLE_TRACE_PARAMETERS
   { Version = 2 }` and no filter (`FilterDescCount = 0`), keywords 0;
   (b) `EVENT_CONTROL_CODE_ENABLE_PROVIDER` at level `TRACE_LEVEL_VERBOSE`
   with `MatchAnyKeyword` = the constructor value, `MatchAllKeyword` = 0,
   the pinned filter attached, `Version = 2`, `FilterDescCount = 1`,
   descriptor `Size = 4`, `Type = 0x1`, payload `0xFFFFFFFF`. All successful
   POC probes used this sequence; enable-with-filter without a prior disable
   was never isolated (ADR Known unknown 3) — the engine keeps the proven
   sequence unconditionally.
3. **Return-code handling matches the POC:** a nonzero return from the
   **disable** call is logged (WintapLogger, Info) and tolerated (disabling
   a not-currently-enabled provider can legitimately fail); a nonzero return
   from the **enable** call throws `InvalidOperationException` naming the
   win32 error code.
4. **Re-assert timer:** `System.Timers.Timer` (the pattern already used in
   this namespace); the tick body is `OnReassertTick()`, which calls
   `ReassertCapture()` inside try/catch — a native failure during re-assert
   is logged at Info with the error and the timer keeps running (transient
   failure must not kill the engine; the next tick retries). Increment
   `ReassertCount` on every successful re-assert. No timer is started unless
   `StartReassertTimer` is called (wrc-06/07 choose the interval).
5. **Keyword-mask constants (documentation + wrc-07 handoff; NOT wired to
   anything here).** Declare, with the citation comment:

   ```csharp
   // Architect-chosen masks (2026-08-25) from the provider's keyword table
   // (logman query providers Microsoft-Windows-Kernel-Registry):
   //   SetValueKey 0x100 | DeleteValueKey 0x200 | CreateKey 0x1000 | DeleteKey 0x4000
   internal const ulong DefaultKeywordMask = 0x5300;
   //   + QueryValueKey 0x400, only when CollectRegistryRead is enabled
   internal const ulong ReadKeywordMask = 0x5700;
   // PENDING probe8: composition of a narrowed MatchAnyKeyword with the
   // capture filter is not yet live-verified; wrc-07 wires the final value.
   ```

6. **Logging:** one Info line on successful `EnableCapture` (provider GUID,
   mask, "capture filter asserted"), one on each re-assert path. Match the
   existing `WintapLogger.Log.Append(..., LogLevel.Info)` style in
   `EtwProviderSensor.cs`.

### 3. What is honestly unit-testable (and what is not)

Unit-testable without elevation or live ETW — the tests below:

- The **reflection contract against the pinned TraceEvent 3.1.23 binary**:
  `typeof(TraceEventSession).GetField("m_SessionHandle",
  BindingFlags.Instance | BindingFlags.NonPublic)` is non-null, and its
  `FieldType` exposes a public `DangerousGetHandle()`. This runs **without
  instantiating a session** (no elevation) and is the early-warning trip
  wire for any future TraceEvent bump.
- Guard 2 and guard 3 failure paths via `AcquireHandleFromFieldValue`
  (pass `null`; pass a plain `object` lacking `DangerousGetHandle`).
- The full native **call sequence, argument values, and marshaled filter
  contents** via the `NativeEnableTraceEx2` seam: the fake records each
  call and, during the enable call (while the payload is still pinned),
  reads the descriptor via `Marshal.PtrToStructure<EVENT_FILTER_DESCRIPTOR>`
  and the payload via `Marshal.ReadInt32(filter.Ptr)`.
- Struct marshaling shape via `Marshal.OffsetOf`/`Marshal.SizeOf`.
- Re-assert timer logic via `OnReassertTick()` directly (no real timer
  waits) and `NotifyCaptureLossSuspected()`.

NOT unit-testable, deliberately not faked: acquiring a real handle from a
live session (session creation needs elevation), the actual kernel effect of
`EnableTraceEx2`, capture-mode behavior, stickiness, and mask composition
(probe8 / wrc-07 Architect-run live verification). Do not write tests that
pretend to cover these.

### 4. Tests — `tests/Wintap.Tests/RegistryCaptureEnablerTests.cs`

All `[Trait("Category", "wrc-04")]`; no elevation, no ETW sessions, no
timers left running.

1. **TraceEvent reflection contract:** `m_SessionHandle` field exists on
   `TraceEventSession`; its `FieldType` has a public parameterless
   `DangerousGetHandle()`. (Failure message of this test should say the
   TraceEvent version pin moved.)
2. **Guard 2:** `AcquireHandleFromFieldValue(null)` throws
   `InvalidOperationException`; message contains `TraceEvent` and `3.1.23`.
3. **Guard 3:** `AcquireHandleFromFieldValue(new object())` throws
   `InvalidOperationException`; message contains `TraceEvent` and `3.1.23`.
4. **Guard success path:** a test double with a public
   `DangerousGetHandle()` returning `0x1234UL` (any object — reflection is
   duck-typed) yields `0x1234UL`.
5. **Call sequence:** with a fake native delegate and an injected handle,
   `EnableCapture()` produces exactly two calls in order:
   disable (`ControlCode 0`, `FilterDescCount 0`) then enable
   (`ControlCode 1`, `Level 5`, `MatchAnyKeyword` == ctor value,
   `MatchAllKeyword 0`, `Timeout 10000`, `Version 2`, `FilterDescCount 1`).
   *(Injected handle: allow the ctor's `nativeOverride` companion — an
   internal ctor overload or internal settable handle — so no reflection on
   a live session is needed; keep it `internal` and minimal.)*
6. **Marshaled filter contents:** inside the fake's enable call, the
   descriptor read from `EnableFilterDesc` has `Size == 4`, `Type == 0x1`,
   and `Marshal.ReadInt32(filter.Ptr) == unchecked((int)0xFFFFFFFF)`.
7. **Enable failure throws:** fake returns 5 (`ERROR_ACCESS_DENIED`) on the
   enable call → `InvalidOperationException` naming error 5. Disable-call
   failure alone (fake returns nonzero for control code 0, zero for 1) does
   NOT throw.
8. **Re-assert repeats the sequence:** after `EnableCapture()`,
   `ReassertCapture()` produces two more calls (disable, enable) with
   identical arguments; `ReassertCount == 1`.
9. **Tick resilience:** fake throws on the next tick's calls →
   `OnReassertTick()` does not propagate; a subsequent tick with the fake
   healed re-asserts successfully and increments `ReassertCount`.
10. **Capture-loss seam:** `NotifyCaptureLossSuspected()` triggers one
    immediate re-assert (same assertion shape as test 8).
11. **Struct layout:** `Marshal.OffsetOf<ENABLE_TRACE_PARAMETERS>` —
    `Version` 0, `EnableProperty` 4, `ControlFlags` 8, `SourceId` 12,
    `EnableFilterDesc` 32 (x64: 12 + 16-byte Guid = 28, padded to 32),
    `FilterDescCount` 40; `Marshal.SizeOf<EVENT_FILTER_DESCRIPTOR>()` == 16
    on x64. Guard with `Environment.Is64BitProcess` (Wintap targets
    win-x64/arm64 only).

## Acceptance Criteria

1. `RegistryCaptureEnabler.cs` exists at the specified path/namespace; no
   existing production file changed (`git diff --stat`).
2. Handle acquisition is Option A guarded reflection with the three distinct
   fail-loud guard points, each message naming `TraceEvent` and the `3.1.23`
   pin; no `catch` swallows a guard failure.
3. The native call shape matches the POC/ADR record exactly:
   disable-then-enable, `Version 2`, `FilterDescCount 1`, descriptor
   `Size 4` / `Type 0x1`, 4-byte little-endian `0xFFFFFFFF` payload, level
   Verbose, timeout 10000, `MatchAnyKeyword` from the constructor —
   demonstrated by the seam tests (5, 6).
4. Payload and descriptor are pinned across the native call and released
   after it; no `unsafe` keyword, no `AllowUnsafeBlocks`.
5. Re-assert timer and capture-loss seam behave per Implementation Note 2
   (items 3–4): tolerant disable, throwing enable, resilient tick,
   `ReassertCount` observability.
6. `DefaultKeywordMask`/`ReadKeywordMask` constants exist with the citation
   comment and the probe8 pendency note; nothing in this unit reads
   configuration or wires the enabler into any sensor.
7. All wrc-04 tests pass; the full test project passes (no regression).
8. Hard constraints upheld; audit filed at
   `developer_docs/audits/wrc-04-capture-enablement-engine.md`.

## Test Command

```powershell
dotnet build -c Release
dotnet test --filter "Category=wrc-04"
```

If the repository-root commands fail with the known `MSB4249`
website-project issue, use the documented project-scoped fallbacks and note
the deviation in the audit:

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=wrc-04"
```

## Out of Scope

- Wiring into `EtwProviderSensor.cs` / `RegistrySensor.cs` (wrc-06); any
  config/Settings surface for the mask or re-assert interval (wrc-07).
- Capture-loss **detection** mechanics — canary writes, empty-KeyName
  tracking, thresholds (wrc-07; only the `NotifyCaptureLossSuspected` seam
  lands here).
- Any change to `WintapMessage.cs` (wrc-05), any Esper/parquet change.
- Live ETW sessions in tests, elevation-dependent tests, probe8 itself
  (Architect-run), `EVENT_CONTROL_CODE_CAPTURE_STATE`, or any attempt to
  clear/restore the sticky capture flag (probe7 deliberately not run —
  Architect decision 2026-08-25).
- Upstream TraceEvent work (Option C parallel track — separate effort).
- No new NuGet packages; no `AllowUnsafeBlocks`; no Linux/macOS code.
