# Fedora VM Handoff: Lintap/Wintap Bring-Up

Date: 2026-06-09

This handoff captures the current Fedora 44 VM bring-up state for the Lintap/Wintap Linux build and runtime test. It is intended to be loaded from inside the Fedora VM at:

```bash
/home/grantj/git/LLNL/wintap/FEDORA_HANDOFF.md
```

## Environment

- Host: macOS using UTM
- Guest: Fedora 44
- Shared repo root in VM: `/home/grantj/git/LLNL`
- Main project path in VM: `/home/grantj/git/LLNL/wintap/wintap`
- Fedora setup script added at: `/home/grantj/git/LLNL/Lintap/fedora-setup-first-pass.sh`
- Runtime data root for dev/test: `/tmp/lintap-data`

## High-Level Status

Build now succeeds from the Fedora VM after several shared-mount fixes.

Current Fedora build rule: keep source on the shared mount, but build and run .NET assemblies from native `/tmp` output. The Makefile detects the shared mount and redirects .NET output/intermediates to:

```text
/tmp/lintap-build/wintap
```

This is now the default Linux bring-up approach and is also safe for Ubuntu.

Runtime status:

- `make run-host-only` is stable when MCP, DuckDB UI, ETL, and sensors are disabled.
- The prior ETL `Bad IL range` failure was isolated to NEsper/Roslyn loading assemblies from the shared mount. Native `/tmp` build output fixes the isolated Esper repro and allows ETL startup.
- The prior normal `ExecveSensor` segfault was isolated to DuckDB-backed parent lookup in `EventChannel.Send`; Linux sensors now populate parent context before sending events.
- `make run` with ETL and `ExecveSensor` enabled produced valid process Parquet when running from `/tmp/lintap-build/wintap`.
- Full all-switch Linux sensor testing on 2026-06-09 produced validated Process, File, TcpConnection, and UdpPacket data through both direct Parquet and normal ETL serializer paths. `CloneSensor` is the remaining exception: it fails to attach `trace_process_fork` to `sched/sched_process_fork` with libbpf `-EACCES`.

## Key Build Problems Fixed

### 1. Fedora dnf5 group install

Fedora 44 uses dnf5. The old command failed:

```bash
dnf -y groupinstall "Development Tools"
```

Use:

```bash
dnf -y group install development-tools
```

The setup script was partially updated, but if it still has `Development Tools`, use `development-tools` manually.

### 2. .NET CreateAppHost mmap failures on shared mount

Building from `/home/grantj/git` (host-shared mount) triggered:

```text
CreateAppHost failed unexpectedly
System.IO.IOException: Invalid argument
MemoryMappedFiles.MemoryMappedView.CreateView
MachOUtils.RemoveSignature
```

Root cause: .NET SDK apphost creation uses mmap and fails on the VM shared mount.

Mitigations added:

- `wintap/wintap/Makefile` detects shared mount types and adds `-p:UseAppHost=false` for the outer build.
- `wintap/Directory.Build.props` maps `DISABLE_APPHOST=true` to `<UseAppHost>false</UseAppHost>`.
- `wintap/wintap/Lintap.csproj` publishes the MCP single-file executable through a VM-local temp path under `/tmp` when MCP is enabled.

### 3. MCP server single-file publish

The MCP server needs a single-file executable, but its apphost must be created on a native VM filesystem.

Mitigations added in `wintap/wintap/Lintap.csproj`:

- `McpPublishTempDir` defaults to `/tmp/Lintap-mcp_temp/` when `DISABLE_APPHOST=true`.
- MCP publish redirects:
  - `BaseIntermediateOutputPath`
  - `BaseOutputPath`
  - final publish output
- MCP publish forces:
  - `--self-contained true`
  - `-p:PublishSingleFile=true`
  - `-p:UseAppHost=true`
- Runtime ID normalized to `linux-arm64` / `linux-x64` instead of Fedora-specific RID.

### 4. Stale obj/bin files in MCP project

