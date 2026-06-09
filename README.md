<img width="200" src="https://user-images.githubusercontent.com/50601643/218871643-2d3af433-0923-4786-b5e5-24c6a72e803e.png">

# Wintap
A researcher-first data collection and analytics platform to assist with understanding the behavior of software
<br> Developed at Lawrence Livermore National Laboratory (LLNL)

## Overview
Wintap is designed for security research, behavioral analysis, and exploratory investigations. It provides full-fidelity host telemetry with an extensible plugin architecture, enabling researchers to rapidly prototype detection logic and analyze system behavior without the constraints of enterprise tooling.

## Wintap vs. Traditional EDR
| Feature | Enterprise EDR | Wintap |
|---------|----------------|--------|
| **Deployment Scale** | Enterprise-wide (1,000s-100,000s hosts) | Lab/research (1-100s hosts) |
| **Data Collection** | Optimized for efficiency | Full-fidelity capture |
| **Data Format** | Proprietary/optimized | Open (Parquet, CSV) |
| **Source Code** | Closed-source | Open-source |
| **Feature Maturity** | Production-hardened | Experimental, research-oriented |
| **Primary Use Case** | Security operations, compliance | Cyber research, data science |

Wintap is not designed to replace enterprise EDR solutions. It serves a different purpose: providing researchers with complete control over data collection, analysis, and experimentation.

## Wintap Architecture Diagram

```
┌────────────────────────────────────────────────────────────────────┐
│                         WINTAP SERVICE                             │
│                        (WinTapSvc.cs)                              │
└────────────────────────────────────────────────────────────────────┘
                                  │
                ┌─────────────────┼─────────────────┐
                │                 │                 │
                ▼                 ▼                 ▼
    ┌──────────────────┐  ┌─────────────┐ ┌─────────────--─┐
    │  PluginManager   │  │EventChannel │ │ Subscription   │
    │                  │  │(Esper CEP)  │ │   Manager      │
    │ - Load Plugins   │  │             │ │                │
    │ - MEF Discovery  │  │Route Events │ │ - Windows ETW  │
    │ - Isolation      │  │Enrich Data  │ │ - Linux        │
    │ - Scheduler      │  │Statistics   │ │ - macOS        │
    └──────────────────┘  └─────────────┘ └────────────--──┘
            │                     │                 │
            │                     │                 │
            ▼                     ▼                 ▼
    ┌─────────────────────────────────────────────────────┐
    │                   PLUGIN LAYER                      │
    │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
    │  │ISubscribe│  │   IRun   │  │ IQuery   │  ...      │
    │  └──────────┘  └──────────┘  └──────────┘           │
    └─────────────────────────────────────────────────────┘
                                  │
                                  ▼
    ┌──────────────────────────────────────────────────────┐
    │              ETL / SERIALIZATION LAYER               │
    │  - DefaultSerializer, ProcessSerializer, etc.        │
    │  - Parquet/CSV Writers                               │
    └──────────────────────────────────────────────────────┘
                                  │
                                  ▼
    ┌──────────────────────────────────────────────────────┐
    │              DATA ADAPTER LAYER                      │
    │  - File System (Parquet, CSV)                        │
    │  - Upload Adapters (IUpload interface)               │
    │  - Database Adapters                                 │
    │  - Network/API Adapters                              │
    └──────────────────────────────────────────────────────┘
```

## Key Capabilities

- **Plugin Architecture**  
  Write small .NET assemblies that subscribe to the events you need (process, network, file, registry). The framework handles infrastructure.

- **Unified Data Model**  
  All telemetry uses a consistent `WintapMessage` structure, making it straightforward to correlate across different data sources.

- **Real-Time Analysis**  
  Built-in Esper CEP engine allows live queries against event streams using EPL (Event Processing Language).

- **Platform Support**  
  Windows (stable), Linux (in development). Core infrastructure is platform-agnostic.

## System Requirements
.NET 8.0 or later
Windows 10/11 or Server 2019+ (64-bit)
Linux Ubuntu 24.04+ (in development)
4GB RAM minimum
Administrator/root privileges


## Quick Start
git clone https://github.com/LLNL/wintap.git
cd wintap
dotnet build -c Release

Windows deployment:
powershellsc.exe create Wintap binPath= "C:\Path\To\Wintap.exe" start=auto
sc.exe start Wintap

Linux deployment: 
See docs/LINUX_DEPLOYMENT.md

### Documentation
Developer Guide - see documents folder in repo

### Technology
.NET 8.0 | MEF | Esper CEP | DuckDB | TraceEvent | Parquet

### LLNL-CODE-837816
https://github.com/LLNL/wintap

## Building on Host-Shared Filesystems (Important for macOS/VM mounts)

When the repository is located on a host-shared mount (for example: macOS host -> Linux VM using 9p/virtiofs/osxfs, VirtualBox shared folders vboxsf, CIFS/SMB mounts, or other FUSE-backed mounts), .NET's build step that creates a native "apphost" binary can fail with memory-mapped file errors (IOException: Invalid argument). This is caused by limitations in the shared filesystem's support for memory-mapped file operations.

What we do in this repo
- The Makefile auto-detects common shared/host-mounted filesystem types (9p, virtiofs, vboxsf, fuse, smbfs, cifs, osxfs) and automatically sets the dotnet build flag `-p:UseAppHost=false` to avoid the apphost creation step when building from these mounts. A clear message is printed during `make` when this happens.

How this affects you
- Disabling the apphost prevents the native stub from being generated; the produced app will still run with `dotnet <dll>` but will not be a standalone native binary. CI or native builds on real Linux filesystems are unaffected.

Workarounds and overrides
- To force the apphost behavior (if you are building on a native filesystem or prefer to control the flag), set `DOTNET_BUILD_FLAGS` when invoking make, for example:

  make all DOTNET_BUILD_FLAGS='-p:UseAppHost=true'

- To permanently disable apphost for all builds on your machine, add a `UseAppHost` property to the project or Directory.Build.props. See the Makefile for more details.

If you run into build errors related to memory-mapped files and you're not on a native VM filesystem, try copying the repository onto the VM's local filesystem (e.g. `/home/${USER}/src`) and building there for full fidelity.
