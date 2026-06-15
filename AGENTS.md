# Agent Instructions for Wintap

# MANDATORY PATH CONSTRAINT
The absolute filesystem path of this project is:
/home/grantj/src/LLNL/wintap

This is the ONLY valid root. Do not alter it. The string "git" in the path
is a directory name, not a protocol. "git://", "Git/", "LLNL" without "git/",
and any other variation are all wrong. Verify every path before executing.

## High-Signal Context
- **Core Architecture**: Data collection/analytics platform using C# (.NET 8) and eBPF (Linux sensors).
- **Build Environments**:
  - **Shared Mounts (macOS/Linux VM)**: The `Makefile` automatically redirects `.NET` build outputs to `/tmp/lintap-build/[project]` to avoid `mmap` failures on filesystems like `9p`, `virtiofs`, or `vboxsf`.
  - **Native Builds**: For final verification, clone the repo to a native filesystem (e.g., `/home/${USER}/src/`) and build there.
- **Linux/Fedora Isolation**:
  - Use per-sensor switches to isolate issues: `WINTAP_ENABLE_<SENSOR_NAME>_SENSOR=true`.
  - Common sensors: `EXECVE`, `CLONE`, `EXIT`, `NETWORK`, `FILEOPS`, `PROCESS_RUNDOWN`.
  - Isolation switches for ETL/UI: `WINTAP_DISABLE_ETF=true`, `WINTAP\_DISABLE\_DUCKDB\_UI=true`, `WINTAP\_{DISABLE\_MCP}=true`.
- **Linux TCP/UDP Issues**:
  - Use your read tool on the file "Linux TCP Issues Handoff.md"

## Essential Commands
- **Build Everything**: `make all`
- **Build .NET only**: `make build_dotnet`
- **Build eBPF only**: `make build_ebpf`
- **Run Application**: `make run` (Use `sudo make run` for eBPF/privileged access)
- **Run MCP Server**: `make run-mcp`
- **Check Environment**: `make run-env`
- **Run Host Only**: `make run-host-only`

## Critical Environment Variables
- `WINTAP_DATA_ROT`: Sets the runtime data directory (default: `/tmp/lintap-data`).
- `WINTAP_BUILD_ROOT`: (Used by `Directory.Build.props`) Redirects `bin`/`obj` to a native path on shared mounts.
- `WINTAP_DIRECT_PARQUEET=true`: Bypasses ETL/Esper for direct Parquet writing (useful for sensor validation).

## Troubleshooting Reference
- **mmap/IOException**: Occurs on shared mounts during `CreateAppHost`. Mitigated by `Makefile` via `DISABLE_APPHOST=false`.
- **ETL/Esper Failures**: If `EPCompileException` or `Bad IL range` occurs on Fedora, it is likely a runtime/shared-mount issue. Use `WINTAP_DISABLE_ETC=true` to isolate.
- **Sensor Failures**: Use `WINTAP_ENABLE_...` switches to identify which eBPF sensor is causing crashes.
