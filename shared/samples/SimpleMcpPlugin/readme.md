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
│   ├── Program.cs               # MCP server console app
│   ├── SimpleMcpTools.cs        # MCP tool definitions
│   └── SimpleMcpPluginServer.csproj
└── README.md                    # This file
```

## MCP Tools Provided

The plugin's MCP server exposes these tools to the AI:

1. **SayHello** - Greets users with personalized messages
2. **GetPluginInfo** - Returns plugin metadata as JSON

> **ALPHA NOTE**: Tools are **not namespaced** in the current implementation. Tool names appear as-is without plugin prefixes (e.g., `SayHello` not `SimpleMcpPlugin_SayHello`). Developers should coordinate to avoid tool name conflicts between plugins. Namespacing will be added in a future release when needed.

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
   cd SimpleMcpPluginServer
   dotnet build
   ```

3. **Both projects include post-build events** that automatically copy outputs to:
   ```
   Wintap/bin/Debug/net8.0/Plugins/SimpleMcpPlugin/
   ```

### Manual Build (if post-build fails)

```bash
# Build plugin
dotnet build SimpleMcpPlugin/SimpleMcpPlugin.csproj -c Debug

# Build MCP server
dotnet build SimpleMcpPluginServer/SimpleMcpPluginServer.csproj -c Debug

# Copy to Wintap plugins directory
mkdir -p <wintap-dir>/bin/Debug/net8.0/Plugins/SimpleMcpPlugin
cp SimpleMcpPlugin/bin/Debug/net8.0/SimpleMcpPlugin.dll <wintap-dir>/bin/Debug/net8.0/Plugins/SimpleMcpPlugin/
cp SimpleMcpPlugin/bin/Debug/net8.0/SimpleMcpPlugin.pdb <wintap-dir>/bin/Debug/net8.0/Plugins/SimpleMcpPlugin/
cp -r SimpleMcpPluginServer/bin/Debug/net8.0/* <wintap-dir>/bin/Debug/net8.0/Plugins/SimpleMcpPlugin/
```

## Deployment

### Directory Structure (after build)
```
Wintap/bin/Debug/net8.0/
├── Plugins/
│   └── SimpleMcpPlugin/
│       ├── SimpleMcpPlugin.dll           # Plugin assembly
│       ├── SimpleMcpPlugin.pdb           # Debug symbols
│       ├── SimpleMcpPluginServer.exe     # MCP server executable
│       ├── SimpleMcpPluginServer.dll     # MCP server library
│       ├── ModelContextProtocol.dll      # MCP dependencies
│       └── ... (other MCP server dependencies)
```

> **NOTE**: Wintap uses a directory-based plugin convention. Each plugin must be in its own subdirectory under `Plugins/` with a matching DLL name (e.g., `Plugins/SimpleMcpPlugin/SimpleMcpPlugin.dll`). This prevents third-party dependencies from being loaded as plugins.

### Verification

1. **Check plugin loads:**
   ```
   # Look in Wintap logs for:
   "Found plugin: SimpleMcpPlugin at C:\...\Plugins\SimpleMcpPlugin\SimpleMcpPlugin.dll"
   "Plugin SimpleMcpPlugin implements IProvideMCP"
   "Plugin SimpleMcpPlugin provides MCP server at <path>/SimpleMcpPluginServer.exe"
   "Successfully registered MCP server for plugin SimpleMcpPlugin"
   ```

2. **Check MCP server starts:**
   ```
   # Look in logs for:
   "Plugin SimpleMcpPlugin: Starting MCP server from ..."
   "Plugin SimpleMcpPlugin: MCP server started successfully with 2 tools"
   "  Tool: SimpleMcpPlugin_SayHello - Greets the user..."
   "  Tool: SimpleMcpPlugin_GetPluginInfo - Returns detailed information..."
   ```

3. **Verify MCP server process is running:**
   - Check Task Manager (Windows) or `ps` (Linux) for `SimpleMcpPluginServer.exe`

