# Build And Test (Developer Notes)

This document collects common build and test guidance for developers working on Wintap, with special attention to known issues when the repository is on a host-shared filesystem (macOS host -> Linux VM shared mount, VirtualBox shared folders, CIFS/SMB, etc.).

Linux build artifacts

For Linux development, use native filesystem build outputs under `/tmp` as the default. This is safe for Ubuntu and required for Fedora shared-mount bring-up. The source tree can stay on the shared filesystem, but compiled assemblies and `obj` intermediates should be loaded from native storage.

The Makefile automatically detects shared mount filesystems and redirects build output to:

   /tmp/lintap-build/wintap

The runtime data root remains separate, usually:

   /tmp/lintap-data

This keeps the shared repository mostly source-only and avoids .NET apphost mmap failures and Fedora NEsper/Roslyn runtime compilation failures caused by loading assemblies from the shared mount.

Quick build

1. Build everything (eBPF + .NET):

   make all

   On shared-mounted Linux repos, `.NET` output is written to `/tmp/lintap-build/wintap` by default.

2. Build only .NET:

   make build_dotnet

3. Run Lintap from the already-built DLL:

   make run

   On shared-mounted Linux repos, this runs `/tmp/lintap-build/wintap/bin/Debug/net8.0/Lintap.dll`.

   Use `sudo make run` if the sensor needs root privileges to load eBPF programs or read privileged system state.

   For early Fedora/Linux testing, the Makefile defaults to `WINTAP_DISABLE_MCP=true`, which skips MCP/AI startup and also sets `DISABLE_MCP=true` during build so the MCP server is not published. Override with `WINTAP_DISABLE_MCP=false` when testing MCP integration.

   The Makefile also defaults to `WINTAP_DISABLE_DUCKDB_UI=true` for Fedora/VM bring-up because DuckDB's optional UI extension loads native code and is not required for sensor validation. Override with `WINTAP_DISABLE_DUCKDB_UI=false` when testing the DuckDB UI server.

   If native crashes continue during early startup, temporarily bypass ETL with `WINTAP_DISABLE_ETL=true make run`. This should leave the web host and sensor startup path available for isolation testing.

   To bypass the native-heavy eBPF sensor startup path too, run `WINTAP_DISABLE_SENSORS=true make run` or use `make run-host-only`.

   Fedora bring-up currently keeps individual Linux sensors opt-in even when `WINTAP_DISABLE_SENSORS=false`. Enable one sensor at a time with:

   WINTAP_ENABLE_EXECVE_SENSOR=true WINTAP_DISABLE_ETL=true make run

   Available per-sensor switches:

   - `WINTAP_ENABLE_EXECVE_SENSOR`
   - `WINTAP_ENABLE_CLONE_SENSOR`
   - `WINTAP_ENABLE_EXIT_SENSOR`
   - `WINTAP_ENABLE_NETWORK_SENSOR`
   - `WINTAP_ENABLE_FILEOPS_SENSOR`
   - `WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR`

   Fedora bring-up also has a direct Parquet mode for validating live eBPF feeds while the Esper ETL path is isolated:

   WINTAP_DISABLE_ETL=true WINTAP_ENABLE_DIRECT_PARQUET=true WINTAP_ENABLE_EXECVE_SENSOR=true make run

   `WINTAP_ENABLE_DIRECT_PARQUET=true` writes raw flattened `WintapMessage` records to Parquet and bypasses Esper/process-history enrichment inside `EventChannel.Send`. This is a bring-up path, not the final ETL serializer path.

   `EventChannel.Send` also has narrow Fedora isolation switches for normal sensor event flow:

   - `WINTAP_SKIP_PROCESS_RESOLVE`
   - `WINTAP_SKIP_PARENT_PROCESS_RESOLVE`
   - `WINTAP_SKIP_PROCESS_REGISTER`
   - `WINTAP_SKIP_ESPER_SEND`

4. Run the MCP server directly for startup diagnostics:

   make run-mcp

5. Print runtime paths and MCP binary metadata:

   make run-env

6. Build only eBPF tracers:

   make build_ebpf

Known host-shared filesystem issue

.NET's SDK creates a small native "apphost" binary during `dotnet build`. This step may fail on filesystems that do not fully support memory-mapped file operations (mmap). Typical environments where this happens:

- macOS host → Linux VM shared mount: 9p, virtiofs, osxfs
- VirtualBox shared folders: vboxsf
- SMB/CIFS mounts: cifs, smbfs
- FUSE-backed mounts

