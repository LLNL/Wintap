# Building Wintap

This note summarizes the current build workflow.

For Linux bring-up, shared-mount handling, MCP details, and troubleshooting, use the canonical guide: [`../BUILD_AND_TEST.md`](../BUILD_AND_TEST.md).

## Source Layout

- Repo root: `wintap/`
- Active source tree and Makefile: `wintap/wintap/`
- Windows project: `wintap/wintap/Wintap.csproj`
- Linux project: `wintap/wintap/Lintap.csproj`
- macOS project: `wintap/wintap/Mactap.csproj`

## Recommended Commands

From the repo root:

```bash
make -C wintap/wintap all
make -C wintap/wintap build_dotnet
make -C wintap/wintap build_ebpf
make -C wintap/wintap run-env
```

Or from `wintap/wintap`:

```bash
make all
make build_dotnet
make build_ebpf
make run-env
```

## Linux Notes

- Prefer the Makefile for Linux builds.
- On host-shared mounts, the Makefile automatically redirects .NET outputs to native `/tmp/lintap-build/wintap` paths.
- Avoid `dotnet run` on shared mounts because it bypasses the Makefile safeguards.

## Direct Project Builds

If you need a direct build for a specific platform project, run it from `wintap/wintap`:

```bash
dotnet build Wintap.csproj
dotnet build Lintap.csproj
dotnet build Mactap.csproj
```

For Linux, use this path only when you intentionally want a direct `dotnet build` instead of the Makefile-managed flow.
