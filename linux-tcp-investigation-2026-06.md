# TCP Network Capture Investigation

**Date:** 2026-06-11  
**Investigated Branch:** `grantj-ebf-fixes`  
**Status:** TCP events not reaching EventChannel; UDP working normally

This is a dated investigation memo for one debugging session. It is not a canonical setup or deployment guide.

## Update (2026-06-18)

On RHEL8 bring-up we confirmed a second (Linux-specific) failure mode beyond binary/tracer skew: **not all eBPF programs in `network_ops_tracer.bpf.o` were being attached**, which prevents `TcpIpConnect`/`TcpIpDisconnect` (and UDP recv peer tuple fixes) from ever entering the `WintapMessage` stream.

Changes applied:

1. **Attach all programs in the object** (do not rely on program-name strings).
1. **Emit TCP connect/disconnect from more reliable hooks** (`tcp_*_connect` kretprobes + `tcp_close`), because `sock:inet_sock_set_state` old/new state classification can be unreliable on some RHEL8 kernels.
1. **Fallback protocol classification in userland**: treat `op_type 1..5` as TCP even if `evt.Protocol` is unreliable.

Result: `TcpIpConnect`, `TcpIpSend`, `TcpIpRecv`, `TcpIpDisconnect` are present in the raw_sensor conn increment parquet and flow through Esper and the TCP serializer parquet.

## Problem Statement

Lintap running on `grantj-ebf-fixes` showed zero TCP activity despite the branch including a full TCP capture rearchitecture (commit `66769d4`). Log analysis confirmed:

1. **No TCP events flowing**: The `UdpPacketSerializer` 60-second watchdog timer fired (indicating UDP events arrived then stopped), but the identical `TcpConnectionSerializer` timer never fired — meaning zero TCP events have reached the serializer since startup.

2. **No TCP-related warnings**: All `Could not resolve owner process` warnings reference `(File)` type events only. Any TCP event with an unresolvable PID would have produced `(TcpConnection)` warnings — their absence proves no TCP events reached `EventChannel.Send`.

3. **UDP proves the pipeline works**: Same `.bpf.o` file, same ring buffer, same `NetworkSensor` base class — UDP flows through successfully, so the problem is TCP-specific at the eBPF attach/emit layer.

## Root Cause Diagnosis

**High confidence: Binary/tracer version skew**

The branch fundamentally changed TCP capture architecture:

### What commit 66769d4 changed

```diff
- OLD: trace_connect (syscall enter) emits TCP_CONNECT events
- OLD: trace_accept (syscall enter) emits TCP_ACCEPT events  
- OLD: trace_sendto/recvfrom emit TCP_SEND/TCP_RECV with 0.0.0.0 placeholders

+ NEW: All TCP events come ONLY from tracepoint/sock/inet_sock_set_state
+ NEW: trace_connect is now a no-op stub (reads args, returns 0, emits nothing)
+ NEW: trace_accept is now a no-op stub (reads args, returns 0, emits nothing)
+ NEW: trace_sendto/recvfrom now emit UDP only (TCP paths removed)
```

```diff
# NetworkSensor.cs changes
- protected override string BpfProgramName => "trace_connect";
+ protected override string BpfProgramName => "trace_inet_sock_set_state";

  var programNames = new[]
  {
-     "trace_accept",
      "trace_sendto",
      "trace_recvfrom"
  };
```

### Why skew produces these exact symptoms

A pre-branch Wintap binary (expecting TCP from `trace_connect`/`trace_accept`) running against the rebuilt tracer object (where those programs are empty stubs):

- **Loads without error**: The programs exist in the `.bpf.o` (as stubs), `bpf_object__find_program_by_name` succeeds, `bpf_program__attach` succeeds
- **UDP works perfectly**: `trace_sendto`/`recvfrom` still emit UDP normally
- **TCP is silent**: The attached `trace_connect`/`trace_accept` programs return immediately without emitting; the new `trace_inet_sock_set_state` program was never attached by the old binary
- **Zero logged errors**: From libbpf's perspective, everything succeeded

This matches observed behavior exactly: clean startup, UDP flowing, TCP silent, no errors.

## Verification Steps

### 1. Check startup logs for program attachment