4. **Verify tools are available:**
   ```csharp
   // From the plugin itself or another plugin:
   var tools = await _ai.GetAvailableToolsAsync();
   // Should include: RunSQL, TellTime, SayHello, GetPluginInfo
   ```

## Testing

### Test 1: Plugin Loads and MCP Server Starts
1. Start Wintap
2. Check logs for: "Loaded 1 plugins with logger and AI inference support"
3. Check logs for: "Registered MCP servers for 1 plugins"
4. Verify `SimpleMcpPluginServer.exe` is running in Task Manager

### Test 2: Tools Are Available
The plugin's `Run()` method includes an automated startup test that runs on first execution:
```
[Always] === STARTUP TEST: Verifying Plugin MCP Tools ===
[Always] ✓ SUCCESS: 2 plugin tools registered!
[Always]   - SayHello
[Always]   - GetPluginInfo
```

Check logs for this output to confirm tools loaded correctly.

### Test 3: AI Can Use Plugin Tools
```csharp
// Example from within the plugin or another plugin:
var ai = serviceProvider.GetService<IInfer>();
var response = await ai.AskAsync("Use the SayHello tool to greet me as 'Developer'");
// AI will call SayHello("Developer") and respond with the greeting
```

### Test 4: Tool Invocation Logging
When the AI calls plugin tools, you'll see detailed logging:
```
[Info] AI called tool: SayHello with args: {"name":"Developer"}
[Info] Tool result from {call_id}: Hello, Developer! This greeting comes from...
```

## Troubleshooting

### Plugin doesn't load
- ❌ Check that `SimpleMcpPlugin.dll` is in `Plugins/SimpleMcpPlugin/` directory
- ❌ Verify directory name matches DLL name (case-sensitive)
- ❌ Check Wintap logs for MEF composition errors
- ❌ Verify WintapAPI reference path is correct in `.csproj`

### Plugin loads but no tools found
- ❌ Check logs for "Registered MCP servers for 0 plugins" (indicates IProvideMCP not detected)
- ❌ Verify plugin implements `IProvideMCP` interface
- ❌ Check that `GetMcpServerPath()` returns correct absolute path
- ❌ Ensure `SimpleMcpPluginServer.exe` exists at the returned path

### MCP server doesn't start
- ❌ Check that `SimpleMcpPluginServer.exe` exists in plugin directory
- ❌ Check all MCP server dependencies (.dll files) are copied
- ❌ Look for error in logs: "Plugin SimpleMcpPlugin: Failed to start MCP server"
- ❌ Verify .NET 8.0 runtime is installed
- ❌ Check file permissions (plugin directory must be readable/executable)

### Tools not available to AI
- ❌ Verify MCP server process is running (Task Manager)
- ❌ Check `PluginMcpManager` logs for: "Loaded X tools from plugin SimpleMcpPlugin"
- ❌ Check total tool count: Should be 4 (2 core + 2 plugin)
- ❌ Ensure no exceptions in MCP server stderr output

### AI can't call plugin tools
- ❌ Verify tools appear in `GetAvailableToolsAsync()` results
- ❌ Check AI model supports function/tool calling (Ollama: llama3.1+)
- ❌ Look for tool invocation logs when AI makes calls
- ❌ Check MCP server logs for tool execution errors

