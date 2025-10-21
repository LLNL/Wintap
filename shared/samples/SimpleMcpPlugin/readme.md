# SimpleMcpPlugin - MCP Integration Test Plugin

A simple demonstration plugin showing how to provide custom MCP tools to Wintap's AI inference system.

## Overview

This plugin demonstrates:
- ✅ Implementing `IProvideMCP` interface
- ✅ Creating a .NET-based MCP server with custom tools
- ✅ Receiving `IWintapLogger` and `IInfer` via dependency injection
- ✅ Using plugin-specific MCP tools from within the plugin
- ✅ Proper lifecycle management (startup/shutdown)

## Project Structure

```
SimpleMcpPlugin/
├── SimpleMcpPlugin.cs           # Main plugin class (implements IRun, IProvideMCP)
├── SimpleMcpPlugin.csproj       # Plugin project file
SimpleMcpPluginServer/
│   ├── Program.cs                # MCP server console app
│   ├── SimpleMcpTools.cs     # MCP tool definitions
│   └── SimpleMcpServer.csproj
└── README.md                     # This file
```

## MCP Tools Provided

The plugin's MCP server exposes these tools to the AI:

1. **SayHello** - Greets users with personalized messages
2. **GetPluginInfo** - Returns plugin metadata as JSON

When loaded, these tools are namespaced as:
- `SimpleMcpPlugin_SayHello`
- `SimpleMcpPlugin_GetPluginInfo`

## Building

### Prerequisites
- .NET 8.0 SDK
- Wintap solution properly configured
- WintapAPI project built

### Build Steps

1. **Build the plugin DLL:**
   ```bash
   cd SimpleMcpPlugin
   dotnet build
   ```

2. **Build the MCP server:**
   ```bash
   cd SimpleMcpServer
   dotnet build
   ```

3. **Both projects include post-build events** that automatically copy outputs to:
   ```
   Wintap/bin/Debug/net8.0/plugins/
   ```

### Manual Build (if post-build fails)

```bash
# Build plugin
dotnet build SimpleMcpPlugin/SimpleMcpPlugin.csproj -c Debug

# Build MCP server
dotnet build SimpleMcpServer/SimpleMcpServer.csproj -c Debug

# Copy to Wintap plugins directory
cp SimpleMcpPlugin/bin/Debug/net8.0/SimpleMcpPlugin.dll <wintap-dir>/bin/Debug/net8.0/plugins/
cp -r SimpleMcpServer/bin/Debug/net8.0/* <wintap-dir>/bin/Debug/net8.0/plugins/
```

## Deployment

### Directory Structure (after build)
```
Wintap/bin/Debug/net8.0/
├── plugins/
│   ├── SimpleMcpPlugin.dll              # Plugin assembly
│   ├── SimpleMcpPlugin.pdb              # Debug symbols
│   ├── SimpleMcpServer.exe           # MCP server executable
│   ├── SimpleMcpServer.dll           # MCP server library
│   ├── ModelContextProtocol.dll          # MCP dependencies
│   └── ... (other MCP server dependencies)
```

### Verification

1. **Check plugin loads:**
   ```
   # Look in Wintap logs for:
   "Plugin SimpleMcpPlugin provides MCP server at <path>/SimpleMcpServer.exe"
   "MCP server for plugin SimpleMcpPlugin registered"
   ```

2. **Check MCP server starts:**
   ```
   # Look in plugin logs for:
   "Simple MCP Server starting..."
   "Simple MCP Server started successfully"
   ```

3. **Verify tools are available:**
   ```csharp
   // From another plugin or Wintap code:
   var tools = await inferService.GetAvailableToolsAsync();
   // Should include: SimpleMcpPlugin_SayHello, etc.
   ```

## Testing

### Test 1: Plugin Loads and MCP Server Starts
1. Start Wintap
2. Check logs for plugin registration messages
3. Verify MCP server process is running (check Task Manager / ps)

### Test 2: AI Can Use Plugin Tools
```csharp
// Example test from another plugin or workbench:
var ai = serviceProvider.GetService<IInfer>();
var response = await ai.AskAsync("Use the SayHello tool to greet me as 'Developer'");
// Should call SimpleMcpPlugin_SayHello("Developer")
```

### Test 3: Tool Listing
```csharp
var ai = serviceProvider.GetService<IInfer>();
var tools = await ai.GetAvailableToolsAsync();
Console.WriteLine(string.Join(", ", tools));
// Should include SimpleMcpPlugin_* tools
```

## Troubleshooting

### Plugin doesn't load
- ❌ Check that `SimpleMcpPlugin.dll` is in the plugins directory
- ❌ Check Wintap logs for MEF composition errors
- ❌ Verify WintapAPI reference path is correct

### MCP server doesn't start
- ❌ Check that `SimpleMcpServer.exe` exists in plugins directory
- ❌ Check all MCP server dependencies are copied
- ❌ Look for stderr output in plugin logs
- ❌ Verify .NET 8.0 runtime is installed

### Tools not available to AI
- ❌ Check that plugin implements `IProvideMCP`
- ❌ Verify `GetMcpServerPath()` returns correct path
- ❌ Check `PluginMcpManager` logs for registration errors
- ❌ Ensure MCP server process is actually running

### MCP server crashes
- ❌ Check plugin logs in: `%ProgramData%\Wintap\Plugins\Simple\`
- ❌ Look for exceptions in stderr output
- ❌ Verify ModelContextProtocol package version matches core

## Architecture Notes

### Plugin Lifecycle
1. **Load**: PluginManager discovers plugin via MEF
2. **Detect MCP**: Checks if plugin implements `IProvideMCP`
3. **Register**: Calls `GetMcpServerPath()` and starts MCP server
4. **Inject Services**: Passes `IWintapLogger` and `IInfer` to constructor
5. **Startup**: Calls `RunStartup()`
6. **Run**: Periodic calls to `Run()` method
7. **Shutdown**: Calls `RunShutdown()`, stops MCP server

### Tool Namespacing
- Core tools: `TellTime`, `RunSQL` (no prefix)
- Plugin tools: `SimpleMcpPlugin_SayHello` (prefixed)
- Prevents conflicts between plugins with same tool names

### Dependency Injection
The plugin receives:
- `IWintapLogger`: For writing to plugin-specific log files
- `IInfer`: For making AI requests with access to all MCP tools

## Extending This Plugin

### Add a New Tool
1. Add method to `SimpleMcpTools.cs`:
   ```csharp
   [McpServerTool(Name = "MyNewTool")]
   [Description("Does something cool")]
   public static string MyNewTool(string param, CancellationToken ct)
   {
       return "result";
   }
   ```

2. Rebuild MCP server
3. Restart Wintap
4. Tool appears as `SimpleMcpPlugin_MyNewTool`

### Access Wintap Resources
To access Wintap telemetry or other services from tools:
1. Pass services through DI to tools class
2. Make tools instance methods instead of static
3. Register tools class in MCP server DI container

## License

Same as Wintap core (LLNL)

## Support

For issues with this plugin:
1. Check Wintap logs in `%ProgramData%\Wintap\Logs\`
2. Check plugin-specific logs in `%ProgramData%\Wintap\Plugins\Simple\`
3. Enable verbose logging: Set log level to Debug in plugin constructor