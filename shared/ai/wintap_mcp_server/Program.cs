using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using gov.llnl.wintap.helpers;
using gov.llnl.wintap.ai.mcp;

string baseDir = OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap")
    : Path.Combine("/var/lib", "wintap");

Logit.Instance.LogDir = Path.Combine(baseDir, "Logs");
Logit.Instance.Init();
Logit.Instance.Append("MCP Server is starting", LogVerboseLevel.Normal);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

DuckDBManager.Initialize();

Logit.Instance.Append("MCP Server is creating services", LogVerboseLevel.Normal);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<mcp_tools>();

Logit.Instance.Append("MCP Server is started", LogVerboseLevel.Normal);

await builder.Build().RunAsync();