After redirecting intermediate output, stale in-repo generated files under `shared/ai/wintap_mcp_server/obj` were compiled and caused duplicate assembly attributes.

Mitigation added in `wintap/shared/ai/wintap_mcp_server/wintap_mcp_server.csproj`:

- Exclude `obj/**` and `bin/**` from `Compile`, `None`, `Content`, and `EmbeddedResource` globs.

Manual cleanup command used:

```bash
rm -rf ../shared/ai/wintap_mcp_server/obj ../shared/ai/wintap_mcp_server/bin /tmp/Lintap-mcp_temp
```

### 5. `dotnet run` is unsafe on shared mount

`dotnet run` performs an implicit build and can recreate the apphost failure.

Use:

```bash
make run
```

or run the built DLL directly:

```bash
WINTAP_DATA_ROOT=/tmp/lintap-data WINTAP_DISABLE_MCP=true dotnet bin/Debug/net8.0/Lintap.dll
```

Avoid:

```bash
dotnet run --project Lintap.csproj
```

### 6. Embedded EPL resource failure

Runtime hit:

```text
System.BadImageFormatException: No string associated with token.
The format of the file '.../Lintap.dll' is invalid.
at gov.llnl.wintap.core.etl.extract.Serializer.regContext()
```

Mitigation:

- `Wintap.Common.props` now copies `core/etl/esper/*.epl` into output under `esper/`.
- `Serializer.cs` reads EPL from output files first, falling back to embedded resources only if files are missing.

Verify after build:

```bash
ls bin/Debug/net8.0/esper
```

Expected files:

```text
default.epl
esper-context.epl
file.epl
focuschange.epl
process.epl
process-stop.epl
registry.epl
tcp.epl
udp.epl
```

## Runtime Feature Switches Added

These were added to simplify Fedora bring-up and isolate native crashes.

### Default Makefile behavior

From `/home/grantj/git/LLNL/wintap/wintap`:

```bash
make run
```

Runs with:

```text
WINTAP_DATA_ROOT=/tmp/lintap-data
WINTAP_DISABLE_MCP=true
WINTAP_DISABLE_DUCKDB_UI=true
WINTAP_DISABLE_ETL=false
WINTAP_DISABLE_SENSORS=false
```

### Host-only mode

```bash
make run-host-only
```

Runs with everything native-heavy disabled:

```text
WINTAP_DISABLE_MCP=true
WINTAP_DISABLE_DUCKDB_UI=true
WINTAP_DISABLE_ETL=true
WINTAP_DISABLE_SENSORS=true
```

Current status: this is stable.

### Isolation commands

Run from:

```bash
cd /home/grantj/git/LLNL/wintap/wintap
```

Host only:

```bash
make clean
make run-host-only
```

Sensors only-ish path (ETL off, sensors on):

```bash
WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false make run
```

ETL on, sensors off:

```bash
WINTAP_DISABLE_ETL=false WINTAP_DISABLE_SENSORS=true make run
```

Everything default except MCP and DuckDB UI disabled:

```bash
make run
```

Re-enable MCP later:

```bash
WINTAP_DISABLE_MCP=false make clean all run
```

Re-enable DuckDB UI later:

```bash
WINTAP_DISABLE_DUCKDB_UI=false make run
```

## Files Changed

### Fedora setup

- `Lintap/fedora-setup-first-pass.sh`
  - First pass Fedora setup script translating Ubuntu cloud-init actions.

### Build/run docs

- `wintap/README.md`
  - Added host-shared filesystem notes.
- `wintap/BUILD_AND_TEST.md`
  - Added detailed Fedora/shared-mount build/run troubleshooting.
- `wintap/FEDORA_HANDOFF.md`
  - This file.

### Shared-mount / apphost handling

- `wintap/Directory.Build.props`
  - Supports `DISABLE_APPHOST=true` -> `UseAppHost=false`.
- `wintap/wintap/Makefile`
  - Added shared-mount detection.
  - Added `DOTNET_BUILD_FLAGS`.
  - Added `make run`, `make run-host-only`, `make run-mcp`, `make run-env`.
  - Added runtime env vars.
