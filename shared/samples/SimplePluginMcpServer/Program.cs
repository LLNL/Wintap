/*
 * HelloWorld MCP Server - Console application that hosts MCP tools for HelloWorldPlugin
 * This server runs as a separate process and communicates via stdio.
 */

using gov.llnl.wintap.plugins.mcp.samples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace gov.llnl.wintap.plugins.mcpserver.samples
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup logging directory (optional - MCP uses stderr for logs)
            string baseDir = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap", "Plugins", "HelloWorld")
                : Path.Combine("/var/lib", "wintap", "plugins", "helloworld");

            Directory.CreateDirectory(baseDir);
            string logFile = Path.Combine(baseDir, $"mcp_server_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            // Write startup message to stderr (MCP convention)
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HelloWorld MCP Server starting...");
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log file: {logFile}");

            try
            {
                var builder = Host.CreateApplicationBuilder(args);

                // Configure logging to go to stderr (MCP requirement)
                builder.Logging.ClearProviders();
                builder.Logging.AddConsole(consoleLogOptions =>
                {
                    // All logs must go to stderr in MCP servers
                    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
                });

                // Also log to file for debugging
                //builder.Logging.AddFile(logFile, minimumLevel: LogLevel.Debug);

                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Configuring MCP services...");

                // Register MCP server with stdio transport and our tools
                builder.Services
                    .AddMcpServer()
                    .WithStdioServerTransport()
                    .WithTools<SimpleMcpTools>();

                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HelloWorld MCP Server started successfully");
                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Available tools: SayHello, GetPluginInfo, SquareNumber, GetStatus");

                // Run the MCP server (blocks until terminated)
                await builder.Build().RunAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL ERROR in MCP Server: {ex.Message}");
                Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Stack trace: {ex.StackTrace}");
                Environment.Exit(1);
            }

            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HelloWorld MCP Server shutting down");
        }
    }
}