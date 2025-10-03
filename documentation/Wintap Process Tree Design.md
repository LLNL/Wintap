# Wintap Robust Process Tree Management
## High-Level Design Document (Revised)

**Version:** 2.0  
**Date:** December 2024  
**Authors:** Wintap Development Team

---

## Executive Summary

This document describes a streamlined architecture for robust process tree management in Wintap that eliminates the "unknown process" attribution problem through a three-layer approach: boot trace analysis, lightweight ETW mini-trace processing with hourly compaction, and real-time event streams. The solution provides zero-gap process visibility with automatic recovery, efficient storage via DuckDB, and predictable performance regardless of system uptime.

### Key Benefits
- **Zero-gap process attribution** - No missed processes during Wintap restarts
- **Predictable performance** - Bounded processing times via smart compaction
- **Self-healing** - Automatic ETW session management and database maintenance
- **Scalable** - Database stays lean through intelligent pruning
- **Operationally simple** - Minimal maintenance with automated cleanup

---

## Architecture Overview

The new process tree management system consists of three integrated subsystems working together to provide comprehensive process visibility:

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Boot Trace    │    │   Mini-Trace     │    │  Real-Time      │
│   Processor     │    │   Processor      │    │  Event Feed     │
│                 │    │                 │    │                 │
│ • Historical    │    │ • Lightweight    │    │ • Live Updates  │
│ • One-time      │    │ • Process Only   │    │ • Process Start │
│ • Complete      │    │ • Hourly Reset   │    │ • Process Stop  │
└─────────────────┘    └──────────────────┘    └─────────────────┘
         │                       │                       │
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DuckDB Process Tree                        │
│  ┌─────────────────┐  ┌──────────────────┐  ┌─────────────────┐ │
│  │ Historical      │  │  Live Processes  │  │ Smart           │ │
│  │ Process Data    │  │  (Hourly Refresh)│  │ Compaction      │ │
│  │                 │  │                  │  │                 │ │
│  │ • Boot lineage  │  │ • Active trees   │  │ • Prune dead    │ │
│  │ • Rich details  │  │ • Gap coverage   │  │   processes     │ │
│  │                 │  │                  │  │ • Keep lineages │ │
│  └─────────────────┘  └──────────────────┘  └─────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Wintap Event Attribution                     │
│ • File Events        • Network Events      • Registry Events    │
│ • Process Lookups    • Ancestry Queries    • Tree Visualization │
└─────────────────────────────────────────────────────────────────┘
```

---

## Subsystem Designs

### 1. ETW Subsystem

The ETW subsystem provides three complementary data collection mechanisms:

#### 1.1 Boot Trace Processor
**Purpose:** Provides complete historical process context from system boot to Wintap startup.

```
┌─────────────────────────────────────────────────────────────────┐
│                    Boot Trace Processor                         │
├─────────────────────────────────────────────────────────────────┤
│  Input: Wintap.Collectors.Process.ETLFile.BootTrace.etl        │
│                                                                 │
│  ┌─────────────────┐    ┌──────────────────┐    ┌─────────────┐ │
│  │ ETW TraceEvent  │───▶│ Process Builder  │───▶│ DuckDB      │ │
│  │ Source          │    │                  │    │ Loader      │ │
│  │ • ProcessStart  │    │ • Correlate      │    │             │ │
│  │ • ProcessStop   │    │   Events         │    │ • Bulk      │ │
│  │ • ImageLoad     │    │ • Build Tree     │    │   Insert    │ │
│  └─────────────────┘    │ • Complete Info  │    │ • Index     │ │
│                         └──────────────────┘    │   Creation  │ │
│                                                 └─────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

**Key Features:**
- **One-time execution** at system boot or Wintap startup
- **Complete process lineage** from boot time with ImageLoad correlation
- **Rich process information** (full paths, command lines, parent relationships)
- **Efficient bulk loading** into DuckDB

#### 1.2 Mini-Trace Processor
**Purpose:** Continuously captures new process activity with lightweight ETW sessions.

```
┌─────────────────────────────────────────────────────────────────┐
│                    Mini-Trace Processor                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────┐    ┌──────────────────┐    ┌─────────────┐ │
│  │ Lightweight     │───▶│ Hourly Processor │───▶│ DuckDB      │ │
│  │ ETW Session     │    │                  │    │ Update      │ │
│  │                 │    │ • Process Events │    │             │ │
│  │ • Process Only  │    │   Only (No       │    │ • Merge     │ │
│  │ • No ImageLoad  │    │   ImageLoad)     │    │   New       │ │
│  │ • Session Reset │    │ • Gap Analysis   │    │   Processes │ │
│  │ • Small Files   │    │ • Session Reset  │    │ • Update    │ │
│  └─────────────────┘    └──────────────────┘    │   State     │ │
│                                                 └─────────────┘ │
│                                                                 │
│  Managed by: WintapSvcMgr (via Wintap calls + Scheduled Task)  │
└─────────────────────────────────────────────────────────────────┘
```