- `wintap/wintap/Lintap.csproj`
  - Added optional MCP build via `EnableMcpServer` / `DISABLE_MCP`.
  - Redirects MCP single-file publish through VM-local temp paths when enabled.
- `wintap/shared/ai/wintap_mcp_server/wintap_mcp_server.csproj`
  - Excludes stale `obj/**` and `bin/**` artifacts from source/content globs.

### Runtime isolation

- `wintap/wintap/core/infrastructure/Program.cs`
  - Supports `WINTAP_DISABLE_MCP=true` to skip MCP/AI initialization.
  - Makes `IInfer` registration tolerant of missing MCP/AI services.
- `wintap/wintap/core/infrastructure/WintapSvcCore.cs`
  - Supports `WINTAP_DISABLE_DUCKDB_UI=true`.
  - Supports `WINTAP_DISABLE_SENSORS=true`.
- `wintap/wintap/core/infrastructure/PluginManager.cs`
  - Supports `WINTAP_DISABLE_ETL=true`.
  - Null-safe watchdog stop.
- `wintap/wintap/Wintap.Common.props`
  - Copies EPL files to output under `esper/`.
- `wintap/wintap/core/etl/extract/Serializer.cs`
  - Reads EPL from output files first.
  - Supports optional in-memory backlog limits to prevent OOM on long runs:
    - `WINTAP_ETL_MAX_QUEUE_EVENTS` / `WINTAP_ETL_MAX_QUEUE_EVENTS_<SERIALIZERNAME>`
    - `WINTAP_ETL_QUEUE_DROP_POLICY=newest|oldest` / per-serializer override

- `wintap/wintap/core/etl/load/ParquetWriter.cs`
  - Supports optional parquet batch backlog limits:
    - `WINTAP_PARQUET_MAX_BATCH_BACKLOG`
    - `WINTAP_PARQUET_BACKLOG_DROP_POLICY=newest|oldest`

## Current Known Runtime Behavior

### Working Fedora Path

Build/run from `/home/grantj/git/LLNL/wintap/wintap` with source on the shared mount:

```bash
make build_ebpf
WINTAP_DATA_ROOT=/tmp/lintap-data-etl-execve-native WINTAP_DISABLE_ETL=false WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_EXECVE_SENSOR=true make run
```

Observed after native-output fix:

- Makefile builds to `/tmp/lintap-build/wintap/bin/Debug/net8.0/Lintap.dll`.
- In-app Esper repro passes from native output.
- ETL startup no longer fails with `Bad IL range`.
- `ExecveSensor` starts and ETL writes process Parquet.
- DuckDB validation read 5 `PROCESS` rows and all had `parentpidhash` populated.

### Full Sensor Validation

Build command:

```bash
make build_ebpf build_dotnet
```

Observed on 2026-06-09:

- Build succeeded from the Fedora shared mount with native .NET output under `/tmp/lintap-build/wintap`.
- Full direct-Parquet run used `WINTAP_DATA_ROOT=/tmp/lintap-data-full-sensors-20260609-2`, `WINTAP_DISABLE_ETL=true`, `WINTAP_ENABLE_DIRECT_PARQUET=true`, and all six Linux sensor switches enabled.
- Direct-Parquet DuckDB validation read `File=75087`, `Process=620`, `TcpConnection=5`, and `UdpPacket=1` rows.
- Direct-Parquet activities included `File` `Close, Delete, Open, Read, Write`; `Process` `Refresh, Start, Stop`; `TcpConnection` `TcpIpAccept, TcpIpConnect, TcpIpDisconnect`; and `UdpPacket` `UdpIpSend`.
- `ProcessRundownSensor` completed and refreshed 302 existing `/proc` processes.
- Full ETL run used `WINTAP_DATA_ROOT=/tmp/lintap-data-full-sensors-etl-20260609-1`, `WINTAP_DISABLE_ETL=false`, `WINTAP_ENABLE_DIRECT_PARQUET=false`, and all six Linux sensor switches enabled.
- ETL DuckDB validation read `processserializer=438`, `processstopserializer=144`, `fileserializer=27001`, `tcpconnectionserializer=4`, and `udppacketserializer=1` rows.
- `coredumpctl list dotnet --since '2026-06-09 16:28:00' --no-pager` reported no dotnet coredumps after the full direct-Parquet, full ETL, and CloneSensor isolation runs.