**Expected on new build (3 programs):**
```
Network attached 'trace_sendto'
Network attached 'trace_recvfrom'
Network attached 3 network programs
```

**Old builds attached 4 programs** (included `trace_accept`). If you see 4, the binary is stale.

### 2. Check running BPF programs

```bash
sudo bpftool prog list | grep -E "trace_inet_sock_set_state|trace_connect|trace_accept"
```

**Expected:** `inet_sock_set_state` attached to `tracepoint/sock/inet_sock_set_state`  
**Diagnostic:** If you see `trace_connect` or `trace_accept` attached but no `inet_sock_set_state`, the binary is stale.

### 3. Check tracer object timestamp vs running binary

```bash
# Find which .bpf.o was actually loaded
sudo bpftool prog show | grep network_ops_tracer

# Check all possible tracer locations and their timestamps
find /opt/wintap ./tracers ~/git/LLNL/wintap -name "network_ops_tracer.bpf.o" -ls 2>/dev/null

# Compare to running binary
ls -l /path/to/running/Lintap.dll
```

`FindBpfObject` probes 8 locations in priority order — a stale copy in an earlier path (e.g., `/opt/wintap/tracers/`) will shadow a fresh build in a later path.

## Immediate Fix

Redeploy binary + tracer together, ensuring no stale copies remain:

```bash
# Clean all potential stale tracer locations
sudo rm -f /opt/wintap/tracers/network_ops_tracer.bpf.o
rm -f ./tracers/network_ops_tracer.bpf.o

# Rebuild from /tmp to avoid shared-mount issues
cd /tmp/lintap-build/wintap
make clean
make

# Deploy fresh
sudo make install

# Restart and verify
sudo systemctl restart lintap
sudo bpftool prog list | grep inet_sock_set_state
```

## Design Issue: PID Attribution in inet_sock_set_state (Next to Fix)

Once TCP events start flowing, a second problem will surface: **wrong PIDs on most TCP events**.

### The problem

`trace_inet_sock_set_state` calls `bpf_get_current_pid_tgid()`, but this tracepoint frequently fires in **softirq context**:

- `SYN_SENT → ESTABLISHED`: fires when SYN-ACK packet arrives (softirq RX processing)
- `SYN_RECV → ESTABLISHED`: fires during inbound SYN handling (softirq)  
- `ESTABLISHED → CLOSE`: fires during connection teardown (often softirq)

"Current" in softirq context is whatever task was interrupted — often PID 0, `ksoftirqd`, or an unrelated process. This produces:

- Flood of `Could not resolve owner process (TcpConnection)` warnings
- TCP rows with `ProcessName = "Unknown"` or wrong process names
- Incorrect process attribution for threat detection

### Why the Fedora handoff didn't catch this

The 6/9 validation showing 5 valid TcpConnection rows likely got "lucky" on an idle VM where outbound connections happened from user-context `connect()` syscalls. Real workload testing will expose the issue.

### The standard fix (bcc/bpftrace pattern)

Capture PID at syscall time (guaranteed user context), store in a BPF hash map keyed by socket pointer:

```c
// At trace_connect/trace_accept (user context)
struct sock_info {
    u32 pid;
    u64 start_ns;
};

struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, void*);           // socket pointer
    __type(value, struct sock_info);
    __uint(max_entries, 10240);
} sock_pid_map SEC(".maps");

// Store at connect/accept
struct sock_info info = {.pid = pid, .start_ns = ts};
bpf_map_update_elem(&sock_pid_map, &socket_ptr, &info, BPF_ANY);

// Retrieve at inet_sock_set_state
struct sock_info *info = bpf_map_lookup_elem(&sock_pid_map, &ctx->skaddr);
if (info) {
    emit_network_event(info->pid, ...);  // Use stored PID, not current
    bpf_map_delete_elem(&sock_pid_map, &ctx->skaddr);
}
```

This is how `bcc/tools/tcpconnect.py` and `tcptracer` work. The join on `skaddr` also gets both correct PID ownership *and* fully-populated addresses in one event.

## Other Warnings Explained

### "No PidHash found for PID X" + "Could not resolve owner process (File)"