Symptom

- Build error during CreateAppHost with IOException: Invalid argument
- Backtrace referencing MemoryMappedFiles.MemoryMappedView.CreateView or Microsoft.NET.HostModel.AppHost.MachOUtils.RemoveSignature

Short-term workarounds

- Disable apphost when building on shared mounts (this repo's Makefile auto-detects common shared mounts and will set the build flag automatically). You will see a message like:

  Detected filesystem type '9p' for /home/grantj/git/LLNL/wintap/wintap — disabling apphost to avoid mmap errors

- The repository also includes a Directory.Build.props that reads the environment variable
  `DISABLE_APPHOST`. The Makefile will export `DISABLE_APPHOST=true` when it detects a
  shared mount, so both `dotnet build` and any inner `dotnet publish` calls respect the
  setting. You can override this behavior by setting `DOTNET_BUILD_FLAGS` or the
  `DISABLE_APPHOST` environment variable yourself.

Special handling for the MCP server (single-file requirement)

The Lintap project builds an MCP server (shared/ai/wintap_mcp_server) as a single-file
self-contained executable. To produce that single-file even when the repo is on a
host-shared mount, the build does the following:

- The Makefile will detect shared mounts and set `DOTNET_BUILD_FLAGS='-p:UseAppHost=false'`
  for the outer build to avoid failures during compile on the shared filesystem.
- However, the MCP server publish step is executed into a VM-local temporary directory
  (by default `/tmp/<ProjectName>-mcp_temp`) and forces `--self-contained true`,
  `-p:PublishSingleFile=true`, and `-p:UseAppHost=true` for that publish. It also
  redirects the MCP server's `obj` and `bin` paths into that same VM-local temp tree.
  This is important because the .NET SDK can create the apphost in intermediate `obj`
  paths before copying it to the final publish directory. The produced single-file
  executable is then copied back into the repo's output directory.

If you prefer a custom temporary publish directory, set the `MCP_PUBLISH_TMP` env var
before running make, for example:

  export MCP_PUBLISH_TMP=/home/grantj/src/wintap-mcp-temp/
  make all

This is useful if you want the native artifacts to be placed on a specific VM-local
path or a ramdisk.

- Manually set the flag when invoking make:

  make all DOTNET_BUILD_FLAGS='-p:UseAppHost=false'

- Copy the repository to a native filesystem inside the VM and build there (recommended for final verification):

  rsync -a /home/grantj/git/LLNL/wintap /home/${USER}/src/wintap
  cd /home/${USER}/src/wintap/wintap
  make all

Makefile configuration

- SHARED_MOUNT_TYPES (Makefile) - pipe-separated list of filesystem types to treat as shared mounts. Default:

  9p|virtiofs|vboxsf|fuse|smbfs|cifs|osxfs

- DOTNET_BUILD_FLAGS - flags passed to `dotnet build`. You can override on the command line to force behavior.

- NATIVE_BUILD_ROOT - native filesystem root for build outputs and intermediates. Default:

  /tmp/lintap-build/<project-directory-name>

- NativeBuildRoot - MSBuild property used by `Directory.Build.props` to place per-project native `bin`/`obj` subtrees under `NATIVE_BUILD_ROOT`.

Examples

- Force apphost on (if you know you're on a native filesystem):

  make all DOTNET_BUILD_FLAGS='-p:UseAppHost=true'

- Force disable apphost on any filesystem:

   make all DOTNET_BUILD_FLAGS='-p:UseAppHost=false'

- Force a custom native build root:

   make all NATIVE_BUILD_ROOT=/tmp/my-lintap-build

- Inspect where the app will run from:

   make run-env

Troubleshooting checklist

1. Confirm .NET SDK version:

   dotnet --info

2. If CreateAppHost fails with mmap errors and you're on a shared mount, either build with UseAppHost=false or move to a native filesystem.

   Avoid `dotnet run` directly from a host-shared mount because it performs an implicit build and can attempt apphost creation without the Makefile's safeguards. Use `make run`, or run the built DLL directly with `dotnet bin/Debug/net8.0/Lintap.dll`.

3. If eBPF build fails, ensure you have the right kernel-headers/kernel-devel and libbpf-devel packages installed for your distro/kernel.

4. If running inside a VM, ensure your shared mount type is reflected in /proc/mounts and, if needed, add it to SHARED_MOUNT_TYPES in the Makefile.

5. If `make run` exits with a segmentation fault, use `make run-mcp` first. If the MCP server also segfaults, focus on native dependencies in the single-file MCP executable (especially DuckDB.NET native runtime extraction/RID support). If only Lintap segfaults, inspect the host startup path, eBPF sensors, ProcessResolver DuckDB initialization, and DuckDB UI startup.

6. To skip MCP/AI during local Linux bring-up, use the default Makefile behavior or set explicitly:

   WINTAP_DISABLE_MCP=true make run

   To include MCP again:

   WINTAP_DISABLE_MCP=false make clean all run

7. To isolate native aborts during ETL/DuckDB startup:

   WINTAP_DISABLE_DUCKDB_UI=true make run

   WINTAP_DISABLE_ETL=true make run

8. To isolate host startup from eBPF/libbpf sensor startup:

   WINTAP_DISABLE_SENSORS=true WINTAP_DISABLE_ETL=true make run

   Or use the convenience target:

   make run-host-only

9. Fedora 44 runtime isolation findings from 2026-06-09:

   - `WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false make run` previously segfaulted when Linux sensor startup loaded every sensor by default.
   - Linux sensor startup now has per-sensor opt-in switches. With all per-sensor switches unset, `WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false make run` survived until a 45 second timeout and shut down cleanly.
   - Individual eBPF sensor checks for `ExecveSensor`, `CloneSensor`, `ExitSensor`, `NetworkSensor`, and `FileOpsSensor` each survived their startup timeout with ETL disabled when eBPF objects were not present and the sensors returned after logging missing BPF objects.
   - After `make build_ebpf`, `ExecveSensor` loaded and started, but the normal `EventChannel.Send` path still produced a SIGSEGV after the first generated exec event. The crash reproduced with `WINTAP_ENABLE_DIRECT_PARQUET=false`, so direct Parquet was not the cause.
   - `ProcessRundownSensor` is currently suspect. `WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR=true` with ETL disabled reached `Linux process rundown starting` in `/tmp/lintap-data/Logs/Lintap.log`, did not log completion, and produced a `SIGSEGV` coredump entry for `dotnet`.
   - `WINTAP_DISABLE_ETL=false WINTAP_DISABLE_SENSORS=true` still produced a `SIGSEGV` entry after ETL startup. The earlier abort in `PluginExceptionHandler.LogPluginException` was fixed by making stack-trace logging null/formatting-safe, which exposed the underlying repeated Esper/Roslyn `Bad IL range` unobserved task exceptions in `/tmp/lintap-data/Logs/Lintap.log`.
   - `WINTAP_DISABLE_ESPER_ENUM_CAST=true` was tested as an EPL compile-time rewrite. Removing `CAST(MessageType, string)` avoided one Roslyn `Bad IL range` shape but exposed that NEsper does not accept direct enum/string comparisons. Rewriting to fully-qualified enum literals was also rejected by this NEsper build. Leave this switch off unless specifically debugging EPL formatting.
   - Current ETL suspect area: Esper EPL compile/deploy during ETL/plugin event routing, not DuckDB UI or MCP, because `WINTAP_DISABLE_DUCKDB_UI=true` and `WINTAP_DISABLE_MCP=true` were set during the failing run.
   - Direct Parquet mode produced validated Parquet output for live eBPF feeds with ETL disabled and the normal EventChannel enrichment/Esper path bypassed. `ExecveSensor` produced `/tmp/lintap-data/parquet/process/process-134255067557426588.parquet`, validated with DuckDB as 4 `Process` rows. `FileOpsSensor` produced `/tmp/lintap-data/parquet/file/*.parquet`, validated with DuckDB as readable `File` rows.
   - SELinux was set to permissive with `setenforce 0` on Fedora and is not the primary cause. ETL-only still failed with `Bad IL range` in `Watchdog..ctor()` and `FocusChangeSerializer`; normal `ExecveSensor` still segfaulted after event flow; direct Parquet mode still worked and produced `/tmp/lintap-data-selinux-off/parquet/process/process-134255097089967720.parquet`, validated with DuckDB as 4 `Process` rows.
   - `EventChannel.Send` isolation narrowed the normal `ExecveSensor` segfault to parent process resolution for `Process` events. `WINTAP_SKIP_PROCESS_RESOLVE=true` survived. `WINTAP_SKIP_PROCESS_REGISTER=true` alone still segfaulted. `WINTAP_SKIP_ESPER_SEND=true` alone still segfaulted. `WINTAP_SKIP_PARENT_PROCESS_RESOLVE=true` survived while normal process registration and Esper send remained enabled.
   - Parent-process resolution fix: Linux process sensors now populate parent process context from `/proc` before `EventChannel.Send`, and `ExecveSensor` captures PPID in-kernel in `execve_tracer.bpf.c` so short-lived exec events do not lose `parent_process_id` when `/proc/<child>` disappears. `EventChannel.Send` now trusts an existing `ParentPidHash` instead of querying DuckDB for the parent. Verified normal `ExecveSensor` flow with all EventChannel skip switches false: no new coredump, and DuckDB process records had nonzero `parent_process_id` plus `parent_pid_hash` for `id`, `uname`, `true`, and `sleep`.
   - Minimal Esper repro mode was added with `WINTAP_ESPER_REPRO=true`. It runs before `ProcessResolver`, hosted service startup, ETL workers, DuckDB registration, or sensors. On Fedora, even `SELECT * FROM WintapMessage` fails in `EventChannel.CompileDeploy` with `EPCompileException` / `Bad IL range` or `Common Language Runtime detected an invalid program`. Therefore the ETL blocker is inside NEsper compile/deploy on this Fedora runtime, not ETL worker ordering, DuckDB, sensors, or plugin registration.
   - Runtime toggles such as `COMPlus_ReadyToRun=0`, `COMPlus_TieredCompilation=0`, and `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` were tested. They did not produce a stable full-ETL fix; `COMPlus_TieredCompilation=0` also produced a NEsper timer callback `TypeLoadException` in full runtime testing. Do not treat these as fixes.
   - A standalone repro was added at `diagnostics/nesper-repro`. It proves the NEsper failure is not caused by `WintapMessage` shape: `SELECT * FROM SimpleEvent` also fails when the repro is built and loaded from the Fedora shared mount. Copying the repro and `shared/WintapAPI` to native `/tmp/opencode`, building there, and running from the native output makes all SimpleEvent and WintapMessage queries pass. Running the native-built DLL from the shared repo current directory also passes, while running the shared-built DLL from native `/tmp/opencode` still fails. The current best explanation is assembly/output location on the shared mount, not current working directory, .NET version, architecture, SELinux, DuckDB, sensors, or ETL worker order.
   - Fedora shared-mount fix: `Directory.Build.props` now supports `NativeBuildRoot`, and the Makefile automatically sets `NativeBuildRoot=/tmp/lintap-build/wintap/` plus a native `OutputPath` when the repo is on a shared mount. This keeps source on the shared mount but loads runtime assemblies from native `/tmp`. `make run` now executes `/tmp/lintap-build/wintap/bin/Debug/net8.0/Lintap.dll` on this Fedora VM.
   - Native-output validation: `WINTAP_ESPER_REPRO=true` passes all in-app repro queries from `/tmp/lintap-build/wintap/bin/Debug/net8.0/Lintap.dll`. ETL-only with sensors disabled stayed up to timeout with no `Bad IL range` or segfault. ETL plus `ExecveSensor` produced validated process Parquet at `/tmp/lintap-data-etl-execve-native/parquet/processserializer/*.parquet`; DuckDB read 5 `PROCESS` rows and all 5 had `parentpidhash` populated. No new coredump was produced after the native-output run.
   - Stale shared-mount `bin`/`obj` artifact directories were moved to `/tmp/wintap-shared-artifacts-*`. The Fedora shared mount can rematerialize empty `wintap/bin` and `wintap/obj` directory skeletons, but current Makefile builds no longer rely on them; generated assemblies are in `/tmp/lintap-build/wintap`.
   - Remaining ETL warning: multiple serializers attempt to create the same `Every10Seconds` Esper context, so logs show `Context by name 'Every10Seconds' already exists`. This is not the Fedora invalid-IL failure and did not prevent process serializer Parquet output.
   - Full Linux sensor validation from 2026-06-09: `make build_ebpf build_dotnet` succeeded from the Fedora shared mount with native output under `/tmp/lintap-build/wintap`. A timed direct-Parquet run with all six Linux sensor switches enabled used `WINTAP_DATA_ROOT=/tmp/lintap-data-full-sensors-20260609-2`, `WINTAP_DISABLE_ETL=true`, `WINTAP_ENABLE_DIRECT_PARQUET=true`, and generated process, file, TCP, and UDP stimulus. DuckDB validated raw Parquet rows by `MessageType`: `File=75087` with `Close, Delete, Open, Read, Write`; `Process=620` with `Refresh, Start, Stop`; `TcpConnection=5` with `TcpIpAccept, TcpIpConnect, TcpIpDisconnect`; `UdpPacket=1` with `UdpIpSend`. `ProcessRundownSensor` completed and refreshed 302 existing `/proc` processes.
   - Full ETL serializer validation from the same session used `WINTAP_DATA_ROOT=/tmp/lintap-data-full-sensors-etl-20260609-1`, `WINTAP_DISABLE_ETL=false`, `WINTAP_ENABLE_DIRECT_PARQUET=false`, and all six Linux sensor switches enabled. DuckDB validated serializer output: `processserializer=438`, `processstopserializer=144`, `fileserializer=27001`, `tcpconnectionserializer=4`, and `udppacketserializer=1`. `coredumpctl list dotnet --since '2026-06-09 16:28:00' --no-pager` reported no coredumps after the direct-Parquet, ETL, and isolation runs.
   - Remaining Linux sensor blocker: `CloneSensor` still fails to attach `trace_process_fork` to `sched/sched_process_fork` with libbpf `-EACCES`, so no clone-only Parquet was produced. The failure reproduced in the combined direct-Parquet run, the full ETL run, a Clone-only run with SELinux temporarily permissive, a Clone-only run with `kernel.perf_event_paranoid` temporarily relaxed to `1`, and a Clone-only run under explicit `cap_perfmon,cap_bpf,cap_sys_admin,cap_sys_resource` via `capsh`. SELinux and `kernel.perf_event_paranoid` were restored after the isolation checks.

10. Useful Fedora isolation commands after the per-sensor switches:

   Sensor manager enabled, all individual Linux sensors off:

   WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false make run

   One eBPF sensor at a time:

   WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_EXECVE_SENSOR=true make run

   Direct Parquet smoke test for a process feed:

   WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_DIRECT_PARQUET=true WINTAP_DIRECT_PARQUET_FLUSH_SECONDS=10 WINTAP_ENABLE_EXECVE_SENSOR=true make run

   Direct Parquet smoke test for a file feed:

   WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_DIRECT_PARQUET=true WINTAP_DIRECT_PARQUET_FLUSH_SECONDS=10 WINTAP_ENABLE_FILEOPS_SENSOR=true make run

   Validate generated Parquet with DuckDB:

   duckdb -c "SELECT count(*) AS rows, min(MessageType) AS message_type FROM read_parquet('/tmp/lintap-data/parquet/process/*.parquet');"

   duckdb -c "SELECT count(*) AS rows, min(MessageType) AS message_type FROM read_parquet('/tmp/lintap-data/parquet/file/*.parquet');"

   Normal event-flow test that keeps process registration and Esper send enabled but skips only parent lookup:

   WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_DIRECT_PARQUET=false WINTAP_SKIP_PARENT_PROCESS_RESOLVE=true WINTAP_ENABLE_EXECVE_SENSOR=true make run

   Verify parent process registration after the Linux parent fix:

   duckdb /tmp/lintap-data-parent-fix2/event_store/main.duckdb -c "SELECT process_id, parent_process_id, process_name, parent_pid_hash FROM process ORDER BY create_time DESC LIMIT 20;"

   Minimal Esper compile/deploy repro:

   WINTAP_DATA_ROOT=/tmp/lintap-data-esper-repro WINTAP_DISABLE_MCP=true WINTAP_ESPER_REPRO=true dotnet bin/Debug/net8.0/Lintap.dll

   Expected Fedora failure signature:

   WINTAP_ESPER_REPRO_RESULT|0|FAIL|com.espertech.esper.compiler.client.EPCompileException|Bad IL range...|SELECT * FROM WintapMessage

   Standalone NEsper repro from shared output:

   dotnet build diagnostics/nesper-repro/nesper-repro.csproj
   dotnet diagnostics/nesper-repro/bin/Debug/net8.0/nesper-repro.dll

   Native-output comparison:

   mkdir -p /tmp/opencode/nesper-repro-run /tmp/opencode/shared
   cp -a diagnostics/nesper-repro /tmp/opencode/nesper-repro-run/nesper-repro
   cp -a shared/WintapAPI /tmp/opencode/shared/WintapAPI
   dotnet build /tmp/opencode/nesper-repro-run/nesper-repro/nesper-repro.csproj
   dotnet /tmp/opencode/nesper-repro-run/nesper-repro/bin/Debug/net8.0/nesper-repro.dll

   Native-output Lintap build/run on shared-mounted Fedora repo:

   make build_dotnet

   WINTAP_DATA_ROOT=/tmp/lintap-data-native-output-repro WINTAP_DISABLE_MCP=true WINTAP_ESPER_REPRO=true dotnet /tmp/lintap-build/wintap/bin/Debug/net8.0/Lintap.dll

   ETL plus ExecveSensor Parquet validation:

   WINTAP_DATA_ROOT=/tmp/lintap-data-etl-execve-native WINTAP_DISABLE_ETL=false WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_EXECVE_SENSOR=true make run

   duckdb -c "SELECT count(*) AS rows, min(MessageType) AS message_type, count(parentpidhash) AS parent_hash_rows FROM read_parquet('/tmp/lintap-data-etl-execve-native/parquet/processserializer/*.parquet');"

   Full direct-Parquet sensor validation with all Linux sensor switches enabled:

   WINTAP_DATA_ROOT=/tmp/lintap-data-full-sensors-20260609-2 WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_DIRECT_PARQUET=true WINTAP_DIRECT_PARQUET_FLUSH_SECONDS=5 WINTAP_ENABLE_EXECVE_SENSOR=true WINTAP_ENABLE_CLONE_SENSOR=true WINTAP_ENABLE_EXIT_SENSOR=true WINTAP_ENABLE_NETWORK_SENSOR=true WINTAP_ENABLE_FILEOPS_SENSOR=true WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR=true make run

   duckdb -c "SELECT MessageType, count(*) AS rows, count(DISTINCT ActivityType) AS activity_types, string_agg(DISTINCT ActivityType, ', ' ORDER BY ActivityType) AS activities FROM read_parquet('/tmp/lintap-data-full-sensors-20260609-2/parquet/*/*.parquet', union_by_name=true) GROUP BY MessageType ORDER BY MessageType;"

   Full ETL serializer validation with all Linux sensor switches enabled:

   WINTAP_DATA_ROOT=/tmp/lintap-data-full-sensors-etl-20260609-1 WINTAP_DISABLE_ETL=false WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_DIRECT_PARQUET=false WINTAP_ENABLE_EXECVE_SENSOR=true WINTAP_ENABLE_CLONE_SENSOR=true WINTAP_ENABLE_EXIT_SENSOR=true WINTAP_ENABLE_NETWORK_SENSOR=true WINTAP_ENABLE_FILEOPS_SENSOR=true WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR=true make run

   duckdb -c "SELECT 'processserializer' AS dataset, count(*) AS rows FROM read_parquet('/tmp/lintap-data-full-sensors-etl-20260609-1/parquet/processserializer/*.parquet') UNION ALL SELECT 'processstopserializer', count(*) FROM read_parquet('/tmp/lintap-data-full-sensors-etl-20260609-1/parquet/processstopserializer/*.parquet') UNION ALL SELECT 'fileserializer', count(*) FROM read_parquet('/tmp/lintap-data-full-sensors-etl-20260609-1/parquet/fileserializer/*.parquet') UNION ALL SELECT 'tcpconnectionserializer', count(*) FROM read_parquet('/tmp/lintap-data-full-sensors-etl-20260609-1/parquet/tcpconnectionserializer/*.parquet') UNION ALL SELECT 'udppacketserializer', count(*) FROM read_parquet('/tmp/lintap-data-full-sensors-etl-20260609-1/parquet/udppacketserializer/*.parquet') ORDER BY dataset;"

   Process rundown only:

   WINTAP_DISABLE_ETL=true WINTAP_DISABLE_SENSORS=false WINTAP_ENABLE_PROCESS_RUNDOWN_SENSOR=true make run

   ETL only, sensors off:

   WINTAP_DISABLE_ETL=false WINTAP_DISABLE_SENSORS=true make run

   After a crash:

   coredumpctl list dotnet --no-pager

   coredumpctl info <PID> --no-pager

   Check or relax SELinux for comparison only:

   getenforce

   setenforce 0

   Re-enable if needed:

   setenforce 1

Further improvements (future work)

- Provide a simple script to copy the repo to a VM-local path for reproducible native builds.
- Investigate `CloneSensor` attach failure for `sched/sched_process_fork` on Fedora 44. Current evidence points to a tracer/kernel attach issue rather than SELinux, `kernel.perf_event_paranoid`, or missing effective capabilities.
- Reduce repeated file/process resolver warnings in full ETL runs when FileOps emits events for processes that have not yet been registered in the DuckDB process resolver.

If you hit a build problem that isn't covered here, capture the failing `dotnet build` output and post it to the team — we'll extend this document with the fix.
