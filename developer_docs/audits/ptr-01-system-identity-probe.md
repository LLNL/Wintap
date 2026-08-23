# Audit Artifact: ptr-01 System Identity Probe And Precondition

**Date:** 2026-08-23
**Instruction:** `developer_docs/instructions/ptr-01-system-identity-probe.md`
**Status:** Complete

## Scope Implemented

- Exposed the existing Windows process-start probe as `internal static` without changing its logic and added the Windows-only, null-returning PID 4 wrapper `TryGetSystemStartFileTimeUtc()`.
- Added the no-maintenance `IsProcessRowOpen` lookup as an internal static DuckDB test seam, a locked and exception-contained `ProcessResolver` member, an `IProcessResolver` member, and a null-safe `EventChannel` passthrough.
- Refactored the existing event-store DDL into `EnsureEventStoreTables(DuckDBConnection)` and retained the same two tables and columns.
- Added isolated temp-file DuckDB tests for DDL idempotence, open/closed/missing/empty/null/quoted hashes, the current-process Windows start-time probe, and an invalid PID.
- Performed the approved instruction's blocking Step 0 audit before implementation. The precondition **HOLDS**.

## Files Created

- `tests/Wintap.Tests/SystemIdentityProbeTests.cs`
- `developer_docs/audits/ptr-01-system-identity-probe.md`

## Files Modified

- `wintap/core/infrastructure/ProcessResolver.cs`
- `wintap/core/infrastructure/IProcessResolver.cs`
- `wintap/core/infrastructure/EventChannel.cs`

No source, test, instruction, or wiki file was edited while preparing this audit artifact.

## Step 0 Precondition: HOLDS

The durable process-event path is driven by each live `WintapMessage`; it does not read process rows back from ProcessResolver's `event_store/main.duckdb`.

### Live event to Esper

- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs:82-105` establishes the sensor's `emit` delegate and defaults it to `EventChannel.Send`.
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs:770-803` constructs a process Start `WintapMessage` and invokes `emit(message)`.
- `wintap/platform/windows/sensor/etw/WindowsProcessSensor.cs:984-1060` constructs a process Stop `WintapMessage` and invokes the same live emission path.
- `wintap/core/infrastructure/EventChannel.cs:221-253` receives the live message, tags it with the agent ID, and only diverts it when the separately configured direct Parquet sink is enabled.
- `wintap/core/infrastructure/EventChannel.cs:376-391` registers process events with the resolver cache and then independently sends the same live event to Esper through `EsperRuntime.EventService.SendEventBean(streamedEvent, "WintapMessage")`. Registration is not a source for the Esper send.

### Esper to serializer queues

- `wintap/core/etl/WintapETL.cs:101-128` constructs and installs both `ProcessSerializer` with `process.epl` and `ProcessStopSerializer` with `process-stop.epl`, then includes both instances in the serializer list.
- `wintap/core/etl/extract/Serializer.cs:422-429` compiles/deploys each serializer's EPL and attaches `ProcStatement_Events` to the statement's event callback.
- `wintap/core/etl/extract/Serializer.cs:472-480` forwards each Esper `EventBean` directly to the serializer's `HandleSensorEvent`.
- `wintap/core/etl/extract/ProcessSerializer.cs:42-67` casts `sensorEvent.Underlying` directly to the original `WintapMessage`, handles Start/Refresh, flattens it, and calls `Save`.
- `wintap/core/etl/extract/ProcessStopSerializer.cs:32-54` likewise casts `sensorEvent.Underlying` directly to `WintapMessage`, handles Stop, flattens it, and calls `Save`.
- `wintap/core/etl/extract/Serializer.cs:163-209` enqueues the flattened event in the serializer's in-memory queue; no ProcessResolver query appears in this path.

### Serializer queues to durable raw_sensor Parquet