File events arriving for PIDs not in the process resolver's DuckDB table. Three causes on this branch:

1. **CloneSensor failure**: `fedora-handoff-2026-06.md` documents it failing to attach `sched_process_fork` with `-EACCES`. Fork-only children (worker processes, shells) never get registered via execve or clone sensors.

2. **ProcessRundownSensor not enabled**: Gated behind `WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR=true`. Pre-existing processes (started before Lintap) have no registration events.

3. **Race condition**: Fast file activity beats the execve event through `ProcessResolver.RegisterProcess`.

**Impact:** Events pass through with `ProcessName="Unknown"` and a generated PidHash. Not dropped, just unresolved. The 10x repetition per PID (138393 appears ~10 times) is one warning per file event — a short-lived negative-lookup cache would reduce log volume.

### "ParquetWriter.DetermineSchemaFromExpando: Error ... System.Func´1[System.String]"

**Two bugs in one log line:**

1. **Garbage log message**: Line 185 has `ex.ToString` instead of `ex.ToString()` — prints the method group `System.Func´1[System.String]` instead of the exception.

2. **Underlying failure**: A null-valued expando field not covered by the `ParentPidHash`/`FileSha2`/`FileMd5` special-case list. `kvp.Value?.GetType()` yields null → `new DataField(key, null)` throws → field is silently **dropped from the schema**.

**Impact:** Parquet files have **inconsistent schemas** batch-to-batch (some have the field, others don't). This breaks DLT/DuckDB unions downstream. The error fired on a `processserializer` batch — suspect a newly-nullable field from the rundown/parent-enrichment changes.

**Fix:**
```csharp
catch (Exception ex)
{
    WintapLogger.Log.Append($"Error on determining parquet schema: {ex.ToString()}", LogLevel.Error);
}
```

And extend null-handling to default **any** null value to `typeof(string)`, not just three hardcoded keys.

### "ETW session provider has ceased to send network events for 60 seconds"

**Misleading copy-paste from Windows path.** On Linux, this just means the Esper `udp.epl` snapshot stream was quiet for 60 seconds (benign on idle host). The message references "ETW session provider" which doesn't exist on Linux — worth rewording per-platform.

## Debug Leftovers to Clean Before Merge

1. **Hardcoded AgentId**: `TcpConnectionSerializer.cs` line 66:
   ```csharp
   //flatMsg.AgentId = sensorEvent["AgentId"].ToString();
   flatMsg.AgentId = "grantj";  // DEBUG LEFTOVER
   ```

2. **Committed AgentId GUID**: `wintapstate.json` should be `{"AgentId":""}` but contains a real UUID `aa582ab7-3a2f-428b-904e-891590b2e27a`.

## Action Items

**Priority 1 (immediate):**
- [ ] Verify binary/tracer skew via startup log + `bpftool`
- [ ] Redeploy binary + tracer together, purge stale `.bpf.o` copies
- [ ] Run `devtools/network_capture_smoke_test.py` to confirm network rows flow (it will warn if only UDP rows appear)

**Priority 2 (once TCP events flow):**
- [ ] Implement skaddr-keyed PID attribution fix in `network_ops_tracer.bpf.c`
- [ ] Test under realistic load (not just outbound HTTP from idle VM)
- [ ] Fix ParquetWriter schema null-handling and logging
- [ ] Clean up TcpConnectionSerializer hardcoded AgentId
- [ ] Reset wintapstate.json to empty AgentId
- [ ] Reword "ETW session provider" message for Linux context

**Priority 3 (log noise reduction):**
- [ ] Add negative-lookup cache to ProcessResolver to suppress repeated PID warnings
- [ ] Enable ProcessRundownSensor by default or document the env var requirement
- [ ] Investigate CloneSensor `-EACCES` on `sched_process_fork` (Fedora/kernel version specific?)

## Reference

Compiled tracer verified clean:
- All 5 program sections present in ELF
- `inet_sock_set_state` struct layout matches kernel format
- `PROTO_TCP = 6` correctly filters `IPPROTO_TCP`
- C# `NetworkEvent` marshaling offsets match C struct
- UDP path proven functional (same ring buffer, same sensor base)

The code is not broken — this is an environment/deployment issue.