**Key Features:**
- **Lightweight ETW capture** (Process events only, no ImageLoad)
- **Hourly processing and reset** to keep file sizes manageable
- **WintapSvcMgr integration** for external session management
- **Gap coverage** during Wintap outages

**File Size Management:**
```
Expected Mini-Trace Sizes:
- 1 hour: 1-5 MB (manageable)
- Processing time: 2-10 seconds
- Reset cycle: Every hour
```

#### 1.3 Real-Time Event Feed
**Purpose:** Provides immediate process updates for real-time event attribution.

```
┌─────────────────────────────────────────────────────────────────┐
│                   Real-Time Event Feed                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────┐    ┌──────────────────┐    ┌─────────────┐ │
│  │ ETW Real-Time   │───▶│ In-Memory Cache  │───▶│ Event       │ │
│  │ Listener        │    │ Manager          │    │ Attribution │ │
│  │                 │    │                  │    │             │ │
│  │ • ProcessStart  │    │ • Fast Lookups   │    │ • Immediate │ │
│  │ • ProcessStop   │    │ • TTL Management │    │   Attribution│ │
│  │ • Live Updates  │    │ • DuckDB Sync    │    │ • Process   │ │
│  └─────────────────┘    └──────────────────┘    │   Names     │ │
│                                                 └─────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### 2. DuckDB Process Tree Database

**Purpose:** Central analytical database for all process information with smart compaction.

#### Database Schema
```sql
-- Main process table
CREATE TABLE live_processes (
    process_id INTEGER PRIMARY KEY,
    parent_process_id INTEGER,
    process_name VARCHAR,
    image_path VARCHAR,
    command_line VARCHAR,
    create_time TIMESTAMP,
    exit_time TIMESTAMP,
    exit_code INTEGER,
    unique_process_key BIGINT,
    is_active BOOLEAN,
    update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    -- Metadata
    source VARCHAR,  -- 'boot_trace', 'mini_trace', 'real_time'
    depth INTEGER,   -- Process depth in hierarchy
    has_live_descendants BOOLEAN  -- Key for compaction
);

-- Performance indexes
CREATE INDEX idx_parent_process_id ON live_processes(parent_process_id);
CREATE INDEX idx_process_name ON live_processes(process_name);
CREATE INDEX idx_is_active ON live_processes(is_active);
CREATE INDEX idx_has_live_descendants ON live_processes(has_live_descendants);
```

#### Smart Compaction Algorithm
**Purpose:** Keep database lean by removing processes with no live descendants.

```sql
-- Identify processes with live descendants using recursive CTE
WITH RECURSIVE live_lineages AS (
    -- Start with all active processes
    SELECT process_id, parent_process_id, 1 as is_in_live_lineage
    FROM live_processes 
    WHERE is_active = true
    
    UNION ALL
    
    -- Recursively add all ancestors of live processes
    SELECT p.process_id, p.parent_process_id, 1
    FROM live_processes p
    INNER JOIN live_lineages l ON p.process_id = l.parent_process_id
)
-- Delete processes not in any live lineage
DELETE FROM live_processes 
WHERE process_id NOT IN (SELECT process_id FROM live_lineages);
```

**Compaction Benefits:**
- **Typical reduction**: 80-95% of processes pruned
- **Database size**: Stays proportional to active process trees
- **Query performance**: Constant time regardless of system uptime

---

## System Flows

### Boot Startup Flow
```
┌─────────────────────────────────────────────────────────────────┐
│                    Boot Startup Flow                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Delete DuckDB ──────────────────────────────────────────┐   │
│     • Clean slate for new boot cycle                       │   │
│                                                             │   │
│  2. Process Boot Trace ─────────────────────────────────────┤   │
│     • Load complete historical process tree                │   │
│     • Rich process information with ImageLoad correlation  │   │
│                                                             │   │
│  3. Process Mini-Trace ─────────────────────────────────────┤   │
│     • WintapSvcMgr called from Wintap                     │   │
│     • Load any processes since boot trace creation         │   │
│                                                             │   │
│  4. Compact Database ───────────────────────────────────────┤   │
│     • WintapSvcMgr called from Wintap                     │   │
│     • Remove processes with no live descendants            │   │
│                                                             │   │
│  5. Hook Real-Time ─────────────────────────────────────────┤   │
│     • Start live ETW process monitoring                    │   │
│                                                             │   │
│  6. Send All DB Events ─────────────────────────────────────┘   │
│     • Broadcast complete process tree (ordered by time)        │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Wintap Restart Flow
```
┌─────────────────────────────────────────────────────────────────┐
│                   Wintap Restart Flow                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Process Mini-Trace ─────────────────────────────────────┐   │
│     • WintapSvcMgr called from Wintap                     │   │
│     • Process gap events since last restart                │   │
│                                                             │   │
│  2. Compact Database ───────────────────────────────────────┤   │
│     • WintapSvcMgr called from Wintap                     │   │
│     • Remove processes with no live descendants            │   │
│                                                             │   │
│  3. Hook Real-Time ─────────────────────────────────────────┤   │
│     • Resume live ETW process monitoring                   │   │
│                                                             │   │
│  4. Send All DB Events ─────────────────────────────────────┘   │
│     • Broadcast current process tree state                     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Hourly Maintenance Flow
```
┌─────────────────────────────────────────────────────────────────┐
│                   Hourly Maintenance Flow                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Triggered by: Windows Scheduled Task                          │
│                                                                 │
│  1. Process Mini-Trace ─────────────────────────────────────┐   │
│     • WintapSvcMgr called from Scheduled Task             │   │
│     • Process accumulated events from past hour            │   │
│                                                             │   │
│  2. Compact Database ───────────────────────────────────────┤   │
│     • WintapSvcMgr called from Scheduled Task             │   │
│     • Prune processes with no live descendants             │   │
│                                                             │   │
│  3. Send All DB Events ─────────────────────────────────────┘   │
│     • Update Wintap with refreshed process tree                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Process Attribution Flow

