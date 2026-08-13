# SimpleMcpPlugin

This sample plugin demonstrates how a Wintap plugin can implement `IRun` and `IProvideMCP` to expose plugin-specific MCP tools.

## What This Sample Shows

- constructor injection of `IWintapLogger` and `IInfer`
- plugin startup and periodic execution through `IRun`
- MCP server discovery through `GetMcpServerPath()`
- simple AI-assisted tool validation using `AskAsync(..., useTools: true)`

## Actual Sample Layout

This sample directory contains the plugin project:

```text
shared/samples/SimpleMcpPlugin/
  SimpleMcpPlugin.cs
  SimpleMcpPlugin.csproj
  README.md
```

The plugin project references a separate MCP server project at:

```text
shared/samples/SimplePluginMcpServer/SimpleMcpPluginServer.csproj
```

## Build

From the repo root:

```bash
dotnet build shared/samples/SimplePluginMcpServer/SimpleMcpPluginServer.csproj
dotnet build shared/samples/SimpleMcpPlugin/SimpleMcpPlugin.csproj
```

## Runtime Expectation

`SimpleMcpPlugin.GetMcpServerPath()` expects the MCP server executable to be located beside the plugin assembly under the plugin deployment directory.

The current implementation looks for:

```text
SimpleMcpPluginServer.exe
```

in the same directory as the plugin DLL.

## Notes About Tool Naming

This sample currently contains mixed assumptions about tool naming:

- some code paths check for un-namespaced tools like `SayHello`
- other code paths expect prefixed names like `SimpleMcpPlugin_*`

Treat this sample as a development example, not a stable contract document.

If you use it as a starting point, verify the current tool-registration behavior in the core runtime and update the sample accordingly.

## Related Code

- plugin sample: `shared/samples/SimpleMcpPlugin/SimpleMcpPlugin.cs`
- MCP integration in core: `wintap/core/infrastructure/PluginManager.cs`
- MCP-related interfaces: `shared/WintapAPI/Interfaces.cs`
