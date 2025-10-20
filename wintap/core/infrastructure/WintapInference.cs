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
using System.Text.Json;
using System.Threading.Tasks;
using static gov.llnl.wintap.Interfaces;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Implementation of IInfer that wraps the AI chat client and MCP tools.
    /// Provides a simplified interface for plugins to access AI inference capabilities.
    /// Uses MCP (Model Context Protocol) exclusively for tool calling.
    /// Supports multiple MCP servers (core + plugin-specific) with automatic tool namespacing.
    /// </summary>
    public class WintapInference : IInfer
    {
        private readonly IChatClient _chatClient;
        private readonly PluginMcpManager _mcpManager;
        private readonly IWintapLogger _logger;
        private readonly List<ChatMessage> _pluginHistory;

        /// <summary>
        /// Creates a new WintapInference instance.
        /// </summary>
        /// <param name="chatClient">The AI chat client</param>
        /// <param name="mcpManager">The plugin MCP manager for tool aggregation</param>
        /// <param name="logger">Logger for diagnostics</param>
        public WintapInference(IChatClient chatClient, PluginMcpManager mcpManager, IWintapLogger logger)
        {
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
            _mcpManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));
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

                // Add all MCP tools if requested
                if (useTools)
                {
                    try
                    {
                        var tools = await LoadAllToolsAsync();
                        _logger.Append($"Loaded {tools.Count} total tools for request", LogLevel.Info);

                        if (tools.Count > 0)
                        {
                            // Log each tool being made available
                            foreach (var tool in tools)
                            {
                                _logger.Append($"  Tool available: {tool.Name} - {tool.Description}", LogLevel.Debug);
                            }

                            chatOptions.Tools = [.. tools];
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

                            _logger.Append($"Chat options configured with {tools.Count} tools", LogLevel.Info);
                        }
                        else
                        {
                            _logger.Append("No tools available for this request", LogLevel.Warn);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Append($"Warning: Could not load tools: {ex.Message}", LogLevel.Warn);
                        _logger.Append($"Tool loading stack trace: {ex.StackTrace}", LogLevel.Debug);
                    }
                }

                // Get response from AI
                _logger.Append("Sending request to AI...", LogLevel.Debug);
                var response = await _chatClient.GetResponseAsync(chatHistory, chatOptions);
                _logger.Append($"Received response from AI", LogLevel.Debug);

                // Log tool calls if any were made
                if (response.Messages != null && response.Messages.Any())
                {
                    foreach (var msg in response.Messages)
                    {
                        if (msg.Contents != null)
                        {
                            foreach (var content in msg.Contents)
                            {
                                if (content is FunctionCallContent toolCall)
                                {
                                    _logger.Append($"AI called tool: {toolCall.Name} with args: {toolCall.Arguments}", LogLevel.Info);
                                }
                                else if (content is FunctionResultContent toolResult)
                                {
                                    var resultPreview = toolResult.Result?.ToString()?.Substring(0, Math.Min(100, toolResult.Result?.ToString()?.Length ?? 0)) ?? "";
                                    _logger.Append($"Tool result from {toolResult.CallId}: {resultPreview}", LogLevel.Info);
                                }
                            }
                        }
                    }
                }

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
        /// Simple inference using only specific MCP tools by name.
        /// </summary>
        public async Task<string> AskWithToolsAsync(string prompt, List<string> toolNames, float temperature = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
            }

            if (toolNames == null || toolNames.Count == 0)
            {
                throw new ArgumentException("At least one tool name must be provided", nameof(toolNames));
            }

            try
            {
                _logger.Append($"Processing inference with specific tools: {string.Join(", ", toolNames)}", LogLevel.Debug);

                var chatHistory = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.User, prompt)
                };

                // Load all tools and filter to requested names
                var allTools = await LoadAllToolsAsync();
                _logger.Append($"Total tools available: {allTools.Count}", LogLevel.Info);

                var selectedTools = allTools.Where(t => toolNames.Contains(t.Name)).ToList();

                _logger.Append($"Requested tools: {string.Join(", ", toolNames)}", LogLevel.Info);
                _logger.Append($"Matched tools: {selectedTools.Count}", LogLevel.Info);

                if (selectedTools.Count == 0)
                {
                    var availableNames = string.Join(", ", allTools.Select(t => t.Name));
                    _logger.Append($"WARNING: None of the requested tools were found!", LogLevel.Warn);
                    _logger.Append($"Available tools: {availableNames}", LogLevel.Warn);
                }
                else
                {
                    foreach (var tool in selectedTools)
                    {
                        _logger.Append($"  Using tool: {tool.Name}", LogLevel.Debug);
                    }
                }

                var chatOptions = new ChatOptions
                {
                    Temperature = temperature,
                    Tools = [.. selectedTools],
                    ToolMode = ChatToolMode.Auto
                };

                // OpenAI-specific settings
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

                _logger.Append($"Using {selectedTools.Count} tools", LogLevel.Debug);

                _logger.Append("Sending request to AI with selected tools...", LogLevel.Debug);
                var response = await _chatClient.GetResponseAsync(chatHistory, chatOptions);
                _logger.Append($"Received response from AI", LogLevel.Debug);

                // Log tool calls if any were made
                if (response.Messages != null && response.Messages.Any())
                {
                    foreach (var msg in response.Messages)
                    {
                        if (msg.Contents != null)
                        {
                            foreach (var content in msg.Contents)
                            {
                                if (content is FunctionCallContent toolCall)
                                {
                                    _logger.Append($"AI called tool: {toolCall.Name} with args: {toolCall.Arguments}", LogLevel.Info);
                                }
                                else if (content is FunctionResultContent toolResult)
                                {
                                    var resultPreview = toolResult.Result?.ToString()?.Substring(0, Math.Min(100, toolResult.Result?.ToString()?.Length ?? 0)) ?? "";
                                    _logger.Append($"Tool result from {toolResult.CallId}: {resultPreview}", LogLevel.Info);
                                }
                            }
                        }
                    }
                }

                _logger.Append($"Inference with tools completed", LogLevel.Debug);

                return response.Text;
            }
            catch (Exception ex)
            {
                _logger.Append($"Error during inference with tools: {ex.Message}", LogLevel.Error);
                throw new InvalidOperationException($"Inference with tools failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets a structured JSON response deserialized to type T.
        /// Instructs the AI to return data matching the structure of T.
        /// </summary>
        public async Task<T> AskStructuredAsync<T>(string prompt, float temperature = 1.0f) where T : class
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
            }

            try
            {
                _logger.Append($"Processing structured inference request for type {typeof(T).Name}", LogLevel.Debug);

                // Enhance prompt to request JSON format
                var structuredPrompt = $@"{prompt}

IMPORTANT: You must respond with valid JSON matching this structure. Do not include any explanation or markdown, just the raw JSON.

Expected JSON structure based on C# type {typeof(T).Name}:
{GetTypeDescription<T>()}

Respond with valid JSON only.";

                var chatHistory = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.User, structuredPrompt)
                };

                // Configure for JSON response
                var chatOptions = new ChatOptions
                {
                    Temperature = temperature,
                    ResponseFormat = ChatResponseFormat.Json
                };

                // Add all MCP tools if available
                try
                {
                    var tools = await LoadAllToolsAsync();
                    if (tools.Count > 0)
                    {
                        chatOptions.Tools = [.. tools];
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

                        _logger.Append($"Structured inference: {tools.Count} MCP tools available", LogLevel.Debug);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Append($"Warning: Could not load tools for structured inference: {ex.Message}", LogLevel.Warn);
                }

                // Get response
                var response = await _chatClient.GetResponseAsync(chatHistory, chatOptions);

                _logger.Append($"Received structured response, attempting deserialization", LogLevel.Debug);

                // Deserialize the JSON response
                var result = JsonSerializer.Deserialize<T>(response.Text, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                });

                if (result == null)
                {
                    throw new InvalidOperationException("Failed to deserialize AI response to requested type");
                }

                _logger.Append($"Successfully deserialized response to {typeof(T).Name}", LogLevel.Debug);

                return result;
            }
            catch (JsonException ex)
            {
                _logger.Append($"JSON deserialization error: {ex.Message}", LogLevel.Error);
                throw new InvalidOperationException($"AI response was not valid JSON for type {typeof(T).Name}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.Append($"Error during structured inference: {ex.Message}", LogLevel.Error);
                throw new InvalidOperationException($"Structured inference failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Structured inference using only specific MCP tools by name.
        /// Combines tool selection with structured JSON output.
        /// </summary>
        public async Task<T> AskWithToolsAsync<T>(string prompt, List<string> toolNames, float temperature = 1.0f) where T : class
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
            }

            if (toolNames == null || toolNames.Count == 0)
            {
                throw new ArgumentException("At least one tool name must be provided", nameof(toolNames));
            }

            try
            {
                _logger.Append($"Processing structured inference with tools: {string.Join(", ", toolNames)}", LogLevel.Debug);

                // Enhance prompt for structured output
                var structuredPrompt = $@"{prompt}

IMPORTANT: You must respond with valid JSON matching this structure. Do not include any explanation or markdown, just the raw JSON.

Expected JSON structure based on C# type {typeof(T).Name}:
{GetTypeDescription<T>()}

Respond with valid JSON only.";

                var chatHistory = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.User, structuredPrompt)
                };

                // Load all tools and filter to requested names
                var allTools = await LoadAllToolsAsync();
                var selectedTools = allTools.Where(t => toolNames.Contains(t.Name)).ToList();

                if (selectedTools.Count == 0)
                {
                    var availableNames = string.Join(", ", allTools.Select(t => t.Name));
                    _logger.Append($"WARNING: None of the requested tools were found!", LogLevel.Warn);
                    _logger.Append($"Available tools: {availableNames}", LogLevel.Warn);
                }

                var chatOptions = new ChatOptions
                {
                    Temperature = temperature,
                    ResponseFormat = ChatResponseFormat.Json,
                    Tools = [.. selectedTools],
                    ToolMode = ChatToolMode.Auto
                };

                // OpenAI-specific settings
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

                _logger.Append($"Using {selectedTools.Count} tools for structured response", LogLevel.Debug);

                var response = await _chatClient.GetResponseAsync(chatHistory, chatOptions);

                _logger.Append($"Received structured response, deserializing to {typeof(T).Name}", LogLevel.Debug);

                // Deserialize the JSON response
                var result = JsonSerializer.Deserialize<T>(response.Text, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                });

                if (result == null)
                {
                    throw new InvalidOperationException("Failed to deserialize AI response to requested type");
                }

                _logger.Append($"Successfully deserialized to {typeof(T).Name}", LogLevel.Debug);

                return result;
            }
            catch (JsonException ex)
            {
                _logger.Append($"JSON deserialization error: {ex.Message}", LogLevel.Error);
                throw new InvalidOperationException($"AI response was not valid JSON for type {typeof(T).Name}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.Append($"Error during structured inference with tools: {ex.Message}", LogLevel.Error);
                throw new InvalidOperationException($"Structured inference with tools failed: {ex.Message}", ex);
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
        /// Gets names of available MCP tools (core + all plugins).
        /// </summary>
        public async Task<List<string>> GetAvailableToolsAsync()
        {
            try
            {
                return await _mcpManager.GetToolNamesAsync();
            }
            catch (Exception ex)
            {
                _logger.Append($"Error getting available MCP tools: {ex.Message}", LogLevel.Warn);
                return new List<string>();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads all available MCP tools (core + plugin) for a chat request.
        /// </summary>
        private async Task<List<AITool>> LoadAllToolsAsync()
        {
            try
            {
                var tools = await _mcpManager.GetAllToolsAsync();
                _logger.Append($"Loaded {tools.Count} total MCP tools (core + plugin)", LogLevel.Debug);
                return tools;
            }
            catch (Exception ex)
            {
                _logger.Append($"Warning: Could not load MCP tools: {ex.Message}", LogLevel.Warn);
                return new List<AITool>();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // HELPER METHODS FOR STRUCTURED OUTPUT
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates a simple description of the expected type structure.
        /// </summary>
        private string GetTypeDescription<T>()
        {
            var type = typeof(T);
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            var propDescriptions = properties.Select(p =>
                $"  \"{ToCamelCase(p.Name)}\": {GetPropertyTypeDescription(p.PropertyType)}"
            );

            return "{\n" + string.Join(",\n", propDescriptions) + "\n}";
        }

        /// <summary>
        /// Gets a simple type description for JSON schema hints.
        /// </summary>
        private string GetPropertyTypeDescription(Type type)
        {
            if (type == typeof(string))
                return "\"string value\"";
            if (type == typeof(int) || type == typeof(long))
                return "integer";
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
                return "number";
            if (type == typeof(bool))
                return "true or false";
            if (type == typeof(DateTime))
                return "\"ISO 8601 date\"";
            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
                return "[...]";

            return "{}";
        }

        /// <summary>
        /// Converts PascalCase property names to camelCase for JSON.
        /// </summary>
        private string ToCamelCase(string str)
        {
            if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
                return str;

            return char.ToLower(str[0]) + str.Substring(1);
        }
    }
}