### PidHash Resolution Strategy
```
┌─────────────────────────────────────────────────────────────────┐
│                   PidHash Resolution Flow                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Event Arrives (File/Network/Registry) with PID                │
│                │                                               │
│                ▼                                               │
│  ┌─────────────────────────────┐                               │
│  │ Check In-Memory Cache       │                               │
│  │ • Fast O(1) lookup          │                               │
│  │ • Recent processes          │                               │
│  └─────────────────────────────┘                               │
│                │                                               │
│                ▼                                               │
│  ┌─────────────────────────────┐    ┌─────────────────────────┐ │
│  │ Cache Hit?                  │───▶│ Use Cached Info         │ │
│  └─────────────────────────────┘    │ • Process name          │ │
│                │                    │ • Image path            │ │
│                │ No                 │ • Parent info           │ │
│                ▼                    └─────────────────────────┘ │
│  ┌─────────────────────────────┐                               │
│  │ Fallback: Query DuckDB      │                               │
│  │ • SELECT * FROM             │                               │
│  │   live_processes            │                               │
│  │   WHERE process_id = ?      │                               │
│  │ • Cache result for future   │                               │
│  └─────────────────────────────┘                               │
│                │                                               │
│                ▼                                               │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ Enhance WintapMessage with Process Information              │ │
│  │ • ProcessName, ProcessPath populated                       │ │
│  │ • Complete Process object with lineage                     │ │
│  │ • No more "unknown" process attribution                    │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## WintapSvcMgr Integration

### Component Responsibilities
```
┌─────────────────────────────────────────────────────────────────┐
│                    WintapSvcMgr Component                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ETW Session Management:                                        │
│  • Create/manage lightweight mini-trace ETW sessions           │
│  • Reset sessions hourly to prevent file growth                │
│  • Handle session lifecycle independently of Wintap            │
│                                                                 │
│  Mini-Trace Processing:                                         │
│  • Parse ETW files for process events                          │
│  • Extract process start/stop information                      │
│  • Merge new processes into DuckDB                             │
│                                                                 │
│  Database Compaction:                                           │
│  • Execute smart compaction queries                            │
│  • Remove processes with no live descendants                   │
│  • Maintain database performance over time                     │
│                                                                 │
│  Called by:                                                     │
│  • Wintap service (startup, restart)                          │
│  • Windows Scheduled Task (hourly maintenance)                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Deployment Architecture

### File Structure
```
C:\ProgramData\Wintap\
├── ProcessTrace\
│   ├── mini-trace.etl                    # Lightweight ETW capture
│   └── session-config.json               # ETW session configuration
├── ProcessTree\
│   ├── live-processes.duckdb             # Main process database
│   └── live-processes.duckdb.wal         # Write-ahead log
├── Logs\
│   ├── process-tree.log                  # Processing logs
│   └── wintap-svcmgr.log                # Service manager logs
└── Config\
    └── process-tree-config.json         # Configuration settings
```

### Windows Services & Tasks
```
Windows Services:
├── Wintap                                # Main telemetry service
│   ├── Boot trace processing
│   ├── Real-time event feed
│   └── DuckDB integration
└── WintapSvcMgr                         # Process tree service manager
    ├── ETW session management
    ├── Mini-trace processing
    └── Database compaction

Windows Scheduled Tasks:
├── WintapHourlyMaintenance              # Hourly task
│   ├── Schedule: Every hour at :00
│   ├── User: SYSTEM
│   └── Command: WintapSvcMgr.exe /process-mini-trace /compact-db
```

