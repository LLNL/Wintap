/*
 * SimpleMcpPlugin - Simple test plugin demonstrating IProvideMCP interface
 * This plugin provides its own MCP server with custom tools.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using static gov.llnl.wintap.Interfaces;

namespace gov.llnl.wintap.plugins.samples
{
    /// <summary>
    /// Simple test plugin that demonstrates plugin-specific MCP server integration.
    /// Provides custom MCP tools: SayHello, GetPluginInfo, SquareNumber, GetStatus
    /// </summary>
    [Export(typeof(IRun))]
    [ExportMetadata("Name", "SimpleMcpPlugin")]
    [ExportMetadata("Description", "A simple plugin that demonstrates custom MCP tools")]
    public class SimpleMcpPlugin : IRun, IProvideMCP
    {
        private readonly IWintapLogger _logger;
        private readonly IInfer _ai;
        private System.Threading.Timer _timer;

        /// <summary>
        /// Plugin constructor with dependency injection.
        /// Receives logger and AI inference services from Wintap core.
        /// </summary>
        [ImportingConstructor]
        public SimpleMcpPlugin(IWintapLogger logger, IInfer ai)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // IRun INTERFACE IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════════════════════

        public string Name => "SimpleMcpPlugin";

        public RunManifest RunStartup()
        {
            _logger.Append("SimpleMcpPlugin starting up", LogLevel.Info);
            _logger.Append($"Plugin has access to AI inference: {_ai != null}", LogLevel.Info);

            _logger.Append("SimpleMcpPlugin startup complete", LogLevel.Info);

            RunManifest manifest = new RunManifest()
            {
                Interval = TimeSpan.FromMinutes(5),
                MaxRuntime = TimeSpan.FromMinutes(2),
                RequiredHost = "NONE"
            };
            return manifest;
        }

        public async void Run()
        {
            try
            {
                _logger.Append($"Attempting tool call", LogLevel.Error);
                await TestPluginToolsAsync();
                _logger.Append($"Done with tool call", LogLevel.Error);
            }
            catch (Exception ex)
            {
                _logger.Append($"Error in Run(): {ex.Message}", LogLevel.Error);
                _logger.Append($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
            }
        }

        public void RunShutdown()
        {
            _logger.Append("SimpleMcpPlugin shutting down", LogLevel.Info);

            _timer?.Dispose();
            _timer = null;

            _logger.Append("SimpleMcpPlugin shutdown complete", LogLevel.Info);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // IProvideMCP INTERFACE IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the path to this plugin's MCP server executable.
        /// The MCP server should be deployed in the same directory as the plugin DLL.
        /// </summary>
        public string GetMcpServerPath()
        {
            // Get the directory where this plugin DLL is located
            string pluginDir = Path.GetDirectoryName(typeof(SimpleMcpPlugin).Assembly.Location);

            // MCP server executable should be in the same directory
            string mcpServerPath = Path.Combine(pluginDir, "SimpleMcpPluginServer.exe");

            _logger.Append($"Plugin MCP server path: {mcpServerPath}", LogLevel.Debug);

            if (!File.Exists(mcpServerPath))
            {
                _logger.Append($"WARNING: MCP server not found at {mcpServerPath}", LogLevel.Warn);
            }

            return mcpServerPath;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // PLUGIN LOGIC - DEMONSTRATING AI TOOL USAGE
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initial test that verifies plugin tools are available.
        /// Runs once on first Run() call after MCP server has initialized.
        /// </summary>
        private async System.Threading.Tasks.Task TestPluginToolsAsync()
        {
            try
            {
                _logger.Append("═══════════════════════════════════════════════════════", LogLevel.Info);
                _logger.Append("=== STARTUP TEST: Verifying Plugin MCP Tools ===", LogLevel.Info);
                _logger.Append("═══════════════════════════════════════════════════════", LogLevel.Info);

                var tools = await _ai.GetAvailableToolsAsync();
                foreach (var tool in tools)
                {
                    _logger.Append($"Un-namespaced tool:  - {tool}", LogLevel.Info);
                }

                // ALPHA: Tools are not namespaced, so just check for our specific tool names
                var ourTools = tools.Where(t => t == "SayHello" || t == "GetPluginInfo").ToList();

                if (ourTools.Any())
                {
                    _logger.Append($"✓ SUCCESS: {ourTools.Count} plugin tools registered!", LogLevel.Always);
                    foreach (var tool in ourTools)
                    {
                        _logger.Append($"  - {tool}", LogLevel.Always);
                    }
                }
                else
                {
                    _logger.Append("✗ FAILED: No plugin tools found. Check MCP server status.", LogLevel.Error);
                    _logger.Append($"Total tools available: {tools.Count}", LogLevel.Error);
                    _logger.Append($"Tools: {string.Join(", ", tools)}", LogLevel.Info);
                    return;
                }

                // Test 1: Natural language prompt (AI decides which tools to use)
                _logger.Append("", LogLevel.Always);
                _logger.Append("─────────────────────────────────────────────────────", LogLevel.Always);
                _logger.Append("--- Test 1: Natural Language Tool Invocation ---", LogLevel.Always);
                _logger.Append("─────────────────────────────────────────────────────", LogLevel.Always);
                string prompt1 = "Please greet me using the SayHello tool, addressing me as 'Plugin Developer'.";
                _logger.Append($"Prompt: \"{prompt1}\"", LogLevel.Always);
                _logger.Append("Sending to AI with tool calling enabled...", LogLevel.Always);

                string response1 = await _ai.AskAsync(prompt1, useTools: true);
                _logger.Append($"✓ AI Response: {response1}", LogLevel.Always);
                _logger.Append("(Behind the scenes: AI called SimpleMcpPlugin_SayHello tool)", LogLevel.Debug);

                // Test 2: Plugin info retrieval
                _logger.Append("", LogLevel.Always);
                _logger.Append("─────────────────────────────────────────────────────", LogLevel.Always);
                _logger.Append("--- Test 2: Plugin Information Retrieval ---", LogLevel.Always);
                _logger.Append("─────────────────────────────────────────────────────", LogLevel.Always);
                string prompt2 = "Use the GetPluginInfo tool to tell me about this plugin.";
                _logger.Append($"Prompt: \"{prompt2}\"", LogLevel.Always);
                _logger.Append("Sending to AI...", LogLevel.Always);

                string response2 = await _ai.AskAsync(prompt2, useTools: true);
                _logger.Append($"✓ AI Response: {response2}", LogLevel.Always);

                // Test 3: Multiple tool usage in one request
                _logger.Append("", LogLevel.Always);
                _logger.Append("─────────────────────────────────────────────────────", LogLevel.Always);
                _logger.Append("--- Test 3: Multiple Tool Invocation ---", LogLevel.Always);
                _logger.Append("─────────────────────────────────────────────────────", LogLevel.Always);
                string prompt3 = "First greet me with SayHello, then show plugin info with GetPluginInfo, then calculate the square of 7 using SquareNumber.";
                _logger.Append($"Prompt: \"{prompt3}\"", LogLevel.Always);
                _logger.Append("Sending to AI (should call 3 different tools)...", LogLevel.Always);

                string response3 = await _ai.AskAsync(prompt3, useTools: true);
                _logger.Append($"✓ AI Response: {response3}", LogLevel.Always);
                _logger.Append("(AI should have called 3 tools: SayHello, GetPluginInfo, SquareNumber)", LogLevel.Debug);

                _logger.Append("", LogLevel.Always);
                _logger.Append("═══════════════════════════════════════════════════════", LogLevel.Always);
                _logger.Append("=== STARTUP TEST COMPLETE ===", LogLevel.Always);
                _logger.Append("═══════════════════════════════════════════════════════", LogLevel.Always);
            }
            catch (Exception ex)
            {
                _logger.Append($"✗ Error in startup test: {ex.Message}", LogLevel.Error);
                _logger.Append($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Periodic task that demonstrates using AI with plugin-specific MCP tools.
        /// The AI will automatically call the plugin's MCP tools when given natural language instructions.
        /// </summary>
        private async System.Threading.Tasks.Task PeriodicTaskAsync()
        {
            try
            {
                _logger.Append("=== Running periodic AI test with plugin MCP tools ===", LogLevel.Info);

                // Get available tools (should include SimpleMcpPlugin_* tools)
                var tools = await _ai.GetAvailableToolsAsync();
                _logger.Append($"Available MCP tools ({tools.Count} total)", LogLevel.Info);

                // Verify our plugin's tools are registered
                var ourTools = tools.Where(t => t.StartsWith("SimpleMcpPlugin_")).ToList();
                if (ourTools.Any())
                {
                    _logger.Append($"✓ Plugin tools registered: {string.Join(", ", ourTools)}", LogLevel.Info);
                }
                else
                {
                    _logger.Append("✗ WARNING: No SimpleMcpPlugin tools found! MCP server may not be running.", LogLevel.Warn);
                    return;
                }

                // Natural language prompt that will trigger tool calls
                // The AI will interpret this and call SimpleMcpPlugin_SayHello and SimpleMcpPlugin_GetStatus
                string prompt = "Use the SayHello tool to greet me as 'Developer', then use GetStatus to show me the current status.";

                _logger.Append($"Sending prompt to AI: \"{prompt}\"", LogLevel.Info);
                _logger.Append("NOTE: The AI will now call our plugin's MCP tools automatically...", LogLevel.Debug);

                // Make the AI request - tool calling happens inside AskAsync()
                string response = await _ai.AskAsync(prompt, useTools: true);

                _logger.Append("=== AI completed successfully ===", LogLevel.Info);
                _logger.Append($"Final AI Response: {response}", LogLevel.Info);
                _logger.Append("(The AI called our MCP tools behind the scenes to generate this response)", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logger.Append($"Error in periodic task: {ex.Message}", LogLevel.Error);
                _logger.Append($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
            }
        }
    }
}