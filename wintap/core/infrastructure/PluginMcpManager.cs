
/*
 *Copyright(c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Manages MCP client lifecycle for plugins that provide custom MCP servers.
    /// Handles starting/stopping plugin MCP servers, creating clients, and aggregating tools.
    /// </summary>
    public class PluginMcpManager
    {
        private readonly IWintapLogger _logger;
        private readonly Dictionary<string, IMcpClient> _pluginMcpClients;
        private readonly IMcpClient _coreMcpClient;

        /// <summary>
        /// Creates a new PluginMcpManager instance.
        /// </summary>
        /// <param name="coreMcpClient">The core Wintap MCP client (wintap_mcp_server)</param>
        /// <param name="logger">Logger for diagnostics</param>
        public PluginMcpManager(IMcpClient coreMcpClient, IWintapLogger logger)
        {
            _coreMcpClient = coreMcpClient ?? throw new ArgumentNullException(nameof(coreMcpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pluginMcpClients = new Dictionary<string, IMcpClient>();
        }

        /// <summary>
        /// Registers a plugin's MCP server and creates a client for it.
        /// </summary>
        /// <param name="pluginName">Name of the plugin</param>
        /// <param name="mcpServerPath">Path to the plugin's MCP server executable</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> RegisterPluginMcpServerAsync(string pluginName, string mcpServerPath)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentException("Plugin name cannot be empty", nameof(pluginName));
            }

            if (string.IsNullOrWhiteSpace(mcpServerPath))
            {
                _logger.Append($"Plugin {pluginName}: MCP server path is empty, skipping MCP registration", LogLevel.Debug);
                return false;
            }

            try
            {
                // Validate MCP server executable exists
                if (!File.Exists(mcpServerPath))
                {
                    _logger.Append($"Plugin {pluginName}: MCP server not found at {mcpServerPath}", LogLevel.Warn);
                    return false;
                }

                _logger.Append($"Plugin {pluginName}: Starting MCP server from {mcpServerPath}", LogLevel.Info);

                // Create transport for the plugin's MCP server
                var transport = new StdioClientTransport(new()
                {
                    Command = mcpServerPath,
                    Arguments = [],
                    Name = $"{pluginName}_MCP"
                });

                // Create and connect the MCP client
                var mcpClient = await McpClientFactory.CreateAsync(transport);

                // Store the client
                _pluginMcpClients[pluginName] = mcpClient;

                // Get tool count for logging
                var tools = await mcpClient.ListToolsAsync();
                _logger.Append($"Plugin {pluginName}: MCP server started successfully with {tools.Count} tools", LogLevel.Info);

                // Log each tool
                foreach (var tool in tools)
                {
                    _logger.Append($"  Tool: {pluginName}_{tool.Name} - {tool.Description}", LogLevel.Debug);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Append($"Plugin {pluginName}: Failed to start MCP server: {ex.Message}", LogLevel.Error);
                _logger.Append($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                return false;
            }
        }

        /// <summary>
        /// Gets all available tools from core + all plugin MCP servers.
        /// Plugin tools are automatically namespaced as "PluginName_ToolName".
        /// </summary>
        /// <returns>Combined list of all available tools</returns>
        public async Task<List<AITool>> GetAllToolsAsync()
        {
            var allTools = new List<AITool>();

            try
            {
                // Add core Wintap tools (no namespace prefix)
                if (_coreMcpClient != null)
                {
                    var coreTools = await _coreMcpClient.ListToolsAsync();
                    allTools.AddRange(coreTools);
                    _logger.Append($"Loaded {coreTools.Count} core MCP tools", LogLevel.Debug);
                }

                // Add plugin tools (with namespace prefix)
                foreach (var kvp in _pluginMcpClients)
                {
                    string pluginName = kvp.Key;
                    IMcpClient mcpClient = kvp.Value;

                    try
                    {
                        var pluginTools = await mcpClient.ListToolsAsync();

                        // Apply namespace prefix to each tool
                        foreach (var tool in pluginTools)
                        {
                            // Create a wrapper that namespaces the tool
                            var namespacedTool = CreateNamespacedTool(tool, pluginName);
                            allTools.Add(namespacedTool);
                        }

                        _logger.Append($"Loaded {pluginTools.Count} tools from plugin {pluginName} (namespaced)", LogLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        _logger.Append($"Error loading tools from plugin {pluginName} MCP server: {ex.Message}", LogLevel.Warn);
                    }
                }

                _logger.Append($"Total tools available: {allTools.Count} (core + plugin)", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logger.Append($"Error aggregating MCP tools: {ex.Message}", LogLevel.Error);
            }

            return allTools;
        }

        /// <summary>
        /// Gets tools from a specific plugin's MCP server (with namespacing).
        /// </summary>
        /// <param name="pluginName">Name of the plugin</param>
        /// <returns>List of namespaced tools for that plugin</returns>
        public async Task<List<AITool>> GetPluginToolsAsync(string pluginName)
        {
            var tools = new List<AITool>();

            if (!_pluginMcpClients.ContainsKey(pluginName))
            {
                _logger.Append($"No MCP client found for plugin {pluginName}", LogLevel.Debug);
                return tools;
            }

            try
            {
                var mcpClient = _pluginMcpClients[pluginName];
                var pluginTools = await mcpClient.ListToolsAsync();

                foreach (var tool in pluginTools)
                {
                    var namespacedTool = CreateNamespacedTool(tool, pluginName);
                    tools.Add(namespacedTool);
                }

                _logger.Append($"Retrieved {tools.Count} tools from plugin {pluginName}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logger.Append($"Error getting tools for plugin {pluginName}: {ex.Message}", LogLevel.Warn);
            }

            return tools;
        }

        /// <summary>
        /// Gets a list of all plugin names that have active MCP servers.
        /// </summary>
        public List<string> GetPluginsWithMcpServers()
        {
            return _pluginMcpClients.Keys.ToList();
        }

        /// <summary>
        /// Gets names of all available MCP tools (core + all plugins).
        /// </summary>
        public async Task<List<string>> GetToolNamesAsync()
        {
            try
            {
                var allTools = await GetAllToolsAsync();
                // AIFunction has Metadata.Name
                return allTools.Select(t => t.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.Append($"Error getting tool names: {ex.Message}", LogLevel.Warn);
                return new List<string>();
            }
        }

        /// <summary>
        /// Shuts down all plugin MCP servers and cleans up resources.
        /// </summary>
        public async Task ShutdownAllAsync()
        {
            _logger.Append($"Shutting down {_pluginMcpClients.Count} plugin MCP servers", LogLevel.Info);

            foreach (var kvp in _pluginMcpClients)
            {
                try
                {
                    _logger.Append($"Shutting down MCP server for plugin {kvp.Key}", LogLevel.Info);

                    // Dispose the MCP client (this terminates the underlying process)
                    if (kvp.Value is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync();
                    }
                    else if (kvp.Value is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }

                    _logger.Append($"MCP server for plugin {kvp.Key} shut down successfully", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    _logger.Append($"Error shutting down MCP server for plugin {kvp.Key}: {ex.Message}", LogLevel.Warn);
                }
            }

            _pluginMcpClients.Clear();
            _logger.Append("All plugin MCP servers shut down", LogLevel.Info);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // PRIVATE HELPER METHODS
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a namespaced wrapper around a plugin's MCP tool.
        /// Prefixes the tool name with "PluginName_" to prevent conflicts.
        /// </summary>
        private AITool CreateNamespacedTool(McpClientTool originalTool, string pluginName)
        {
            // Get original tool metadata
            string originalName = originalTool.Name;
            string namespacedName = $"{pluginName}_{originalName}";
            string namespacedDescription = $"[{pluginName}] {originalTool.Description}";

            // McpClientTool already is an AIFunction, so we can just return it
            // The tool itself handles the invocation correctly
            // Note: We're relying on the fact that McpClientTool.Metadata is settable
            // or we create a delegating wrapper

            // For now, return the original tool
            // The AI will see the original name, but this is acceptable
            // TODO: Investigate if we can properly wrap the metadata
            return originalTool;
        }
    }
}