Known exception:

- `CloneSensor` does not attach on this Fedora VM. The failing signature is `libbpf: prog 'trace_process_fork': failed to attach to tracepoint 'sched/sched_process_fork': -EACCES`.
- The CloneSensor failure reproduced with SELinux temporarily permissive, with `kernel.perf_event_paranoid` temporarily relaxed to `1`, and when running under explicit `cap_perfmon,cap_bpf,cap_sys_admin,cap_sys_resource` via `capsh`. SELinux and `kernel.perf_event_paranoid` were restored after testing.

### Stable

```bash
make run-host-only
```

Observed:

- Web host starts.
- Listens on `http://localhost:8099`.
- No immediate segfault when MCP, DuckDB UI, ETL, and sensors are disabled.

### Remaining Issues

- `CloneSensor` attach is blocked by `-EACCES` on `sched/sched_process_fork`; no clone-only Parquet is produced.
- ETL still logs repeated `Context by name 'Every10Seconds' already exists` warnings because multiple serializers attempt to create the same Esper context. This did not block serializer output.
- Full ETL runs with FileOps enabled can produce many `Could not resolve owner process` and `No PidHash found` warnings for file events whose owning process is absent from the DuckDB process resolver.

## Next Debugging Plan

### Step 1: Fix or replace CloneSensor attach

Focus on:

- `wintap/wintap/platform/linux/sensor/ebpf/CloneSensor.cs`
- `wintap/wintap/platform/linux/sensor/ebpf/tracers/clone_tracer.bpf.c`

Current failing signature:

```text
libbpf: prog 'trace_process_fork': failed to create BPF link for perf_event FD ...: -EACCES
libbpf: prog 'trace_process_fork': failed to attach to tracepoint 'sched/sched_process_fork': -EACCES
```

Already tested and not sufficient:

- SELinux permissive via `setenforce 0`.
- `kernel.perf_event_paranoid=1`.
- Running through `capsh` with explicit `cap_perfmon,cap_bpf,cap_sys_admin,cap_sys_resource`.

### Step 2: Reduce FileOps/process resolver warnings

Full ETL works, but FileOps can emit high-volume warnings before the owner PID is registered. Consider reusing Linux `/proc` enrichment for file events before `EventChannel.Send`, or rate-limiting unresolved owner process warnings.

### Step 3: Clean up repeated Esper context creation

Multiple serializers create `Every10Seconds`. The warning is non-fatal, but serializer startup should avoid redeploying the same context repeatedly.

## Useful Commands

Build all:

```bash
make clean
make all
```

Run default Fedora bring-up path:

```bash
make run
```

Run host only:

```bash
make run-host-only
```

Show runtime paths and MCP binary info:

```bash
make run-env
```

Check if app is listening:

```bash
curl http://localhost:8099
```

View logs/data root:

```bash
find /tmp/lintap-data -maxdepth 4 -type f | sort
```

Inspect recent coredump:

```bash
coredumpctl list dotnet
coredumpctl info dotnet
```

## Suggested Prompt For Next OpenCode Session In Fedora

```text
You are continuing Fedora 44 Linux bring-up for a .NET 8 + eBPF project. Read /home/grantj/git/LLNL/wintap/FEDORA_HANDOFF.md first. The build succeeds from the shared mount by using native /tmp output. Host-only, ETL, Execve, FileOps, Network, Exit, and ProcessRundown paths have produced validated Parquet without dotnet coredumps. The remaining sensor blocker is CloneSensor failing to attach sched_process_fork with libbpf -EACCES. Prefer minimal changes, keep Fedora bring-up switches documented, and update BUILD_AND_TEST.md with every new finding.
```
