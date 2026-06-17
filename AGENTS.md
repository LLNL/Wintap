# Agent Instructions for Wintap

## Scope

These instructions apply to the `wintap/` project in this workspace.

## Start Here

- Product overview and repo entry point: `wintap/README.md`
- Canonical build, run, and troubleshooting guide: `wintap/BUILD_AND_TEST.md`
- Developer tools and smoke tests: `wintap/devtools/README.md`
- Dated Linux bring-up notes: `wintap/fedora-handoff-2026-06.md`
- Dated TCP investigation notes: `wintap/linux-tcp-investigation-2026-06.md`

## Working Directory

- The active .NET projects and Makefile live in `wintap/wintap/`.
- Run build and run commands from `wintap/wintap`, or use `make -C wintap/wintap ...` from the repo root.

## High-Signal Context

- `Wintap` is the overall project.
- `Wintap.csproj` is the Windows build.
- `Lintap.csproj` is the Linux build.
- `Mactap.csproj` is the macOS build.
- Linux sensors live under `wintap/wintap/platform/linux/sensor/` and eBPF tracers live under `wintap/wintap/platform/linux/sensor/ebpf/tracers/`.

## Essential Commands

- Build everything: `make -C wintap/wintap all`
- Build .NET only: `make -C wintap/wintap build_dotnet`
- Build eBPF only: `make -C wintap/wintap build_ebpf`
- Run Lintap: `make -C wintap/wintap run`
- Run with eBPF privileges: `sudo make -C wintap/wintap run`
- Run host only: `make -C wintap/wintap run-host-only`
- Run MCP server directly: `make -C wintap/wintap run-mcp`
- Print runtime paths and flags: `make -C wintap/wintap run-env`

## Important Environment Variables

- `WINTAP_DATA_ROOT`: runtime data directory
- `NATIVE_BUILD_ROOT`: native filesystem output root used on shared mounts
- `WINTAP_DISABLE_MCP`: skip MCP startup
- `WINTAP_DISABLE_DUCKDB_UI`: skip DuckDB UI startup
- `WINTAP_DISABLE_ETL`: bypass ETL for isolation
- `WINTAP_DISABLE_SENSORS`: bypass sensor startup for isolation
- `WINTAP_ENABLE_DIRECT_PARQUET`: bypass Esper enrichment and write direct Parquet
- `WINTAP_ENABLE_<SENSOR>_SENSOR`: Linux per-sensor opt-in switches used by the Makefile
- `WINTAP_SKIP_PROCESS_RESOLVE`, `WINTAP_SKIP_PARENT_PROCESS_RESOLVE`, `WINTAP_SKIP_PROCESS_REGISTER`, `WINTAP_SKIP_ESPER_SEND`: narrow Linux isolation switches

## Troubleshooting Notes

- On shared mounts, the Makefile automatically redirects Linux .NET outputs to native `/tmp/lintap-build/wintap` paths.
- Avoid `dotnet run` on shared mounts because it triggers an implicit build without the Makefile safeguards.
- Use `make -C wintap/wintap run-env` to confirm the exact DLL and MCP paths being used.
- Use `make -C wintap/wintap run-mcp` to isolate MCP native startup issues from the main host.
- Treat `fedora-handoff-2026-06.md` and `linux-tcp-investigation-2026-06.md` as investigation notes, not canonical build instructions.
