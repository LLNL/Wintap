/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static gov.llnl.wintap.Interfaces;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Implementation of IInfer that wraps the AI chat client and MCP tools.
    /// Provides a simplified interface for plugins to access AI inference capabilities.
    /// </summary>
    public class WintapInference : IInfer
    {
        private readonly IChatClient _chatClient;
        private readonly IMcpClient _mcpClient;
        private readonly IWintapLogger _logger;
        private readonly List<ChatMessage> _pluginHistory;

        /// <summary>
        /// Creates a new WintapInference instance.
        /// </summary>
        /// <param name="chatClient">The AI chat client</param>
        /// <param name="mcpClient">The MCP tools client</param>
        /// <param name="logger">Logger for diagnostics</param>
        public WintapInference(IChatClient chatClient, IMcpClient mcpClient, IWintapLogger logger)
        {
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
            _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pluginHistory = new List<ChatMessage>();
        }

        /// <summary>
        /// Simple inference without conversation history.
        /// </summary>
        public async Task<string> AskAsync(string prompt)
        {
            return await AskAsync(prompt, 1.0f, useTools: true, includeHistory: false);
        }

        /// <summary>
        /// Full-featured inference with options.
        /// </summary>
        public async Task<string> AskAsync(string prompt, float temperature = 1.0f, bool useTools = true, bool includeHistory = false)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
            }

            try
            {
                _logger.Append($"Processing inference request: {prompt.Substring(0, Math.Min(50, prompt.Length))}...",
                    LogLevel.Debug);

                // Build chat history for this request
                var chatHistory = new List<ChatMessage>();

                if (includeHistory && _pluginHistory.Any())
                {
                    chatHistory.AddRange(_pluginHistory);
                }

                chatHistory.Add(new ChatMessage(ChatRole.User, prompt));

                // Configure chat options
                var chatOptions = new ChatOptions
                {
                    Temperature = temperature
                };

                // Add MCP tools if requested
                if (useTools && _mcpClient != null)
                {
                    try
                    {
                        var tools = await _mcpClient.ListToolsAsync();
                        chatOptions.Tools = [.. tools];  // Spread operator for conversion
                        chatOptions.ToolMode = ChatToolMode.Auto;

                        // OpenAI-specific settings (ignored by Ollama)
                        if (chatOptions.AdditionalProperties == null)
                        {
                            chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary()
                            {
                                ["reasoning_effort"] = "minimal"
                            };
                        }

                        chatOptions.AdditionalProperties["disabled_params"] = new Dictionary<string, object>
                        {
                            ["parallel_tool_calls"] = null
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger.Append($"Warning: Could not load MCP tools: {ex.Message}", LogLevel.Warn);
                    }
                }

                // Get response from AI
                var response = await _chatClient.GetResponseAsync(chatHistory, chatOptions);

                // Store in history if requested
                if (includeHistory)
                {
                    _pluginHistory.Add(new ChatMessage(ChatRole.User, prompt));
                    _pluginHistory.Add(new ChatMessage(ChatRole.Assistant, response.Text));
                }

                _logger.Append($"Inference completed successfully", LogLevel.Debug);

                return response.Text;
            }
            catch (Exception ex)
            {
                _logger.Append($"Error during inference: {ex.Message}", LogLevel.Error);
                throw new InvalidOperationException($"Inference failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Clears the conversation history for this plugin.
        /// </summary>
        public void ClearHistory()
        {
            _pluginHistory.Clear();
            _logger.Append("Plugin inference history cleared", LogLevel.Debug);
        }

        /// <summary>
        /// Gets names of available MCP tools.
        /// </summary>
        public async Task<List<string>> GetAvailableToolsAsync()
        {
            try
            {
                if (_mcpClient == null)
                {
                    return new List<string>();
                }

                var tools = await _mcpClient.ListToolsAsync();
                return tools.Select(t => t.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.Append($"Error getting available tools: {ex.Message}", LogLevel.Warn);
                return new List<string>();
            }
        }
    }
}