- `wintap/core/etl/extract/Serializer.cs:308-399` drains queued flattened events, creates `ParquetWriter.Batch.SensorData`, and submits the batch to `ParquetWriter.Add`.
- `wintap/core/etl/load/ParquetWriter.cs:77-105` drains batches, calls `Write`, and atomically renames completed `.parquet.active` files to `.parquet` intermediate files.
- `wintap/core/etl/load/ParquetWriter.cs:197-232` derives the process/process-stop intermediate file, opens a `FileStream`, and serializes the event data with `ParquetSerializer.SerializeAsync`.
- `wintap/core/etl/load/CacheManager.cs:289-300` periodically invokes `doMerge`; `wintap/core/etl/load/CacheManager.cs:431-457` invokes `Merge.Start` for each serializer directory.
- `wintap/core/etl/load/Merge.cs:33-76` builds an intermediate-Parquet glob and calls `RawSensorWriter.MaterializeFromParquetGlob`; it does not read the resolver database.
- `wintap/core/etl/load/RawSensorWriter.cs:16-52` maps the merged output into the partitioned `raw_sensor` tree and writes the destination Parquet.
- `wintap/core/etl/load/RawSensorWriter.cs:97-128` maps `raw_processstop` to `raw_process`; normal `raw_process` remains `raw_process` through the default mapping.
- `wintap/core/etl/load/RawSensorWriter.cs:131-156` uses a separate in-memory DuckDB connection to `read_parquet` from intermediate Parquet and `COPY` the result to final `raw_sensor` Parquet. It never opens ProcessResolver's database.

### Role of event_store/main.duckdb and other readers

- `wintap/core/infrastructure/ProcessResolver.cs:21-29` identifies the resolver's DuckDB-backed event store and locates it at `Env.FileDataRoot/event_store/main.duckdb`.
- `wintap/core/infrastructure/ProcessResolver.cs:123-179` reads the `process` table to resolve the process owning an event at a given time. `wintap/core/infrastructure/EventChannel.cs:260-367` uses those lookups to enrich live events before their Esper send. This makes the database a **runtime resolution cache**, not the durable telemetry source.
- `wintap/core/infrastructure/ProcessResolver.cs:601-640` uses the cache for PID-hash resolution and live-process fallback; this also supports runtime enrichment rather than durable export.
- `wintap/core/infrastructure/ProcessResolver.cs:794-846` defines `GetAllProcesses`. Repository-wide C# search found no active callers except the no-argument wrapper at `wintap/core/infrastructure/EventChannel.cs:410-413`.
- Repository-wide C# search found no active callers of no-argument `EventChannel.GetProcessHistory()`; the active two-argument overload at `wintap/core/infrastructure/EventChannel.cs:415-418` is the runtime point-in-time resolver used for attribution.
- `shared/ai/wintap_mcp_server/DuckDBManager.cs:75-86` creates a separate `Data Source=:memory:` DuckDB connection. `shared/ai/wintap_mcp_server/DuckDBManager.cs:89-112` points its process view at `*+raw_process+*.parquet` and uses `read_parquet`; it does not open `event_store/main.duckdb`.
- The only other C# `main.duckdb` literal found is `platform/windows/WintapCoreSvcMgr/BackupDatabaseManager.cs:28-51`, which refers to the distinct `%ProgramData%/Wintap/ProcessTree/main.duckdb` service-manager path, not `Env.FileDataRoot/event_store/main.duckdb` and not the ETL Parquet source.

**Conclusion:** ProcessResolver's `event_store/main.duckdb` is a runtime process-resolution and enrichment cache. Process Start/Refresh/Stop durability is supplied independently by the live WindowsProcessSensor -> EventChannel -> Esper -> process serializers -> intermediate Parquet -> Merge/RawSensorWriter -> `raw_sensor/raw_process` pipeline. Clearing resolver rows cannot delete process events already serialized to Parquet, so the approved deletion-policy precondition holds.

## Verification Commands

### Required repository-root commands

```powershell
dotnet build -c Release
dotnet test --filter "Category=ptr-01"
```

Both root commands failed because `dotnet` attempted to process the solution's website project:

`dotnet build -c Release` exited 1 with this full merged output:

```text
C:\PUBLIC\Wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.

Build FAILED.

C:\PUBLIC\Wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.33
```

`dotnet test --filter "Category=ptr-01"` exited 1 with this full merged output:

```text
C:\PUBLIC\Wintap\Wintap.sln : Solution file error MSB4249: Unable to build website project "Wintap-Workbench". The ASP.NET compiler is only available on the .NET Framework version of MSBuild.
```

### Project-scoped fallbacks

```powershell
dotnet build "wintap\Wintap.csproj" -c Release -p:WarningLevel=0
dotnet test "tests\Wintap.Tests\Wintap.Tests.csproj" --filter "Category=ptr-01" --logger "console;verbosity=detailed"
```

