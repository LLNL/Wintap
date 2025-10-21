/*
 * SimpleMcpTools - MCP tools provided by SimplePlugin
 * These tools will be exposed to the AI through the plugin's MCP server.
 */

using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Threading;

namespace gov.llnl.wintap.plugins.mcp.samples
{
    /// <summary>
    /// MCP tools provided by the Simple plugin.
    /// Each method decorated with [McpServerTool] becomes an AI-callable tool.
    /// </summary>
    [McpServerToolType]
    internal class SimpleMcpTools
    {
        internal SimpleMcpTools()
        {
        }

        /// <summary>
        /// Simple greeting tool that demonstrates basic MCP functionality.
        /// </summary>
        [McpServerTool(Name = "SayHello")]
        [Description("Greets the user with a friendly hello message. Can optionally personalize with a name.")]
        public static string SayHello(string name = "friend", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "friend";
            }

            string greeting = $"Hello, {name}! This greeting comes from the Simple plugin's custom MCP tool. " +
                            $"The current time is {DateTime.Now:yyyy-MM-dd HH:mm:ss}.";

            return greeting;
        }

        /// <summary>
        /// Returns information about the plugin.
        /// </summary>
        [McpServerTool(Name = "GetPluginInfo")]
        [Description("Returns detailed information about the Simple plugin, including its version and capabilities.")]
        public static string GetPluginInfo(CancellationToken cancellationToken = default)
        {
            return @"{
  ""pluginName"": ""SimplePlugin"",
  ""version"": ""1.0.0"",
  ""description"": ""A demonstration plugin showing how to provide custom MCP tools"",
  ""capabilities"": [
    ""Custom MCP tool integration"",
    ""Dependency injection (IWintapLogger, IInfer)"",
    ""Periodic AI-powered tasks""
  ],
  ""tools"": [
    ""SayHello - Greets users with personalized messages"",
    ""GetPluginInfo - Provides plugin metadata""
  ],
  ""author"": ""Wintap Development Team"",
  ""mcpServerType"": "".NET MCP Server""
}";
        }
    }
}