### MCP server crashes
- ❌ Check plugin logs in: `%ProgramData%\Wintap\Plugins\SimpleMcpPlugin\`
- ❌ Look for exceptions in MCP server stderr output
- ❌ Verify ModelContextProtocol package version matches core Wintap
- ❌ Check for missing dependencies

## Architecture Notes

### Plugin Lifecycle
1. **Discovery**: PluginManager scans `Plugins/` subdirectories
2. **Load**: Plugin assembly loaded into isolated AppDomain via MEF
3. **DI Injection**: `IWintapLogger` and `IInfer` injected via constructor
4. **MCP Detection**: Reflection checks if plugin implements `IProvideMCP`
5. **MCP Registration**: If detected, calls `GetMcpServerPath()` and starts MCP server
6. **Startup**: Calls `RunStartup()` to initialize plugin
7. **Run**: Periodic calls to `Run()` method based on `RunManifest` schedule
8. **Shutdown**: Calls `RunShutdown()`, then stops MCP server, then unloads AppDomain

### Reflection-Based IProvideMCP Detection
Due to isolated AppDomains, interface detection uses reflection:
```csharp
var provideMcpInterface = pluginType.GetInterface("IProvideMCP");
if (provideMcpInterface != null)
{
    var getMcpServerPathMethod = pluginType.GetMethod("GetMcpServerPath");
    var mcpServerPath = getMcpServerPathMethod.Invoke(pluginValue, null) as string;
    // Register MCP server...
}
```

### Tool Availability (ALPHA)
Current tool naming:
- Core tools: `TellTime`, `RunSQL` 
- Plugin tools: `SayHello`, `GetPluginInfo` (no prefix in alpha)

When namespacing is implemented, plugin tools will become:
- `SimpleMcpPlugin_SayHello`
- `SimpleMcpPlugin_GetPluginInfo`

### Dependency Injection
The plugin receives via constructor:
- `IWintapLogger`: Plugin-specific logging to isolated log files
- `IInfer`: AI inference with access to all tools (core + plugin)

Both services are registered in Wintap's DI container and injected via MEF.

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

2. Rebuild MCP server: `dotnet build SimpleMcpPluginServer`
3. Restart Wintap
4. Tool appears as `MyNewTool` (will be `SimpleMcpPlugin_MyNewTool` when namespacing is added)

### Add Tool Parameters
```csharp
[McpServerTool(Name = "CalculateSum")]
[Description("Adds two numbers together")]
public static string CalculateSum(int a, int b, CancellationToken ct)
{
    return $"The sum of {a} and {b} is {a + b}";
}
```

### Return Structured Data
```csharp
[McpServerTool(Name = "GetStatus")]
[Description("Returns plugin status as JSON")]
public static string GetStatus(CancellationToken ct)
{
    return JsonSerializer.Serialize(new {
        status = "running",
        uptime = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMinutes,
        toolCount = 2
    });
}
```

### Access Wintap Resources from Tools
To access Wintap telemetry or services:
1. Change tools from `static` to instance methods
2. Add constructor to `SimpleMcpTools` accepting dependencies
3. Register as singleton in MCP server DI container
4. Access services through instance fields

## Best Practices

### Tool Naming (ALPHA)
Until namespacing is implemented:
- Use descriptive, unique tool names unlikely to conflict
- Avoid generic names like `GetData`, `Process`, `Execute`
- Prefix with plugin name if desired: `SMPSayHello`

### Error Handling
Always handle exceptions in MCP tools:
```csharp
public static string MyTool(string input, CancellationToken ct)
{
    try {
        // Tool logic
        return result;
    } catch (Exception ex) {
        return $"Error: {ex.Message}";
    }
}
```

### Logging
Use the injected logger for diagnostics:
```csharp
_logger.Append("Processing request", LogLevel.Info);
_logger.Append($"Error: {ex.Message}", LogLevel.Error);
```

### Performance
- Keep tool execution fast (< 1 second if possible)
- Use `CancellationToken` to respect cancellation
- Avoid blocking operations

## Known Issues / Future Enhancements

- [ ] **Tool namespacing**: Not yet implemented (manual coordination required)
- [ ] **Tool metadata**: Limited parameter type descriptions
- [ ] **MCP server restart**: No automatic recovery if server crashes
- [ ] **Tool discovery**: No runtime addition/removal of tools
- [ ] **Metrics**: No built-in tool call statistics

## License

Same as Wintap core (LLNL)

## Support

For issues with this plugin:
1. Check Wintap logs: `%ProgramData%\Wintap\Logs\wintap.log`
2. Check plugin logs: `%ProgramData%\Wintap\Plugins\SimpleMcpPlugin\*.log`
3. Enable debug logging: Set `LogLevel.Debug` in plugin constructor
4. Verify MCP server is running in Task Manager
5. Check MCP server stderr output in logs

For questions about plugin development, see Wintap plugin documentation.