Build result:

```text
  Determining projects to restore...
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  All projects are up-to-date for restore.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  WintapAPI -> C:\PUBLIC\Wintap\shared\WintapAPI\bin\Release\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\Wintap\wintap\bin\Release\net8.0\Wintap.dll

Build succeeded.

C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
    16 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.53
```

The 16 warnings were existing package-resolution/compatibility advisories: `NU1603` for TaskScheduler 2.11.1 resolving to 2.12.0, `NU1903` for the Microsoft.OpenApi 2.3.1 vulnerability advisory, and `NU1701` compatibility warnings for the legacy ASP.NET/Owin packages targeting .NET Framework rather than `net8.0`.

Test result:

```text
  Determining projects to restore...
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead. [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project. [C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj]
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  All projects are up-to-date for restore.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\tests\Wintap.Tests\Wintap.Tests.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1603: Wintap depends on TaskScheduler (>= 2.11.1) but TaskScheduler 2.11.1 was not found. TaskScheduler 2.12.0 was resolved instead.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.1 has a known high severity vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Core 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.AspNet.WebApi.Owin 5.3.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Host.HttpListener 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Microsoft.Owin.Hosting 4.2.2' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
C:\PUBLIC\Wintap\wintap\Wintap.csproj : warning NU1701: Package 'Owin 1.0.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0'. This package may not be fully compatible with your project.
  WintapAPI -> C:\PUBLIC\Wintap\shared\WintapAPI\bin\Debug\net8.0\WintapAPI.dll
  Wintap -> C:\PUBLIC\Wintap\wintap\bin\Debug\net8.0\Wintap.dll
  Wintap.Tests -> C:\PUBLIC\Wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
Test run for C:\PUBLIC\Wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.14.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
C:\PUBLIC\Wintap\tests\Wintap.Tests\bin\Debug\net8.0\Wintap.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.30)
[xUnit.net 00:00:00.13]   Discovering: Wintap.Tests
[xUnit.net 00:00:00.22]   Discovered:  Wintap.Tests
[xUnit.net 00:00:00.23]   Starting:    Wintap.Tests
[xUnit.net 00:00:00.83]   Finished:    Wintap.Tests
  Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseForMissingEmptyOrNullHash(pidHash: "") [118 ms]
  Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseForMissingEmptyOrNullHash(pidHash: "missing-process") [56 ms]
  Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseForMissingEmptyOrNullHash(pidHash: null) [48 ms]
  Passed Wintap.Tests.SystemIdentityProbeTests.EnsureEventStoreTables_CreatesRequiredTablesAndIsIdempotent [80 ms]
  Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsFalseWithoutThrowingForQuoteContainingHash [46 ms]
  Passed Wintap.Tests.SystemIdentityProbeTests.TryGetWindowsProcStartFileTimeUtc_MatchesCurrentProcessStartTime [52 ms]
  Passed Wintap.Tests.SystemIdentityProbeTests.TryGetWindowsProcStartFileTimeUtc_ReturnsNullForNegativePid [47 ms]
  Passed Wintap.Tests.SystemIdentityProbeTests.IsProcessRowOpen_ReturnsTrueForUpsertedOpenRowAndFalseAfterExitUpdate [86 ms]

Test Run Successful.
Total tests: 8
     Passed: 8
 Total time: 1.9513 Seconds
```

## Behavioral And Schema Notes

- No `process` table schema was changed, and no new table was added. `EnsureEventStoreTables` still creates only `process` and `process_retention_telemetry`.
- No `WintapMessage` or `ProcessObject` schema, PidHash formula, package dependency, Linux/macOS implementation, or startup behavior was changed.
- No production code path calls `TryGetSystemStartFileTimeUtc` yet. The new `EventChannel.IsProcessRowOpen` passthrough likewise introduces no startup gate or other startup behavior in this unit.

## Deviations From Instruction

- The repository-root build and test commands failed with the known `MSB4249` website-project incompatibility, so the approved project-scoped fallbacks were used.
- The detailed console logger was appended to the test fallback solely to capture all eight test names and statuses in this audit.

## Unrelated Worktree State

- The pre-existing untracked `diagnostics/sid-extraction-test/` directory was not touched and is excluded from this unit's scope.
