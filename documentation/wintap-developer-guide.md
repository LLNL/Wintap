# Wintap Developer Guide

**Version:** 7.0  
**Last Updated:** October, 2025  

---

## Table of Contents

1. [Project Overview and Architecture](#1-project-overview-and-architecture)
2. [Plugin Subsystem](#2-plugin-subsystem)
3. [Data Adapter Subsystem](#3-data-adapter-subsystem)
4. [Multi-Platform Development](#4-multi-platform-development)
5. [Developer Setup and Contribution Guide](#5-developer-setup-and-contribution-guide)
6. [Appendix: Linux Deployment Guide](#appendix-wintap-linux-deployment-guide)
7. [Appendix A: WintapMessage Schema Reference](#appendix-a-wintapmessage-schema-reference)
8. [Appendix B: Useful Resources](#appendix-b-useful-resources)

---

## 1. Project Overview and Architecture

### 1.1 Purpose and Goals

**Wintap** (Windows Telemetry Analysis Platform) is an open-source, extensible host telemetry framework designed for real-time system monitoring, behavioral analysis, and security research. Developed at Lawrence Livermore National Laboratory (LLNL), Wintap collects, processes, and routes system telemetry data from multiple sources.

**Key Goals:**
- **Real-time Telemetry Collection**: Capture process, network, file, and registry events with minimal overhead
- **Extensibility**: Plugin architecture allows researchers to add custom collectors and analyzers
- **Multi-Platform Support**: Architecture designed to support Windows (primary), Linux, and macOS
- **Complex Event Processing**: Built-in Esper CEP engine for real-time event correlation and pattern detection
- **Flexible Data Routing**: Modular adapter system supports multiple output destinations (files, databases, APIs)

### 1.2 Design Philosophy

Wintap follows these core principles:

1. **Separation of Concerns**: Clear boundaries between collection, processing, and output layers
2. **Plugin Isolation**: Each plugin runs in its own isolated assembly load context for stability and hot-reload support
3. **Platform Abstraction**: Platform-specific code isolated in dedicated namespaces
4. **Event-Driven Architecture**: Asynchronous event processing using the Esper CEP engine
5. **Configurability**: JSON-based configuration for adapters and plugins

### 1.3 System Architecture

#### High-Level Architecture Diagram

```
┌────────────────────────────────────────────────────────────────────┐
│                         WINTAP SERVICE                              │
│                        (WinTapSvc.cs)                              │
└────────────────────────────────────────────────────────────────────┘
                                  │
                ┌─────────────────┼─────────────────┐
                │                 │                 │
                ▼                 ▼                 ▼
    ┌──────────────────┐  ┌─────────────┐  ┌──────────────┐
    │  PluginManager   │  │   EventChannel │ │ Subscription │
    │                  │  │   (Esper CEP)  │ │   Manager    │
    │ - Load Plugins   │  │                │ │              │
    │ - MEF Discovery  │  │ - Route Events │ │ - Windows ETW│
    │ - Isolation      │  │ - Enrich Data  │ │ - Linux      │
    │ - Scheduler      │  │ - Statistics   │ │ - macOS      │
    └──────────────────┘  └─────────────┘  └──────────────┘
            │                     │                 │
            │                     │                 │
            ▼                     ▼                 ▼
    ┌──────────────────────────────────────────────────────┐
    │                   PLUGIN LAYER                        │
    │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
    │  │ISubscribe│  │   IRun   │  │ IQuery   │  ...      │
    │  └──────────┘  └──────────┘  └──────────┘           │
    └──────────────────────────────────────────────────────┘
                                  │
                                  ▼
    ┌──────────────────────────────────────────────────────┐
    │              ETL / SERIALIZATION LAYER                │
    │  - DefaultSerializer, ProcessSerializer, etc.        │
    │  - Parquet/CSV Writers                               │
    └──────────────────────────────────────────────────────┘
                                  │
                                  ▼
    ┌──────────────────────────────────────────────────────┐
    │              DATA ADAPTER LAYER                       │
    │  - File System (Parquet, CSV)                        │
    │  - Upload Adapters (IUpload interface)               │
    │  - Database Adapters                                 │
    │  - Network/API Adapters                              │
    └──────────────────────────────────────────────────────┘
```
## Building Wintap

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows: Visual Studio 2022 or VS Code
- Linux/macOS: VS Code or command line

### Quick Start

# Build for your platform
dotnet build ./wintap/Wintap.csproj  # Windows
dotnet build ./wintap/Lintap.csproj  # Linux
dotnet build ./wintap/Mactap.csproj  # macOS

# Publish release builds
dotnet publish ./wintap/Wintap.csproj -c Release -r win-x64
dotnet publish ./wintap/Lintap.csproj -c Release -r linux-x64
dotnet publish ./wintap/Mactap.csproj -c Release -r osx-arm64

### Project Structure
- `Wintap.csproj` - Windows version
- `Lintap.csproj` - Linux version
- `Mactap.csproj` - macOS version
- `Wintap.Common.props` - Shared configuration
- `Directory.Build.props` - Platform-specific exclusions

All three projects share the same source code. Edit files in any project and changes apply to all platforms.


### 1.4 Core Components

#### 1.4.1 WinTapSvc (Main Service)

**Location:** `WintapSvcCore.cs`

The main Windows service implementation that orchestrates all components. It inherits from `BackgroundService` and manages the service lifecycle.

**Responsibilities:**
- Service initialization and shutdown
- Component orchestration (PluginManager, SubscriptionManager, EventChannel)
- Platform-specific configuration (NTFS permissions on Windows)
- Background worker management

**Key Methods:**
- `ExecuteAsync()`: Main service entry point
- `StopAsync()`: Graceful shutdown handler
- `startupWorker_DoWork()`: Asynchronous initialization of all subsystems

#### 1.4.2 PluginManager

**Location:** `gov.llnl.wintap.core.infrastructure.PluginManager`

Manages plugin discovery, loading, execution, and lifecycle using the Managed Extensibility Framework (MEF).

**Responsibilities:**
- Plugin discovery from the plugins directory
- MEF-based composition and dependency injection
- Isolated plugin loading using `AssemblyLoadContext`
- Plugin scheduling and execution (for `IRun` plugins)
- Event routing to subscriber plugins
- Plugin watchdog monitoring

**Key Features:**
- **Isolated Loading**: Each plugin loaded in its own `AssemblyLoadContext` for isolation and unloading
- **Digital Signature Verification**: Production builds require signed assemblies (bypassed in DEBUG mode)
- **Dynamic ETW Provider Registration**: Plugins can request specific ETW providers
- **Exception Handling**: Plugin failures contained using `PluginExceptionHandler`

#### 1.4.3 SubscriptionManager

**Location:** `gov.llnl.wintap.core.infrastructure.SubscriptionManager`

Platform abstraction layer that delegates to platform-specific subscription managers.

**Platform Managers:**
- **Windows**: `WindowsSubscriptionManager` - Manages ETW-based collectors
- **Linux**: `LinuxSubscriptionManager` - Manages Linux-specific sensors
- **macOS**: Placeholder for future implementation

**Key Method:**
```csharp
internal void Start()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        winCollectors = winSubMgr.Start();
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        linuxCollectors = linuxSubMgr.Start();
    }
    // ...
}
```

#### 1.4.4 EventChannel (Esper CEP Integration)

**Location:** `gov.llnl.wintap.core.infrastructure.EventChannel`

Central event routing hub using the Esper Complex Event Processing (CEP) engine.

**Responsibilities:**
- Event ingestion and routing
- Process lineage enrichment (resolving PID to process details)
- Real-time event statistics (throughput, peaks, totals)
- EPL (Event Processing Language) query compilation and deployment
- Interactive query workbench state management

**Key Features:**
- **Process Enrichment**: Every event enriched with process name and hash via DuckDB process tree lookup
- **Performance Monitoring**: Tracks events/second, peak rates, dropped events
- **Query Workbench**: Allows researchers to deploy custom EPL queries against live streams
- **Esper Runtime**: Single shared `EPRuntime` instance for all query processing

**Example Usage:**
```csharp
// Send event to Esper
EventChannel.Send(wintapMessage);

// Compile and deploy EPL query
EPDeployment deployment = EventChannel.CompileDeploy(eplQuery, deploymentId);
```

#### 1.4.5 ETL Pipeline (Serializers)

**Location:** `gov.llnl.wintap.core.etl.extract`

Transforms Wintap events into serialized formats for storage and transmission.

**Key Serializers:**
- **DefaultSerializer**: Generic handler for most event types
- **ProcessSerializer**: Specialized process event handling
- **HostSerializer**: System metadata and configuration
- **NetworkSerializer**: TCP/UDP aggregation and summarization

**Data Flow:**
```
WintapMessage → Esper CEP → Serializer → ExpandoObject → ParquetWriter/CSVWriter → Disk
```

### 1.5 Data Flow Overview

```
┌──────────────────┐
│  Data Sources    │  (ETW, Security Logs, OSQuery, etc.)
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│   Collectors     │  (ProcessSensor, TcpSensor, FileSensor, etc.)
│  (Platform-      │
│   Specific)      │
└────────┬─────────┘
         │
         │ WintapMessage objects
         ▼
┌──────────────────┐
│  EventChannel    │  - Process enrichment
│  (Esper CEP)     │  - Event correlation
│                  │  - Statistics tracking
└────────┬─────────┘
         │
         ├────────────────────────┬────────────────────┐
         │                        │                    │
         ▼                        ▼                    ▼
┌────────────────┐      ┌────────────────┐   ┌────────────────┐
│  ISubscribe    │      │  IQuery        │   │  Serializers   │
│  Plugins       │      │  Plugins       │   │  (ETL)         │
└────────────────┘      └────────────────┘   └───────┬────────┘
                                                      │
                                                      ▼
                                             ┌────────────────┐
                                             │  ParquetWriter │
                                             │  Batching &    │
                                             │  Compression   │
                                             └───────┬────────┘
                                                     │
                                                     ▼
                                             ┌────────────────┐
                                             │ Data Adapters  │
                                             │ - File System  │
                                             │ - Upload APIs  │
                                             │ - Databases    │
                                             └────────────────┘
```

### 1.6 Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `gov.llnl.wintap` | Core service and plugin interfaces |
| `gov.llnl.wintap.core.infrastructure` | Plugin management, event routing, logging |
| `gov.llnl.wintap.core.etl` | ETL pipeline, serialization, data adapters |
| `gov.llnl.wintap.collect.models` | Data models (WintapMessage, ProcessObject, etc.) |
| `gov.llnl.wintap.platform.windows.*` | Windows-specific collectors (ETW, Security Logs) |
| `gov.llnl.wintap.platform.linux.*` | Linux-specific collectors (to be implemented) |
| `gov.llnl.wintap.core.api` | REST API and SignalR hubs for UI integration |
| `gov.llnl.wintap.core.shared` | Utilities, logging, state management |

---

## 2. Plugin Subsystem

### 2.1 Plugin Architecture Overview

Wintap uses the **Managed Extensibility Framework (MEF)** for plugin discovery and composition. Plugins are loaded in isolated `AssemblyLoadContext` instances, allowing for:

- **Hot Unloading**: Plugins can be unloaded without restarting the service
- **Fault Isolation**: Plugin crashes don't affect the core service
- **Dependency Management**: Each plugin manages its own dependencies

### 2.2 Plugin Types and Interfaces

Wintap supports five plugin types, defined in `Interfaces.cs`:

| Interface | Purpose | Use Case |
|-----------|---------|----------|
| `ISubscribe` | Subscribe to Wintap modeled events | Monitor processes, files, network connections |
| `ISubscribeEtw` | Subscribe to raw ETW events | Access ETW providers not modeled by Wintap |
| `IRun` | Scheduled task execution | Periodic data collection, health checks |
| `IQuery` | Esper EPL query submission | Complex event correlation and analysis |
| `IProvide` | Generate custom events | Integrate external data sources |

### 2.3 Plugin Discovery and Loading

**Plugin Directory Structure:**
```
C:\Program Files\Wintap\plugins\
├── MyPlugin\
│   ├── MyPlugin.dll        (main plugin assembly)
│   ├── MyPlugin.deps.json
│   └── lib\                (optional dependency folder)
│       └── Dependency.dll
└── AnotherPlugin\
    └── AnotherPlugin.dll
```

**Loading Process:**
1. PluginManager scans `plugins` directory for subdirectories
2. For each subdirectory:
   - Try to load `{DirectoryName}.dll` (convention-based)
   - If not found or no MEF exports, scan all DLLs for MEF exports
   - Verify digital signature (production builds only)
   - Load valid plugin into isolated `AssemblyLoadContext`
3. MEF composes all plugins into PluginManager's import properties
4. PluginManager registers each plugin based on its interface(s)

### 2.4 Writing a Plugin: Step-by-Step Guide

#### 2.4.1 ISubscribe Plugin (Event Subscriber)

This is the most common plugin type for monitoring Wintap events.

**Step 1: Create a Class Library Project**

```bash
dotnet new classlib -n MyWintapPlugin -f net8.0
```

**Step 2: Add Required References**

```xml
<ItemGroup>
  <PackageReference Include="System.ComponentModel.Composition" Version="8.0.0" />
  <Reference Include="Wintap.Core">
    <HintPath>C:\Program Files\Wintap\Wintap.exe</HintPath>
  </Reference>
</ItemGroup>
```

**Step 3: Implement the Plugin**

```csharp
using System;
using System.ComponentModel.Composition;
using gov.llnl.wintap;
using gov.llnl.wintap.collect.models;
using static gov.llnl.wintap.Interfaces;

namespace MyWintapPlugin
{
    /// <summary>
    /// Example plugin that monitors process creation events
    /// </summary>
    [Export(typeof(ISubscribe))]
    [ExportMetadata("Name", "ProcessMonitor")]
    public class ProcessMonitorPlugin : ISubscribe, ISubscribeData
    {
        // MEF metadata property
        public string Name => "ProcessMonitor";

        /// <summary>
        /// Called once when Wintap starts. Return the events you want to monitor.
        /// </summary>
        public EventFlags Startup()
        {
            Console.WriteLine("ProcessMonitor plugin starting...");
            
            // Subscribe to Process events only
            return EventFlags.Process;
            
            // To subscribe to multiple event types:
            // return EventFlags.Process | EventFlags.TcpConnection | EventFlags.FileActivity;
        }

        /// <summary>
        /// Called for each event matching your EventFlags subscription
        /// </summary>
        public void Subscribe(WintapMessage eventMsg)
        {
            // Filter for process start events
            if (eventMsg.MessageType == WintapMessage.MessageTypeEnum.Process &&
                eventMsg.ActivityType == WintapMessage.ActivityTypeEnum.Start)
            {
                var process = eventMsg.Process;
                
                // Log suspicious behavior
                if (IsSuspicious(process))
                {
                    Console.WriteLine($"[ALERT] Suspicious process detected:");
                    Console.WriteLine($"  Name: {process.Name}");
                    Console.WriteLine($"  PID: {eventMsg.PID}");
                    Console.WriteLine($"  Path: {process.Path}");
                    Console.WriteLine($"  Command Line: {process.CommandLine}");
                    Console.WriteLine($"  Parent PID: {process.ParentPID}");
                    
                    // Send alert to external system
                    SendAlert(process);
                }
            }
        }

        /// <summary>
        /// Called once when Wintap shuts down
        /// </summary>
        public void Shutdown()
        {
            Console.WriteLine("ProcessMonitor plugin shutting down...");
            // Cleanup resources, close connections, etc.
        }

        private bool IsSuspicious(WintapMessage.ProcessObject process)
        {
            // Example heuristics
            if (process.Path?.Contains("\\Temp\\") == true) return true;
            if (process.Name?.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) == true &&
                process.CommandLine?.Contains("-enc") == true) return true;
            
            return false;
        }

        private void SendAlert(WintapMessage.ProcessObject process)
        {
            // Implement alert logic (webhook, SIEM integration, etc.)
        }
    }
}
```

#### 2.4.2 IRun Plugin (Scheduled Task)

For plugins that need to execute periodically:

```csharp
using System;
using System.ComponentModel.Composition;
using gov.llnl.wintap;
using static gov.llnl.wintap.Interfaces;

namespace MyWintapPlugin
{
    [Export(typeof(IRun))]
    [ExportMetadata("Name", "HealthCheckPlugin")]
    public class HealthCheckPlugin : IRun, IRunData
    {
        public string Name => "HealthCheckPlugin";

        /// <summary>
        /// Define execution parameters
        /// </summary>
        public RunManifest RunStartup()
        {
            return new RunManifest
            {
                Interval = TimeSpan.FromMinutes(5),      // Run every 5 minutes
                MaxRuntime = TimeSpan.FromMinutes(2),    // Watchdog timeout
                RequiredHost = "NONE"                     // No network requirement
            };
        }

        /// <summary>
        /// Executed on the defined interval
        /// </summary>
        public void Run()
        {
            try
            {
                Console.WriteLine($"[{DateTime.Now}] Running health check...");
                
                // Check system resources
                var cpuUsage = GetCpuUsage();
                var memoryUsage = GetMemoryUsage();
                
                Console.WriteLine($"CPU: {cpuUsage}%, Memory: {memoryUsage}%");
                
                // Report to monitoring system
                ReportMetrics(cpuUsage, memoryUsage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in health check: {ex.Message}");
            }
        }

        public void RunShutdown()
        {
            Console.WriteLine("HealthCheck plugin shutting down");
        }

        private double GetCpuUsage() { /* Implementation */ return 0; }
        private double GetMemoryUsage() { /* Implementation */ return 0; }
        private void ReportMetrics(double cpu, double mem) { /* Implementation */ }
    }
}
```

#### 2.4.3 ISubscribeEtw Plugin (Raw ETW Events)

For accessing ETW providers not modeled by Wintap:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using gov.llnl.wintap;
using gov.llnl.wintap.collect.models;
using static gov.llnl.wintap.Interfaces;

namespace MyWintapPlugin
{
    [Export(typeof(ISubscribeEtw))]
    [ExportMetadata("Name", "CustomEtwPlugin")]
    public class CustomEtwPlugin : ISubscribeEtw, ISubscribeEtwData
    {
        public string Name => "CustomEtwPlugin";

        /// <summary>
        /// Return list of ETW provider names to subscribe to
        /// </summary>
        public List<string> Startup()
        {
            return new List<string>
            {
                "Microsoft-Windows-PowerShell",
                "Microsoft-Windows-DNS-Client"
            };
        }

        /// <summary>
        /// Receive raw ETW events from subscribed providers
        /// </summary>
        public void Subscribe(WintapMessage wintapMessage)
        {
            if (wintapMessage.MessageType == WintapMessage.MessageTypeEnum.GenericMessage)
            {
                var genericMsg = wintapMessage.GenericMessage;
                
                Console.WriteLine($"ETW Event: {genericMsg.Provider} - {genericMsg.EventName}");
                Console.WriteLine($"Message: {genericMsg.Message}");
                
                // Process the ETW event
                ProcessEtwEvent(genericMsg);
            }
        }

        public void Shutdown()
        {
            Console.WriteLine("CustomEtw plugin shutting down");
        }

        private void ProcessEtwEvent(WintapMessage.GenericMessageObject evt)
        {
            // Custom ETW event processing logic
        }
    }
}
```

### 2.5 Plugin Deployment

**Step 1: Build the Plugin**
```bash
dotnet build -c Release
```

**Step 2: Create Plugin Directory**
```bash
mkdir "C:\Program Files\Wintap\plugins\MyWintapPlugin"
```

**Step 3: Copy Files**
```bash
copy bin\Release\net8.0\*.dll "C:\Program Files\Wintap\plugins\MyWintapPlugin\"
```

**Step 4: Restart Wintap**
```bash
net stop Wintap
net start Wintap
```

**Verification:**
Check the Wintap log for plugin loading confirmation:
```
C:\ProgramData\Wintap\logs\wintap.log
```

Expected output:
```
Loading Wintap subscriber: MyWintapPlugin
Successfully loaded plugin MyWintapPlugin in isolated domain
```

### 2.6 Plugin Best Practices

1. **Error Handling**: Always wrap logic in try-catch blocks. Plugin exceptions are caught by `PluginExceptionHandler` but should be handled gracefully

2. **Performance**: 
   - Keep `Subscribe()` methods lightweight (< 50ms)
   - For heavy processing, queue events and process asynchronously
   - High-frequency events (TCP, UDP) can trigger thousands of times per second

3. **State Management**:
   - Store state in instance variables or external storage
   - Be aware that plugins may be unloaded/reloaded

4. **Logging**:
   - Use `Console.WriteLine()` for debug output (visible in service logs)
   - For production, integrate with Wintap's logging system (if exposed)

5. **Dependencies**:
   - Place dependencies in plugin directory or `lib` subfolder
   - Avoid conflicts with Wintap's dependencies

6. **Testing**:
   - Test plugin in DEBUG mode first (bypasses signature verification)
   - Use Wintap's event replay functionality for reproducible tests

---

## 3. Data Adapter Subsystem

### 3.1 Adapter Architecture

Wintap uses a **serialization and adapter pipeline** to route collected data to various destinations. The architecture separates concerns:

1. **Serializers**: Transform `WintapMessage` objects into flat, serializable formats (`ExpandoObject`)
2. **ParquetWriter/CSVWriter**: Batch and compress serialized data
3. **Adapters**: Transport data to final destinations (files, databases, APIs)

### 3.2 Data Flow Through Adapters

```
EventChannel (WintapMessage)
    │
    ▼
Serializer (per event type)
    │
    ▼
ExpandoObject (flat, dynamic structure)
    │
    ├──→ ParquetWriter → Parquet files
    ├──→ CSVWriter → CSV files  
    │
    ▼
Uploader (IUpload interface)
    │
    ├──→ S3 Adapter
    ├──→ HTTP Adapter
    ├──→ Database Adapter
    └──→ Custom Adapters
```

### 3.3 Serialization Configuration

**Location:** `ETLConfig.json` in the Wintap installation directory

**Example Configuration:**
```json
{
  "LogLevel": "Normal",
  "SensorProfile": "Quality",
  "SerializationIntervalSec": 60,
  "UploadIntervalSec": 300,
  "WriteToParquet": true,
  "WriteToCsv": false,
  "Adapters": [
    {
      "Name": "FileSystem",
      "Enabled": true,
      "Properties": {
        "OutputPath": "C:\\ProgramData\\Wintap\\parquet"
      }
    },
    {
      "Name": "S3Upload",
      "Enabled": false,
      "Properties": {
        "BucketName": "my-wintap-bucket",
        "Region": "us-west-2",
        "AccessKeyId": "YOUR_KEY",
        "SecretAccessKey": "YOUR_SECRET"
      }
    }
  ]
}
```

### 3.4 Built-In Adapters

#### 3.4.1 Parquet File Adapter

**Purpose**: Write telemetry data to Apache Parquet format (columnar, compressed)

**Output Location**: `C:\ProgramData\Wintap\parquet\{sensor_type}\`

**File Naming**: `{hostname}+{sensor_type}+{timestamp}.parquet`

**Example**: `DESKTOP-ABC+process_sensor+638400000000000000.parquet`

**Key Features**:
- Snappy compression
- Batched writes (default: every 60 seconds)
- Per-sensor-type directories
- Schema auto-generated from first `ExpandoObject`

#### 3.4.2 CSV File Adapter

**Purpose**: Write data to CSV format for compatibility with legacy tools

**Configuration**:
```json
{
  "WriteToCsv": true
}
```

**Output Location**: `C:\ProgramData\Wintap\csv\{sensor_type}\`

### 3.5 Creating a Custom Data Adapter

#### 3.5.1 Upload Adapter Interface

**Location:** `gov.llnl.wintap.core.etl.load.interfaces.IUpload`

```csharp
public interface IUpload
{
    string Name { get; set; }
    
    event EventHandler<string> UploadCompleted;
    
    /// <summary>
    /// Pre-upload setup (called once per upload cycle)
    /// </summary>
    bool PreUpload(Dictionary<string, string> parameters);
    
    /// <summary>
    /// Upload a single file
    /// </summary>
    Task<bool> Upload(string localFile, Dictionary<string, string> parameters);
    
    /// <summary>
    /// Post-upload cleanup
    /// </summary>
    bool PostUpload();
}
```

#### 3.5.2 Example: HTTP POST Adapter

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using gov.llnl.wintap.core.etl.load.interfaces;

namespace MyWintapAdapters
{
    public class HttpPostAdapter : IUpload
    {
        private HttpClient _httpClient;
        private string _endpoint;

        public string Name { get; set; } = "HttpPostAdapter";
        
        public event EventHandler<string> UploadCompleted;

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            try
            {
                _endpoint = parameters["Endpoint"];
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };
                
                if (parameters.ContainsKey("ApiKey"))
                {
                    _httpClient.DefaultRequestHeaders.Add("X-API-Key", parameters["ApiKey"]);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTTP adapter PreUpload failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> Upload(string localFile, Dictionary<string, string> parameters)
        {
            try
            {
                if (!File.Exists(localFile))
                {
                    Console.WriteLine($"File not found: {localFile}");
                    return false;
                }

                // Read file content
                var fileBytes = await File.ReadAllBytesAsync(localFile);
                
                // Create multipart form content
                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.Add("Content-Type", "application/octet-stream");
                    content.Add(fileContent, "file", Path.GetFileName(localFile));
                    
                    // Add metadata
                    content.Add(new StringContent(Environment.MachineName), "hostname");
                    content.Add(new StringContent(DateTime.UtcNow.ToString("o")), "timestamp");
                    
                    // Send request
                    var response = await _httpClient.PostAsync(_endpoint, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Successfully uploaded {Path.GetFileName(localFile)}");
                        UploadCompleted?.Invoke(this, localFile);
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Upload failed: {response.StatusCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Upload exception: {ex.Message}");
                return false;
            }
        }

        public bool PostUpload()
        {
            _httpClient?.Dispose();
            return true;
        }
    }
}
```

#### 3.5.3 Example: Database Adapter (SQL Server)

```csharp
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using gov.llnl.wintap.core.etl.load.interfaces;
using Parquet;

namespace MyWintapAdapters
{
    public class SqlServerAdapter : IUpload
    {
        private SqlConnection _connection;
        private string _connectionString;

        public string Name { get; set; } = "SqlServerAdapter";
        
        public event EventHandler<string> UploadCompleted;

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            try
            {
                var server = parameters["Server"];
                var database = parameters["Database"];
                var username = parameters.GetValueOrDefault("Username");
                var password = parameters.GetValueOrDefault("Password");
                
                if (string.IsNullOrEmpty(username))
                {
                    // Integrated Security
                    _connectionString = $"Server={server};Database={database};Integrated Security=true;";
                }
                else
                {
                    _connectionString = $"Server={server};Database={database};User Id={username};Password={password};";
                }
                
                _connection = new SqlConnection(_connectionString);
                _connection.Open();
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database connection failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> Upload(string localFile, Dictionary<string, string> parameters)
        {
            try
            {
                // Parse sensor type from filename
                var fileName = Path.GetFileNameWithoutExtension(localFile);
                var parts = fileName.Split('+');
                if (parts.Length < 2) return false;
                
                var sensorType = parts[1]; // e.g., "process_sensor"
                var tableName = $"wintap_{sensorType}";
                
                // Read Parquet file
                using (var stream = File.OpenRead(localFile))
                {
                    using (var parquetReader = await ParquetReader.CreateAsync(stream))
                    {
                        // Get schema
                        var schema = parquetReader.Schema;
                        
                        // Read all row groups
                        for (int i = 0; i < parquetReader.RowGroupCount; i++)
                        {
                            using (var rowGroupReader = parquetReader.OpenRowGroupReader(i))
                            {
                                // Build dynamic INSERT statement based on schema
                                var columns = string.Join(",", schema.Fields);
                                var placeholders = string.Join(",", schema.Fields.Select((_, idx) => $"@p{idx}"));
                                
                                var insertSql = $"INSERT INTO {tableName} ({columns}) VALUES ({placeholders})";
                                
                                // Execute for each row (simplified - batch for production)
                                using (var cmd = new SqlCommand(insertSql, _connection))
                                {
                                    // Add parameters and execute (implementation details omitted)
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }
                    }
                }
                
                Console.WriteLine($"Inserted data from {Path.GetFileName(localFile)} into {tableName}");
                UploadCompleted?.Invoke(this, localFile);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database insert failed: {ex.Message}");
                return false;
            }
        }

        public bool PostUpload()
        {
            _connection?.Close();
            _connection?.Dispose();
            return true;
        }
    }
}
```

### 3.6 Integrating Custom Adapters

**Step 1: Implement IUpload Interface**

Create your adapter class implementing `IUpload` interface.

**Step 2: Add to ETLConfig.json**

```json
{
  "Adapters": [
    {
      "Name": "HttpPostAdapter",
      "Enabled": true,
      "Properties": {
        "Endpoint": "https://api.example.com/telemetry",
        "ApiKey": "your-api-key-here"
      }
    }
  ]
}
```

**Step 3: Register Adapter in Uploader**

**Location:** `gov.llnl.wintap.core.etl.load.Uploader`

Modify the `Uploader` class to instantiate your adapter:

```csharp
// In Uploader.cs
private void LoadAdapters()
{
    var config = Utilities.GetETLConfig();
    
    foreach (var adapter in config.Adapters)
    {
        if (!adapter.Enabled) continue;
        
        IUpload adapterInstance = adapter.Name switch
        {
            "HttpPostAdapter" => new HttpPostAdapter(),
            "SqlServerAdapter" => new SqlServerAdapter(),
            // Add your adapters here
            _ => null
        };
        
        if (adapterInstance != null)
        {
            _adapters.Add(adapterInstance);
        }
    }
}
```

### 3.7 Data Batching and Compression

**Serialization Interval**: Controls how often data is flushed to disk

**Configuration:**
```json
{
  "SerializationIntervalSec": 60
}
```

**Batching Logic** (`Serializer.cs`):
```csharp
private Timer flushToDiskTimer;

private void initSensor()
{
    flushToDiskTimer = new Timer();
    flushToDiskTimer.Interval = Utilities.GetETLConfig().SerializationIntervalSec * 1000;
    flushToDiskTimer.AutoReset = true;
    flushToDiskTimer.Elapsed += FlushToDiskTimer_Elapsed;
    flushToDiskTimer.Start();
}

private void FlushToDiskTimer_Elapsed(object sender, ElapsedEventArgs e)
{
    int currentQueueDepth = sensorData.Count;
    List<ExpandoObject> tempQueue = new List<ExpandoObject>();
    
    // Dequeue all pending events
    for (int i = 0; i < currentQueueDepth; i++)
    {
        if (sensorData.TryDequeue(out ExpandoObject msg))
        {
            tempQueue.Add(msg);
        }
    }
    
    // Serialize batch to Parquet
    if (tempQueue.Count > 0)
    {
        serialize(tempQueue);
    }
}
```

**Upload Interval**: Controls how often files are uploaded to adapters

```json
{
  "UploadIntervalSec": 300
}
```

### 3.8 Schema Generation

Wintap uses **dynamic schema generation** from the first `ExpandoObject` in each batch:

```csharp
private static ParquetSchema DetermineSchemaFromExpando(ExpandoObject firstItem)
{
    List<Field> fields = new List<Field>();
    
    foreach (var kvp in firstItem)
    {
        Type type = kvp.Value?.GetType();
        DataField field = new DataField(kvp.Key, type);
        fields.Add(field);
    }
    
    return new ParquetSchema(fields.ToArray());
}
```

**Important**: All objects in a batch must have the same schema. Wintap groups by `MessageType` to ensure consistency.

---

## 4. Multi-Platform Development

### 4.1 Cross-Platform Architecture

Wintap is designed with platform abstraction to support Windows, Linux, and macOS:

```
┌─────────────────────────────────────────────────┐
│         Platform-Agnostic Core                  │
│  - PluginManager                                │
│  - EventChannel                                 │
│  - Serialization Pipeline                       │
└───────────────┬─────────────────────────────────┘
                │
    ┌───────────┼───────────┐
    │           │           │
    ▼           ▼           ▼
┌────────┐ ┌────────┐ ┌────────┐
│Windows │ │ Linux  │ │ macOS  │
│ Platform│ │Platform│ │Platform│
│         │ │        │ │        │
│ - ETW   │ │- eBPF  │ │- EndSec│
│ - Sec   │ │- Audit │ │- FSEvent│
│   Logs  │ │- Proc  │ │        │
└────────┘ └────────┘ └────────┘
```

### 4.2 Platform Detection

**Runtime Detection:**
```csharp
using System.Runtime.InteropServices;

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    // Windows-specific code
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    // Linux-specific code
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    // macOS-specific code
}
```

### 4.3 Windows-Specific Components

The following components are **Windows-only** and require abstraction for Linux/macOS:

#### 4.3.1 ETW (Event Tracing for Windows)

**Namespace:** `gov.llnl.wintap.platform.windows.*`

**Key Classes:**
- `EtwKernelCollector.cs`: Kernel-mode ETW session
- `ProcessSensor.cs`: Process events via ETW
- `TcpSensor.cs`: Network connection tracking
- `FileSensor.cs`: File system activity
- `RegistrySensor.cs`: Registry modifications

**Dependencies:**
- `Microsoft.Diagnostics.Tracing.TraceEvent` NuGet package
- Windows APIs: `TraceEventSession`, `ETWTraceEventSource`

**Linux Alternative**: eBPF, Auditd, `/proc` filesystem

#### 4.3.2 Security Event Logs

**Purpose**: Process creation/termination events from Windows Security logs

Wintap uses Windows Security Event Logs for process events rather than ETW's Process provider to avoid the complexities of ETW event correlation. Specifically, ETW process events require correlating Process start/stop events with ImageLoad events to obtain complete process information, including the executable path and command-line arguments. Security Event Logs provide this information in a single, atomic event.

Additionally, the Windows Security Event Log is a durable and reliable store for process events once properly configured with audit policies. Events are persisted to disk and survive system crashes, unlike in-memory ETW sessions which can lose events during system instability.

**Class:** `ProcessSensor.cs` → `startSecurityLogMonitoring()`

**Event IDs:**
- 4688: Process creation (A new process has been created)
- 4689: Process termination (A process has exited)

**Required Audit Policy Configuration:**

To enable process tracking, the following audit policies must be configured:

```powershell
# Enable process creation auditing
auditpol /set /subcategory:"Process Creation" /success:enable

# Enable process termination auditing
auditpol /set /subcategory:"Process Termination" /success:enable
```

**Event Data Extracted:**
- Process name and executable path
- Command-line arguments (requires additional policy)
- Parent process ID
- User/security context
- Process creation time

**Linux Alternative**: Auditd rules for `execve` syscalls, or eBPF process tracing

### 4.4 Abstraction Layer: SubscriptionManager

**Location:** `gov.llnl.wintap.core.infrastructure.SubscriptionManager`

This is the **key abstraction point** for platform-specific collectors:

```csharp
internal SubscriptionManager()
{
    winCollectors = new List<BaseWindowsSensor>();
    etwCollectors = new List<EtwProviderCollector>();
    winSubMgr = new WindowsSubscriptionManager();

    linuxCollectors = new List<BaseSensor>();
    linuxSubMgr = new LinuxSubscriptionManager();
}

internal void Start()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        WintapLogger.Log.Append("Starting WindowsSubscriptionManager", LogLevel.Info);
        winCollectors = winSubMgr.Start();
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        WintapLogger.Log.Append("Starting LinuxSubscriptionManager", LogLevel.Info);
        linuxCollectors = linuxSubMgr.Start();
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        WintapLogger.Log.Append("Starting macOSSubscriptionManager", LogLevel.Info);
        // macCollectors = macSubMgr.Start();
    }
}
```

### 4.5 Porting to Linux: Example Implementation

The key to extending Wintap to Linux is creating platform-specific collectors that integrate with Wintap's event pipeline through **`EventChannel.Send()`**. This method is the central integration point that connects your Linux telemetry sources to the rest of Wintap's infrastructure (plugins, serializers, adapters).

#### 4.5.1 Integration Architecture

```
Linux Data Source (procfs, eBPF, OSQuery)
    │
    ▼
Your Linux Collector Class
    │
    ▼
EventChannel.Send(WintapMessage)  ← KEY INTEGRATION POINT
    │
    ├──→ Plugin Subscribers
    ├──→ Esper CEP Engine
    ├──→ Serializers
    └──→ Data Adapters
```

**Critical Concept:** Once you call `EventChannel.Send(wintapMessage)`, your telemetry data flows through the entire Wintap pipeline automatically. Plugins receive it, Esper processes it, serializers write it to Parquet files, and adapters route it to configured destinations. You don't need to implement any of that—just create valid `WintapMessage` objects.

#### 4.5.2 Example: Linux Process Collector Using /proc

Here's a complete example showing how to collect Linux process data and integrate it with Wintap:

**Namespace:** `gov.llnl.wintap.platform.linux.collect` (to be created)

```csharp
using System;
using System.IO;
using System.Linq;
using System.Timers;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.collect;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Linux process collector using /proc filesystem.
    /// Demonstrates integration with Wintap event pipeline.
    /// </summary>
    public class LinuxProcessCollector : BaseSensor
    {
        private Timer _pollTimer;
        private Dictionary<int, ProcessInfo> _lastSeenProcesses;

        public override bool Start()
        {
            WintapLogger.Log.Append("Starting Linux process collector", LogLevel.Info);
            
            _lastSeenProcesses = new Dictionary<int, ProcessInfo>();
            
            // Poll /proc every 2 seconds
            _pollTimer = new Timer(2000);
            _pollTimer.Elapsed += PollProcesses;
            _pollTimer.Start();
            
            return true;
        }

        private void PollProcesses(object sender, ElapsedEventArgs e)
        {
            try
            {
                var currentProcesses = new Dictionary<int, ProcessInfo>();
                
                // Enumerate /proc/[pid] directories
                var procDirs = Directory.GetDirectories("/proc")
                    .Where(d => int.TryParse(Path.GetFileName(d), out _))
                    .ToList();
                
                foreach (var procDir in procDirs)
                {
                    var pid = int.Parse(Path.GetFileName(procDir));
                    var processInfo = ReadProcessInfo(pid);
                    
                    if (processInfo != null)
                    {
                        currentProcesses[pid] = processInfo;
                        
                        // New process detected?
                        if (!_lastSeenProcesses.ContainsKey(pid))
                        {
                            SendProcessStartEvent(processInfo);
                        }
                    }
                }
                
                // Detect terminated processes
                foreach (var pid in _lastSeenProcesses.Keys)
                {
                    if (!currentProcesses.ContainsKey(pid))
                    {
                        SendProcessStopEvent(_lastSeenProcesses[pid]);
                    }
                }
                
                _lastSeenProcesses = currentProcesses;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error polling processes: {ex.Message}", 
                    LogLevel.Error);
            }
        }

        private ProcessInfo ReadProcessInfo(int pid)
        {
            try
            {
                // Read /proc/[pid]/cmdline
                var cmdlineFile = $"/proc/{pid}/cmdline";
                if (!File.Exists(cmdlineFile)) return null;
                
                var cmdline = File.ReadAllText(cmdlineFile)
                    .Replace("\0", " ").Trim();
                
                // Read /proc/[pid]/stat for process name and parent PID
                var statFile = $"/proc/{pid}/stat";
                var stat = File.ReadAllText(statFile);
                
                // Parse stat format: pid (comm) state ppid ...
                var statParts = stat.Split(' ');
                var processName = statParts[1].Trim('(', ')');
                var ppid = int.Parse(statParts[3]);
                
                // Read /proc/[pid]/exe for executable path
                var exePath = "/proc/" + pid + "/exe";
                string executablePath;
                try
                {
                    executablePath = new FileInfo(exePath).LinkTarget ?? exePath;
                }
                catch
                {
                    executablePath = "unknown";
                }
                
                // Get process owner
                var statusFile = $"/proc/{pid}/status";
                var userName = GetProcessUser(statusFile);
                
                return new ProcessInfo
                {
                    PID = pid,
                    Name = processName,
                    Path = executablePath,
                    CommandLine = cmdline,
                    ParentPID = ppid,
                    User = userName,
                    CreateTime = DateTime.UtcNow // Simplified; can read from /proc/[pid]/stat
                };
            }
            catch
            {
                // Process may have exited during read
                return null;
            }
        }

        private string GetProcessUser(string statusFile)
        {
            try
            {
                var statusLines = File.ReadAllLines(statusFile);
                var uidLine = statusLines.FirstOrDefault(l => l.StartsWith("Uid:"));
                
                if (uidLine != null)
                {
                    var parts = uidLine.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        var uid = parts[1];
                        // Convert UID to username (simplified)
                        return $"uid_{uid}";
                    }
                }
            }
            catch { }
            
            return "unknown";
        }

        /// <summary>
        /// KEY METHOD: Send process start event to Wintap pipeline
        /// </summary>
        private void SendProcessStartEvent(ProcessInfo process)
        {
            // Create WintapMessage object
            var msg = new WintapMessage(
                receiveTime: DateTime.UtcNow,
                pid: process.PID,
                messageType: WintapMessage.MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Start,
                Process = new WintapMessage.ProcessObject
                {
                    Name = process.Name,
                    Path = process.Path,
                    CommandLine = process.CommandLine,
                    ParentPID = process.ParentPID,
                    User = process.User
                }
            };

            // THIS IS THE KEY: Send to EventChannel
            // From this point forward, Wintap handles everything:
            // - Plugins will receive this event if subscribed to EventFlags.Process
            // - Esper CEP will process it for queries
            // - Serializers will write it to Parquet/CSV
            // - Adapters will route it to configured destinations
            EventChannel.Send(msg);
        }

        private void SendProcessStopEvent(ProcessInfo process)
        {
            var msg = new WintapMessage(
                receiveTime: DateTime.UtcNow,
                pid: process.PID,
                messageType: WintapMessage.MessageTypeEnum.Process)
            {
                ActivityType = WintapMessage.ActivityTypeEnum.Stop,
                Process = new WintapMessage.ProcessObject
                {
                    Name = process.Name,
                    Path = process.Path,
                    ParentPID = process.ParentPID
                }
            };

            EventChannel.Send(msg); // Integration point!
        }

        public override void Stop()
        {
            WintapLogger.Log.Append("Stopping Linux process collector", LogLevel.Info);
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
        }

        private class ProcessInfo
        {
            public int PID { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
            public string CommandLine { get; set; }
            public int ParentPID { get; set; }
            public string User { get; set; }
            public DateTime CreateTime { get; set; }
        }
    }
}
```

#### 4.5.3 Registering Your Linux Collector

Add your collector to `LinuxSubscriptionManager`:

```csharp
// File: gov.llnl.wintap.platform.linux.infrastructure.LinuxSubscriptionManager.cs

using System.Collections.Generic;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.platform.linux.collect;

namespace gov.llnl.wintap.platform.linux.infrastructure
{
    public class LinuxSubscriptionManager
    {
        internal List<BaseSensor> Start()
        {
            var collectors = new List<BaseSensor>();
            
            // Add your Linux collectors here
            var processCollector = new LinuxProcessCollector();
            if (processCollector.Start())
            {
                collectors.Add(processCollector);
            }
            
            // Add additional collectors:
            // - LinuxNetworkCollector
            // - LinuxFileCollector
            // - OsQueryCollector
            
            return collectors;
        }
    }
}
```

#### 4.5.4 Key Takeaways for Linux Porting

1. **EventChannel.Send() is Everything**: This single method call integrates your telemetry with all of Wintap's infrastructure. Focus on creating valid `WintapMessage` objects, not reimplementing data flow.

2. **Use Existing Data Models**: Populate the standard `WintapMessage` objects (Process, TcpConnection, File, etc.). This ensures compatibility with existing plugins and serializers.

3. **Follow the Pattern**: Windows collectors use ETW → create WintapMessage → call EventChannel.Send(). Linux collectors use /proc, eBPF, or OSQuery → create WintapMessage → call EventChannel.Send(). The pattern is identical.

4. **Platform-Specific Code Isolation**: Keep Linux-specific code in `gov.llnl.wintap.platform.linux.*` namespaces. The core infrastructure (`EventChannel`, `PluginManager`, etc.) remains platform-agnostic.

5. **Test Data Flow**: After calling `EventChannel.Send()`, verify your data:
   - Check Parquet files: `/var/lib/wintap/parquet/process_sensor/`
   - Verify plugins receive events
   - Confirm serialization to configured adapters

For complete Linux deployment, see **Appendix: Wintap Linux Deployment Guide**.

### 4.6 Building for Linux

**Target Framework:**
```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <RuntimeIdentifiers>win-x64;linux-x64;osx-x64</RuntimeIdentifiers>
</PropertyGroup>
```

**Build Commands:**

Linux Build:
```bash
dotnet build -c Release -r linux-x64
```

Self-Contained Deployment:
```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

For detailed Linux deployment instructions including systemd service configuration, see **Appendix: Wintap Linux Deployment Guide**.

### 4.7 macOS Considerations

**Key Differences:**
- **Process Monitoring**: Use `kqueue` or Endpoint Security Framework (requires special entitlements)
- **Network**: BSD socket APIs, similar to Linux
- **File System**: FSEvents API (more efficient than polling)
- **Permissions**: System extensions require notarization and user approval

**Endpoint Security Framework Example:**

```csharp
// Requires P/Invoke to EndpointSecurity framework
// macOS 10.15+, requires com.apple.developer.endpoint-security.client entitlement

[DllImport("/System/Library/Frameworks/EndpointSecurity.framework/EndpointSecurity")]
private static extern int es_new_client(out IntPtr client, IntPtr handler);

public class EndpointSecurityCollector : BaseSensor
{
    private IntPtr _esClient;
    
    public override bool Start()
    {
        // Register for process events
        int result = es_new_client(out _esClient, 
            Marshal.GetFunctionPointerForDelegate<EventHandler>(HandleEvent));
        
        if (result == 0)
        {
            // Subscribe to ES_EVENT_TYPE_AUTH_EXEC and ES_EVENT_TYPE_NOTIFY_EXIT
            // (Implementation details require native interop)
            return true;
        }
        
        return false;
    }
    
    private void HandleEvent(IntPtr event)
    {
        // Parse es_message_t structure
        // Convert to WintapMessage and send to EventChannel
    }
}
```

---

## 5. Developer Setup and Contribution Guide

### 5.1 Development Environment Setup

#### 5.1.1 Prerequisites

**Windows:**
- Visual Studio 2022 (Community, Professional, or Enterprise) **OR** Visual Studio Code
- .NET 8.0 SDK
- Windows 10/11 or Windows Server 2019+

**Linux:**
- Visual Studio Code (recommended) or any text editor
- .NET 8.0 SDK
- GCC/Clang (for native dependencies)
- sudo privileges (for system monitoring)

**Common Tools:**
- Git
- NuGet CLI (included with .NET SDK)

#### 5.1.2 Installing .NET 8.0 SDK

**Windows:**

Download and install from https://dotnet.microsoft.com/download/dotnet/8.0

**Linux (Ubuntu/Debian):**
```bash
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

**Verify Installation:**
```bash
dotnet --version
# Should output: 8.0.x
```

#### 5.1.3 Visual Studio Code Setup

Visual Studio Code provides an excellent cross-platform development experience for Wintap.

**Install VS Code:**
- Download from https://code.visualstudio.com/

**Required Extensions:**
1. **C# Dev Kit** (Microsoft)
   - Provides IntelliSense, debugging, and project management
2. **C#** (Microsoft)
   - Language support for C#

**Recommended Extensions:**
3. **GitLens** - Enhanced Git integration
4. **Remote - SSH** - For remote Linux development
5. **Docker** - Container management (for testing)

**VS Code Configuration:**

Create `.vscode/launch.json` in your project root:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": ".NET Core Launch (console)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/bin/Debug/net8.0/Wintap.dll",
            "args": [],
            "cwd": "${workspaceFolder}",
            "console": "internalConsole",
            "stopAtEntry": false
        },
        {
            "name": ".NET Core Attach",
            "type": "coreclr",
            "request": "attach"
        }
    ]
}
```

Create `.vscode/tasks.json`:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "${workspaceFolder}/Wintap.sln",
                "/property:GenerateFullPaths=true",
                "/consoleloggerparameters:NoSummary"
            ],
            "problemMatcher": "$msCompile"
        },
        {
            "label": "build-linux",
            "command": "dotnet",
            "type": "process",
            "args": [
                "publish",
                "-c", "Release",
                "-r", "linux-x64",
                "--self-contained",
                "-p:PublishSingleFile=true"
            ],
            "problemMatcher": "$msCompile"
        }
    ]
}
```

**Building Linux Binaries from Windows (VS Code):**

1. Open integrated terminal in VS Code (`Ctrl+` `)
2. Run the build task:
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
   ```
3. Binaries will be in: `.\bin\Release\net8.0\linux-x64\publish\`

**Remote Development on Linux:**

VS Code's Remote-SSH extension allows you to develop directly on Linux:

1. Install Remote-SSH extension
2. Configure SSH connection:
   - Press `F1` → "Remote-SSH: Connect to Host"
   - Enter: `user@linuxhost` or use Multipass: `multipass@vm-name`
3. Open Wintap folder on remote host
4. Development happens on Linux with full debugging support

### 5.2 Building Wintap

#### 5.2.1 Command Line Build

**Clone Repository:**
```bash
git clone https://github.com/LLNL/wintap.git
cd wintap
```

**Restore Dependencies:**
```bash
dotnet restore
```

**Build (Debug):**
```bash
dotnet build -c Debug
```

**Build (Release):**
```bash
dotnet build -c Release
```

#### 5.2.2 Visual Studio / VS Code Build

**Visual Studio:**
1. Open `wintap.sln` in Visual Studio
2. Select build configuration (Debug/Release)
3. Press `Ctrl+Shift+B` or go to **Build → Build Solution**

**Visual Studio Code:**
1. Open project folder in VS Code
2. Open integrated terminal (`Ctrl+` `)
3. Run: `dotnet build`

Or use the build task:
- Press `Ctrl+Shift+B` → Select "build" or "build-linux"

**Output Location:**
```
bin\Debug\net8.0\           (Windows)
bin\Release\net8.0\         (Windows)
bin\Release\net8.0\linux-x64\publish\  (Linux)
```

### 5.3 Running and Debugging

#### 5.3.1 Development Mode (Windows)

**Run as Console Application:**
```bash
dotnet run --project gov.llnl.wintap
```

**Debugging in Visual Studio:**
1. Set `gov.llnl.wintap` as the startup project
2. Press `F5` to debug or `Ctrl+F5` to run without debugging

**Important**: Running as a console app bypasses Windows Service infrastructure. For full testing, install as a service.

#### 5.3.2 Installing as Windows Service

**Build Release:**
```bash
dotnet publish -c Release -r win-x64
```

**Install Service:**
```powershell
# Run PowerShell as Administrator
sc.exe create Wintap binPath= "C:\Path\To\Wintap.exe" start=auto
sc.exe start Wintap
```

**Verify:**
```powershell
sc.exe query Wintap
```

**View Logs:**
```powershell
Get-Content "C:\ProgramData\Wintap\logs\wintap.log" -Tail 50 -Wait
```

#### 5.3.3 Debugging Plugins

**Option 1: Attach to Running Service**
1. Start Wintap service
2. In Visual Studio: **Debug → Attach to Process**
3. Select `Wintap.exe`
4. Set breakpoints in plugin code

**Option 2: Plugin Unit Testing**

Create a test harness that loads plugins without the full service:

```csharp
using Xunit;
using gov.llnl.wintap;
using static gov.llnl.wintap.Interfaces;

public class PluginTests
{
    [Fact]
    public void TestPluginStartup()
    {
        var plugin = new MyPlugin();
        var flags = plugin.Startup();
        
        Assert.True(flags.HasFlag(EventFlags.Process));
    }
    
    [Fact]
    public void TestPluginSubscribe()
    {
        var plugin = new MyPlugin();
        plugin.Startup();
        
        var testMessage = new WintapMessage(DateTime.UtcNow, 1234,
            WintapMessage.MessageTypeEnum.Process);
        
        // Should not throw
        plugin.Subscribe(testMessage);
    }
}
```

### 5.4 Coding Conventions

#### 5.4.1 Naming Conventions

| Type | Convention | Example |
|------|-----------|---------|
| Namespaces | PascalCase, dot-separated | `gov.llnl.wintap.core.infrastructure` |
| Classes | PascalCase | `PluginManager` |
| Interfaces | PascalCase with `I` prefix | `ISubscribe` |
| Methods | PascalCase | `RegisterPlugins()` |
| Private fields | camelCase with `_` prefix | `_pluginManager` |
| Constants | PascalCase | `MaxRetryCount` |
| Local variables | camelCase | `processId` |

#### 5.4.2 Code Style

**Use modern C# features:**
```csharp
// Pattern matching
if (obj is WintapMessage msg && msg.MessageType == MessageTypeEnum.Process)
{
    ProcessMessage(msg);
}

// Expression-bodied members
public string Name => "MyPlugin";

// Null-conditional operators
var path = process?.Path ?? "unknown";
```

**Exception Handling:**
```csharp
try
{
    // Operation
}
catch (Exception ex)
{
    WintapLogger.Log.Append($"Error: {ex.Message}", LogLevel.Error);
    // Don't throw in plugins - contain failures
}
```

**Async/Await:**
```csharp
public async Task<bool> Upload(string file, Dictionary<string, string> parameters)
{
    try
    {
        var response = await _httpClient.PostAsync(url, content);
        return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
        return false;
    }
}
```

#### 5.4.3 Comments and Documentation

**XML Documentation for Public APIs:**
```csharp
/// <summary>
/// Registers and initializes all discovered plugins.
/// </summary>
/// <param name="watchdog">The watchdog instance to monitor plugin execution.</param>
/// <remarks>
/// This method loads plugins from the configured plugin directory using MEF,
/// registers event handlers, and starts the plugin scheduler for IRun plugins.
/// </remarks>
internal void RegisterPlugins(Watchdog watchdog)
{
    // Implementation
}
```

**Inline Comments for Complex Logic:**
```csharp
// ETW sometimes stores CreateTime in different formats
// Try parsing as TimeSpan first, fall back to DateTime
if (etwFormat.ToLower().Contains("ms"))
{
    TimeSpan createTS = TimeSpan.Parse(createTime);
    returnDT = DateTime.Now.Date + createTS;
}
```

### 5.5 Contribution Workflow
TBD

### 5.6 Release Process

**Version Numbering**: Semantic Versioning (MAJOR.MINOR.PATCH)

**Release Checklist:**
1. Update version in `AssemblyInfo.cs`
2. Update `CHANGELOG.md`
3. Run full test suite
4. Build release binaries for all platforms
5. Sign assemblies with Authenticode certificate
6. Create GitHub release with binaries
7. Update documentation

**Build Release Artifacts:**
```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained -o releases/wintap-windows-x64

# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained -o releases/wintap-linux-x64

# macOS x64
dotnet publish -c Release -r osx-x64 --self-contained -o releases/wintap-macos-x64
```

---

## Appendix: Wintap Linux Deployment Guide

### Prerequisites

* Windows development machine with .NET 8 SDK
* Linux host (Ubuntu 24.04 or similar)
* Multipass VM or direct Linux access

### Step 1: Build Linux Binaries (Windows Host)

```bash
# Navigate to project directory
cd C:\Repos\Wintap7\wintap

# Build self-contained Linux binary
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true

# Binaries will be at:
# .\bin\Release\net8.0\linux-x64\publish\
```

### Step 2: Transfer to Linux Host

**Using Multipass:**

```bash
# Transfer files to VM
multipass transfer -r .\bin\Release\net8.0\linux-x64\publish noteworthy-pika:/home/ubuntu/wintap
```

**Using SCP (direct Linux host):**

```bash
scp -r .\bin\Release\net8.0\linux-x64\publish user@linuxhost:/home/user/wintap
```

### Step 3: Install Prerequisites (Linux Host)

```bash
# SSH into Linux host
multipass shell noteworthy-pika  # or ssh user@linuxhost

# Update packages
sudo apt update

# Install OSquery (for Linux telemetry collection)
wget https://pkg.osquery.io/deb/osquery_5.12.2-1.linux_amd64.deb
sudo dpkg -i osquery_5.12.2-1.linux_amd64.deb
sudo apt install -f

# Verify installation
osqueryi --version
```

### Step 4: Deploy Wintap

```bash
# Create directory structure
sudo mkdir -p /var/lib/wintap/{ProcessTree,Logs,Config}
sudo mkdir -p /opt/wintap

# Copy binaries
sudo cp -r ~/wintap/* /opt/wintap/

# Set permissions
sudo chmod +x /opt/wintap/Wintap
sudo chown -R root:root /opt/wintap
sudo chown -R root:root /var/lib/wintap
```

### Step 5: Create Systemd Service

```bash
# Create service file
sudo nano /etc/systemd/system/wintap.service
```

Paste this configuration:

```ini
[Unit]
Description=Wintap Telemetry Service
After=network.target osqueryd.service
Requires=osqueryd.service

[Service]
Type=notify
ExecStart=/opt/wintap/Wintap
WorkingDirectory=/opt/wintap
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal
SyslogIdentifier=wintap

# Security settings
NoNewPrivileges=false
PrivateTmp=yes

[Install]
WantedBy=multi-user.target
```

Save: `Ctrl+O`, `Enter`, `Ctrl+X`

### Step 6: Start and Enable Services

```bash
# Reload systemd
sudo systemctl daemon-reload

# Enable services to start on boot
sudo systemctl enable osqueryd
sudo systemctl enable wintap

# Start OSquery first
sudo systemctl start osqueryd

# Start Wintap
sudo systemctl start wintap

# Verify status
sudo systemctl status wintap
```

### Monitoring and Verification

**Check Service Status:**

```bash
sudo systemctl status wintap
```

**View Logs:**

```bash
# Systemd journal (live)
sudo journalctl -u wintap -f

# Wintap application log
sudo tail -f /usr/share/Wintap/Logs/Wintap.log
```

---

## Appendix A: WintapMessage Schema Reference

**Complete WintapMessage structure** for reference when writing plugins:

```csharp
public class WintapMessage
{
    // Message metadata
    public DateTime ReceiveTime { get; set; }
    public int PID { get; set; }
    public string PidHash { get; set; }
    public string ProcessName { get; set; }
    public MessageTypeEnum MessageType { get; set; }
    public ActivityTypeEnum ActivityType { get; set; }
    public string CorrelationId { get; set; }
    public string ActivityId { get; set; }
    public string AgentId { get; set; }

    // Event type-specific objects (only one populated per message)
    public ProcessObject Process { get; set; }
    public TcpConnectionObject TcpConnection { get; set; }
    public UdpPacketObject UdpPacket { get; set; }
    public ImageLoadObject ImageLoad { get; set; }
    public FileActivityObject File { get; set; }
    public RegActivityObject Registry { get; set; }
    public SessionChangeObject SessionChange { get; set; }
    public UIData UI { get; set; }
    public GenericMessageObject GenericMessage { get; set; }
    public WmiActivityObject WMI { get; set; }
    public ThreadStartObject Thread { get; set; }
    public EventlogEventObject EventLogEvent { get; set; }
    public MicrosoftWindowsCpuTriggerData CpuTrigger { get; set; }
    public MicrosoftWindowsGroupPolicyData GroupPolicy { get; set; }
    public ApiCallData ApiCall { get; set; }
    public MemoryMapData MemoryMap { get; set; }
    public SysdigEventData Sysdig { get; set; }
    public WintapAlertData WintapAlert { get; set; }
}

public class ProcessObject
{
    public int ParentPID { get; set; }
    public string ParentPidHash { get; set; }
    public string ParentProcessName { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string CommandLine { get; set; }
    public string Arguments { get; set; }
    public string User { get; set; }
    public long ExitCode { get; set; }
    public long CPUCycleCount { get; set; }
    public int CPUUtilization { get; set; }
    public long CommitCharge { get; set; }
    public long CommitPeak { get; set; }
    public long ReadOperationCount { get; set; }
    public long WriteOperationCount { get; set; }
    public long ReadTransferKiloBytes { get; set; }
    public long WriteTransferKiloBytes { get; set; }
    public int HardFaultCount { get; set; }
    public int TokenElevationType { get; set; }
    public string UniqueProcessKey { get; set; }
    public string MD5 { get; set; }
    public string SHA2 { get; set; }
}

public class TcpConnectionObject
{
    public DirectionEnum Direction { get; set; }
    public string SourceAddress { get; set; }
    public int SourcePort { get; set; }
    public string DestinationAddress { get; set; }
    public int DestinationPort { get; set; }
    public StateEnum State { get; set; }
    public long PacketSize { get; set; }
    public int PID { get; set; }
    // ... additional fields
}

public class UdpPacketObject
{
    public string SourceAddress { get; set; }
    public int SourcePort { get; set; }
    public string DestinationAddress { get; set; }
    public int DestinationPort { get; set; }
    public long PacketSize { get; set; }
    public int PID { get; set; }
}

public class FileActivityObject
{
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public long FileSize { get; set; }
    public string FileExtension { get; set; }
    // ... additional fields
}

public class RegActivityObject
{
    public string KeyName { get; set; }
    public string ValueName { get; set; }
    public DataTypeEnum DataType { get; set; }
    public string Data { get; set; }
}

// ... Additional object types
```

---

## Appendix B: Useful Resources

### Documentation
- **Esper EPL Reference**: https://www.espertech.com/esper/
- **.NET Platform**: https://docs.microsoft.com/en-us/dotnet/
- **Parquet Format**: https://parquet.apache.org/
- **GitFlow Workflow**: https://nvie.com/posts/a-successful-git-branching-model/

### Tools
- **PerfView** (Windows performance analysis): https://github.com/microsoft/perfview
- **dotTrace** (Profiling): https://www.jetbrains.com/profiler/
- **Process Monitor** (Sysinternals): https://docs.microsoft.com/en-us/sysinternals/
- **Visual Studio Code**: https://code.visualstudio.com/

### Learning Resources
- **ETW Deep Dive**: https://docs.microsoft.com/en-us/windows-hardware/drivers/devtest/event-tracing-for-windows--etw-
- **eBPF Guide**: https://ebpf.io/
- **MEF Programming Guide**: https://docs.microsoft.com/en-us/dotnet/framework/mef/
- **OSQuery Documentation**: https://osquery.io/

---

**Document Version:** 7.0  
**Last Updated:** October 2025  
**Maintained by:** Wintap Development Team  
**License:** See project LICENSE file

**For questions or contributions, please visit:**  
https://github.com/LLNL/wintap