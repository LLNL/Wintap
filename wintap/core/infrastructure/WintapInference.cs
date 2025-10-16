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
                    ResponseFormat = ChatResponseFormat.Json  // Request JSON format
                };

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