### Configuration Management
```json
{
  "ProcessTree": {
    "DuckDbPath": "C:\\ProgramData\\Wintap\\ProcessTree\\live-processes.duckdb",
    "BootTracePath": "C:\\Program Files\\Wintap7\\etl\\Wintap.Collectors.Process.ETLFile.BootTrace.etl",
    "MiniTracePath": "C:\\ProgramData\\Wintap\\ProcessTrace\\mini-trace.etl",
    "MiniTraceSessionName": "WintapMiniTraceSession",
    "ProcessCacheSize": 10000,
    "ProcessCacheTTL": "01:00:00",
    "CompactionEnabled": true,
    "HourlyMaintenanceEnabled": true,
    "EnableBootTraceProcessing": true,
    "EnableMiniTraceProcessing": true,
    "EnableRealTimeCache": true
  }
}
```

---

## Performance Characteristics

### Startup Performance
```
Boot Startup:
├── Boot trace processing: 2-10 seconds
├── Mini-trace processing: 1-3 seconds  
├── Database compaction: 1-2 seconds
└── Total: 4-15 seconds (predictable)

Restart:
├── Mini-trace processing: 1-5 seconds
├── Database compaction: 1-2 seconds
└── Total: 2-7 seconds (fast recovery)

Hourly Maintenance:
├── Mini-trace processing: 1-3 seconds
├── Database compaction: 1-2 seconds
└── Total: 2-5 seconds (minimal impact)
```

### Database Performance
```
Query Performance:
├── Process lookup by PID: <1ms
├── Ancestry queries: <5ms  
├── Tree enumeration: <100ms
└── Compaction operation: 1-2 seconds

Storage Efficiency:
├── Typical database size: 5-50MB
├── Compaction reduction: 80-95%
├── Growth rate: Bounded by active processes
```

---

## Monitoring & Operations

### Health Checks
```csharp
public class ProcessTreeHealthCheck
{
    public async Task<HealthStatus> CheckHealthAsync()
    {
        var health = new HealthStatus();
        
        // Check DuckDB connectivity and size
        health.DatabaseConnectivity = await TestDuckDbConnection();
        health.DatabaseSizeMB = GetDatabaseSizeMB();
        
        // Check ETW mini-trace session
        health.MiniTraceSessionActive = IsETWSessionActive();
        health.MiniTraceFileSizeMB = GetMiniTraceFileSizeMB();
        
        // Check last compaction
        health.LastCompactionTime = GetLastCompactionTime();
        health.ProcessesInDatabase = GetProcessCountInDatabase();
        
        // Check scheduled task
        health.HourlyTaskHealthy = IsHourlyTaskHealthy();
        
        return health;
    }
}
```

### Performance Metrics
- **Boot processing time**: Target <15 seconds
- **Restart processing time**: Target <7 seconds  
- **Hourly processing time**: Target <5 seconds
- **Database size**: Target <100MB steady state
- **Process lookup performance**: Target <1ms average

### Alerting Thresholds
- **Boot processing** takes >30 seconds
- **Database size** exceeds 500MB
- **Compaction** hasn't run in >90 minutes
- **Mini-trace file** exceeds 50MB
- **Process attribution** fails for >1% of events

---

## Benefits & Impact

### Reliability Improvements
- **Eliminates "unknown process" attribution** through comprehensive coverage
- **Survives Wintap restarts** with fast gap recovery
- **Self-healing architecture** with automatic maintenance
- **Predictable performance** regardless of system uptime

### Performance Improvements
- **Fast startup** with bounded processing times
- **Real-time attribution** via in-memory cache with DuckDB fallback
- **Efficient queries** through analytical database engine
- **Scalable architecture** that maintains performance over time

### Operational Improvements
- **Automated maintenance** via scheduled tasks and smart compaction
- **Reduced complexity** compared to previous multi-layered approach
- **Better debugging** with SQL-queryable process history
- **Predictable resource usage** through database compaction

### Development Improvements
- **Simplified codebase** with clear separation of concerns
- **Better testability** with database-backed queries
- **Enhanced monitoring** through health checks and metrics
- **Future extensibility** for advanced process analytics

---

## Conclusion

This revised architecture provides a robust, scalable solution for process tree management that maintains predictable performance characteristics regardless of system uptime. The combination of lightweight ETW capture, smart database compaction, and automated maintenance ensures reliable process attribution while minimizing operational complexity.

The key innovation is the **smart compaction algorithm** that keeps the database lean by preserving only processes with live descendants, ensuring consistent performance over time while maintaining complete process lineage information for attribution purposes.