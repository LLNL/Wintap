<img width="200" src="https://user-images.githubusercontent.com/50601643/218871643-2d3af433-0923-4786-b5e5-24c6a72e803e.png">

# Wintap
A researcher-first data collection and analytics platform to assist with understanding the behavior of software
Developed at Lawrence Livermore National Laboratory (LLNL)

Overview
Wintap is designed for security research, behavioral analysis, and exploratory investigations. It provides full-fidelity host telemetry with an extensible plugin architecture, enabling researchers to rapidly prototype detection logic and analyze system behavior without the constraints of enterprise tooling.

Wintap vs. Traditional EDR
Enterprise EDRWintapScaleEnterprise-wide deploymentLab/research environmentData CollectionOptimized for efficiencyFull-fidelity captureData FormatProprietary/optimizedOpen (Parquet, CSV)Source CodeClosed-sourceOpen-sourceFeaturesMature, production-hardenedExperimental, research-oriented
Wintap is not designed to replace enterprise EDR solutions. It serves a different purpose: providing researchers with complete control over data collection, analysis, and experimentation.

# Wintap Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              WINTAP SERVICE                              │
│                                .NET 8.0                                  │
└─────────────────────────────────────────────────────────────────────────┘
                                     │
        ┌────────────────────────────┼────────────────────────────┐
        │                            │                            │
        ▼                            ▼                            ▼
┌──────────────────┐      ┌──────────────────┐      ┌──────────────────┐
│ Platform Sensors │      │  EventChannel    │      │  Wintap Plugins  │
│                  │      │  ═════════════    │      │  ══════════════  │
│  Windows:        │──────│  • Esper CEP     │──────│  • ISubscribe    │
│  • ETW           │      │  • Process       │      │  • ISubscribeEtw │
│  • Security Logs │      │    Enrichment    │      │  • IRun          │
│  • WMI           │      │  • Statistics    │      │  • IQuery        │
│                  │      │                  │      │  • IProvide      │
│  Linux:          │      │                  │      │                  │
│  • procfs        │      │ WintapMessage    │      │  MEF Framework   │
│  • eBPF          │      │                  │      │                  │
│  • OSQuery       │      └──────────────────┘      └──────────────────┘
│                  │               │
└──────────────────┘               │
                                   ▼
                         ┌──────────────────┐
                         │   Serializers    │
                         │   ═══════════    │
                         │  • Process       │
                         │  • Network       │
                         │  • File          │
                         │  • Default       │
                         │                  │
                         │  Format: Parquet │
                         └──────────────────┘
                                   │
                                   ▼
                         ┌──────────────────┐
                         │  Data Adapters   │
                         │  ═════════════   │
                         │  • S3            │
                         │  • SMB Share     │
                         │  • Custom        │
                         │    (IUpload)     │
                         └──────────────────┘
                                   │
        ┌──────────────────────────┼──────────────────────────┐
        ▼                          ▼                          ▼
┌─────────────┐          ┌─────────────┐          ┌─────────────┐
│  S3 Bucket  │          │  SMB Share  │          │   Custom    │
│  (Parquet)  │          │  (Parquet)  │          │ Destination │
└─────────────┘          └─────────────┘          └─────────────┘


┌─────────────────────────────────────────────────────────────────────────┐
│                          ANALYSIS INTERFACE                              │
│                                                                          │
│                    Wintap Workbench (Web-based)                         │
│                    • Live EPL Query Execution                           │
│                    • Process Tree Visualization                         │
│                    • Real-time Event Streaming                          │
└─────────────────────────────────────────────────────────────────────────┘
```


# Key Capabilities
Plugin Architecture: Write small .NET assemblies that subscribe to the events you need (process, network, file, registry). The framework handles infrastructure.
Unified Data Model: All telemetry uses a consistent WintapMessage structure, making it straightforward to correlate across different data sources.
Real-Time Analysis: Built-in Esper CEP engine allows live queries against event streams using EPL (Event Processing Language).
Platform Support: Windows (stable), Linux (in development). Core infrastructure is platform-agnostic.

# System Requirements
.NET 8.0 or later
Windows 10/11 or Server 2019+ (64-bit)
Linux Ubuntu 24.04+ (in development)
4GB RAM minimum
Administrator/root privileges


# Quick Start
git clone https://github.com/LLNL/wintap.git
cd wintap
dotnet build -c Release

Windows deployment:
powershellsc.exe create Wintap binPath= "C:\Path\To\Wintap.exe" start=auto
sc.exe start Wintap

Linux deployment: 
See docs/LINUX_DEPLOYMENT.md

# Documentation
Developer Guide - see documents folder in repo

# Technology
.NET 8.0 | MEF | Esper CEP | DuckDB | TraceEvent | Parquet

# LLNL-CODE-837816
https://github.com/LLNL/wintap