# Wintap Developer Guide

This guide provides a current, high-signal orientation for developers working in the Wintap codebase.

For day-to-day Linux build, run, and troubleshooting steps, use [`../BUILD_AND_TEST.md`](../BUILD_AND_TEST.md).

## Project Layout

- Repo root: `wintap/`
- Active source tree: `wintap/wintap/`
- Windows project: `wintap/wintap/Wintap.csproj`
- Linux project: `wintap/wintap/Lintap.csproj`
- macOS project: `wintap/wintap/Mactap.csproj`

Key source areas:

- Core infrastructure: `wintap/wintap/core/infrastructure/`
- ETL and serialization: `wintap/wintap/core/etl/`
- Shared utilities and config helpers: `wintap/wintap/core/shared/`
- Linux platform code: `wintap/wintap/platform/linux/`
- Windows platform code: `wintap/wintap/platform/windows/`
- eBPF tracers: `wintap/wintap/platform/linux/sensor/ebpf/tracers/`

## Architecture Overview

At a high level, Wintap is organized into four layers:

1. Platform-specific collectors and sensors
2. Core event routing and enrichment in `EventChannel`
3. ETL and serialization to Parquet and related outputs
4. Optional adapters, plugins, and MCP integrations

Important core components:

- `SubscriptionManager`: starts platform-specific collection paths
- `EventChannel`: central routing, enrichment, and Esper integration
- `ProcessResolver`: process identity and lookup support
- `WintapSvcCore`: host/service startup orchestration
- serializer classes under `core/etl/extract/` and `core/etl/load/`

For deeper architecture notes and ongoing design review, see [`design/architecture-assessment.md`](design/architecture-assessment.md).

## Recommended Build Workflow

For Linux work, prefer the Makefile in `wintap/wintap`.

From the repo root:

```bash
make -C wintap/wintap all
make -C wintap/wintap build_dotnet
make -C wintap/wintap build_ebpf
make -C wintap/wintap run-env
```

From inside `wintap/wintap`:

```bash
make all
make build_dotnet
make build_ebpf
make run-env
```

Direct project builds are still available when needed:

```bash
cd wintap/wintap
dotnet build Wintap.csproj
dotnet build Lintap.csproj
dotnet build Mactap.csproj
```

## Linux Development Notes

- On shared mounts, the Makefile redirects Linux output to native `/tmp/lintap-build/wintap` paths.
- Avoid `dotnet run` on shared mounts because it can recreate apphost and runtime-codegen failures.
- Use `make -C wintap/wintap run-env` to confirm the actual runtime DLL and MCP paths before debugging packaging or deployment issues.

Useful isolation commands:

```bash
make -C wintap/wintap run-host-only
WINTAP_DISABLE_ETL=true make -C wintap/wintap run
WINTAP_DISABLE_SENSORS=true make -C wintap/wintap run
WINTAP_ENABLE_EXECVE_SENSOR=true make -C wintap/wintap run
```

## Key Runtime Variables

- `WINTAP_DATA_ROOT`
- `NATIVE_BUILD_ROOT`
- `WINTAP_DISABLE_MCP`
- `WINTAP_DISABLE_DUCKDB_UI`
- `WINTAP_DISABLE_ETL`
- `WINTAP_DISABLE_SENSORS`
- `WINTAP_ENABLE_DIRECT_PARQUET`
- `WINTAP_ENABLE_<SENSOR>_SENSOR`
- `WINTAP_SKIP_PROCESS_RESOLVE`
- `WINTAP_SKIP_PARENT_PROCESS_RESOLVE`
- `WINTAP_SKIP_PROCESS_REGISTER`
- `WINTAP_SKIP_ESPER_SEND`

These are described in more detail in [`../BUILD_AND_TEST.md`](../BUILD_AND_TEST.md).

## Developer Validation

For Linux telemetry changes, pair your code changes with the narrowest useful validation:

- `make -C wintap/wintap build_dotnet`
- `make -C wintap/wintap build_ebpf`
- `make -C wintap/wintap run-env`
- the relevant smoke tests in [`../devtools/README.md`](../devtools/README.md)

Common smoke-test entry points:

- `devtools/network_capture_smoke_test.py`
- `devtools/process_capture_smoke_test.py`
- `devtools/file_capture_smoke_test.py`

## Working with Investigation Notes

The repo contains both canonical docs and dated investigation notes.

Treat these as investigation context, not general-purpose setup docs:

- [`../fedora-handoff-2026-06.md`](../fedora-handoff-2026-06.md)
- [`../linux-tcp-investigation-2026-06.md`](../linux-tcp-investigation-2026-06.md)
- files under `diagnostics/`
- files under `docs-agent/`

If a debugging session changes the current operating guidance, update the canonical docs as well.

## Documentation Map

- Project overview: [`../README.md`](../README.md)
- Build and troubleshooting: [`../BUILD_AND_TEST.md`](../BUILD_AND_TEST.md)
- Linux deployment: [`Linux Deployment Guide.md`](Linux%20Deployment%20Guide.md)
- Contribution guide: [`CONTRIBUTIONS.md`](CONTRIBUTIONS.md)
- Architecture notes: [`design/architecture-assessment.md`](design/architecture-assessment.md)

## Contribution Expectations

- Keep changes narrow and explain validation clearly.
- Update docs when commands, env vars, or operational guidance change.
- Prefer fixing duplicated guidance by consolidating around the canonical docs instead of copying new instructions into multiple files.
