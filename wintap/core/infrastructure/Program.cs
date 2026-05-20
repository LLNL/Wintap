/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

//  these disables are required for the semantic kernel libraries
#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020, SKEXP0070;

using gov.llnl.wintap;
using gov.llnl.wintap.core.api;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using EtlPaths = gov.llnl.wintap.core.etl.shared.Paths;
using gov.llnl.wintap.Properties;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static gov.llnl.wintap.Interfaces;
using Microsoft.Extensions.Hosting;

#if WINDOWS
using gov.llnl.wintap.platform.windows.infrastructure;
#endif

#if LINUX
using gov.llnl.wintap.platform.linux.infrastructure;
#endif

#if MACOS
using gov.llnl.wintap.platform.macos.infrastructure;
#endif


#if WINDOWS
using gov.llnl.wintap.platform.windows.infrastructure;
#endif




// ═══════════════════════════════════════════════════════════════════════════
// APPLICATION INITIALIZATION
// ═══════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// ─── Configure Wintap to listen on port 8099 ───────────────────────────────
builder.WebHost.UseUrls("http://localhost:8099");

builder.Services.AddControllers();

// ═══════════════════════════════════════════════════════════════════════════
// AI INTEGRATION CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════

// ─── AI Provider Selection ─────────────────────────────────────────────────
string aiProvider = "OpenAI"; // "OpenAI" or "Ollama"
string configuredUrl = Settings.Default.AiApiUrl;

WintapLogger.Log.Append($"Configured AI URL: {configuredUrl}", LogLevel.Info);

if (configuredUrl.Contains("localhost"))
{
    aiProvider = "Ollama";
}

IMcpClient mcpClient;
IChatClient chatClient;

try
{
    // ─── MCP Server Configuration ──────────────────────────────────────────
    string fileRootPath = Env.FileRootPath;
    string exeName;

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        exeName = "wintap_mcp_server.exe";
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
             RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        exeName = "wintap_mcp_server"; // No .exe extension
    }
    else
    {
        throw new PlatformNotSupportedException("Unsupported OS");
    }

    string commandPath = Path.Combine(fileRootPath, "mcp", exeName);

    WintapLogger.Log.Append($"MCP server path: {commandPath}", LogLevel.Info);

    mcpClient = await McpClientFactory.CreateAsync(
        new StdioClientTransport(new()
        {
            Command = commandPath,
            Arguments = [],
            Name = "ai_mcp_server",
        })
    );

    WintapLogger.Log.Append("MCP client initialized successfully", LogLevel.Info);

    IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
    WintapLogger.Log.Append($"MCP tool count: {mcpTools.Count}", LogLevel.Info);
    foreach (McpClientTool tool in mcpTools)
    {
        WintapLogger.Log.Append($"MCP tool: {tool.Name}", LogLevel.Info);
    }

    // ─── Chat Client Configuration (Provider-Specific) ────────────────────
    if (aiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
    {
        // Ollama (Local LLM) Configuration
        string ollamaEndpoint = Settings.Default.AiApiUrl;
        string ollamaModel = Settings.Default.AiModel;

        WintapLogger.Log.Append($"Using Ollama provider: {ollamaEndpoint} with model {ollamaModel}", LogLevel.Info);

        IChatClient ollamaClient = new OllamaChatClient(ollamaEndpoint, ollamaModel);

        chatClient = ollamaClient
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        WintapLogger.Log.Append("Ollama chat client built successfully", LogLevel.Info);
    }
    else
    {
        // OpenAI-Compatible API Configuration (including Open-WebUI)
        string apiUrl = Settings.Default.AiApiUrl;
        string apiKey = Settings.Default.AiApiKey;
        string model = Settings.Default.AiModel;

        WintapLogger.Log.Append($"Configuring OpenAI-compatible endpoint", LogLevel.Info);
        WintapLogger.Log.Append($"  Endpoint: {apiUrl}", LogLevel.Info);
        WintapLogger.Log.Append($"  Model: {model}", LogLevel.Info);
        WintapLogger.Log.Append($"  API Key: {(string.IsNullOrEmpty(apiKey) ? "NOT SET" : "***" + apiKey.Substring(Math.Max(0, apiKey.Length - 4)))}", LogLevel.Info);

        OpenAIClientOptions openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(apiUrl)
        };

        ApiKeyCredential cred = new ApiKeyCredential(apiKey);

        WintapLogger.Log.Append($"Creating OpenAI client...", LogLevel.Info);

        var openAIClient = new OpenAIClient(cred, openAIOptions).GetChatClient(model);

        chatClient = openAIClient
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        WintapLogger.Log.Append("OpenAI-compatible chat client built successfully", LogLevel.Info);
    }

    // ─── Chat History Initialization ───────────────────────────────────────
    string systemPromptPath = Path.Combine(Env.FileRootPath, "systemprompt.txt");
    WintapLogger.Log.Append($"Loading system prompt from: {systemPromptPath}", LogLevel.Info);

    List<ChatMessage> chatHistory = [
        new ChatMessage(ChatRole.System,
            File.ReadAllText(systemPromptPath)),
    ];

    WintapLogger.Log.Append("System prompt loaded successfully", LogLevel.Info);

    // ─── Create Plugin MCP Manager ─────────────────────────────────────────
    WintapLogger.Log.Append("Creating Plugin MCP Manager", LogLevel.Info);
    var pluginMcpManager = new PluginMcpManager(mcpClient, WintapLogger.Log);

    // ─── AI Service Registration ──────────────────────────────────────────
    builder.Services.AddSingleton(chatHistory);
    builder.Services.AddSingleton<IMcpClient>(mcpClient);
    builder.Services.AddSingleton<IChatClient>(chatClient);
    builder.Services.AddSingleton<PluginMcpManager>(pluginMcpManager);

    WintapLogger.Log.Append("AI services registered in DI container", LogLevel.Info);

}
catch (Exception ex)
{
    WintapLogger.Log.Append($"Error loading AI Client: {ex.Message}", LogLevel.Error);
    WintapLogger.Log.Append($"AI initialization stack trace: {ex.StackTrace}", LogLevel.Error);
}

// ═══════════════════════════════════════════════════════════════════════════
// SERVICE CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════

WintapLogger.Log.Append("Configuring dependencies", LogLevel.Info);

// ─── Logger Registration ───────────────────────────────────────────────────
// Register WintapLogger as IWintapLogger for plugin dependency injection
builder.Services.AddSingleton<IWintapLogger>(sp => WintapLogger.Log);

// ─── Inference Registration ────────────────────────────────────────────────
// Register WintapInference as IInfer for plugin AI access
builder.Services.AddSingleton<IInfer>(sp =>
{
    var chatClient = sp.GetService<IChatClient>();
    var mcpManager = sp.GetRequiredService<PluginMcpManager>();
    var logger = sp.GetService<IWintapLogger>();

    if (chatClient == null)
    {
        WintapLogger.Log.Append("Warning: IChatClient not available. IInfer will not be available to plugins.", LogLevel.Warn);
        return null;
    }

    return new WintapInference(chatClient, mcpManager, logger);
});

// ─── Process Resolver Registration (Platform-Specific) ────────────────────
WintapLogger.Log.Append("Registering platform-specific process resolver", LogLevel.Info);

// ─── Process Resolver Registration (cross-platform) ────────────────────
WintapLogger.Log.Append("Registering cross-platform process resolver", LogLevel.Info);
IProcessResolver processResolver = new ProcessResolver();
builder.Services.AddSingleton<IProcessResolver>(processResolver);


// ─── Windows Service & Hosted Services ─────────────────────────────────────
#if WINDOWS
builder.Services.AddWindowsService();
#elif LINUX
builder.Services.AddSystemd();  // For Linux systemd integration
#elif MACOS
// macOS doesn't need a service wrapper for now
#endif
builder.Services.AddHostedService<WinTapSvc>();

// ─── SignalR Configuration ─────────────────────────────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSpaStaticFiles(configuration =>
{
    configuration.RootPath = Path.Combine(Env.FileRootPath, "Workbench");
});

// ═══════════════════════════════════════════════════════════════════════════
// APPLICATION PIPELINE CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════

WintapLogger.Log.Append("Building app container", LogLevel.Info);
var app = builder.Build();

// Make service provider available as singleton for dependency access
ServiceProviderAccessor.Services = app.Services;

// ─── Middleware Pipeline ───────────────────────────────────────────────────
if (Settings.Default.EnableWorkbench)
{
    app.UseStaticFiles();
    app.UseSpaStaticFiles();
}

app.UseRouting();
app.UseAuthorization();
app.MapControllers();  // Map routes to API controllers

// ─── SPA Configuration (Serve Angular Static Files) ───────────────────────
if (Settings.Default.EnableWorkbench)
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = Path.Combine(Env.FileRootPath, "Workbench");
        WintapLogger.Log.Append($"Workbench enabled, serving static files from {spa.Options.SourcePath}", LogLevel.Info);
    });
}
else
{
    WintapLogger.Log.Append("Workbench disabled and will not be served.", LogLevel.Info);
}

// ─── SignalR Hub Endpoints ─────────────────────────────────────────────────
WintapLogger.Log.Append("Setting up API endpoints", LogLevel.Info);
app.UseEndpoints(endpoints =>
{
    #if WINDOWS
        {
            // Windows-specific
            //endpoints.MapHub<ExplorerHub>("/signalr/ExplorerHub");
        }
    #endif

    endpoints.MapHub<WorkbenchHub>("/signalr/WorkbenchHub");
    endpoints.MapHub<InferenceHub>("/signalr/inferenceHub");
});

// ═══════════════════════════════════════════════════════════════════════════
// APPLICATION STARTUP
// ═══════════════════════════════════════════════════════════════════════════

app.Lifetime.ApplicationStarted.Register(() =>
{
    string parquetPath = EtlPaths.ParquetDataPath;
    string rawSensorPath = Path.Combine(parquetPath, "raw_sensor");
    Console.WriteLine($"Wintap parquet data path: {parquetPath}");
    Console.WriteLine($"Wintap raw_sensor data path: {rawSensorPath}");
    WintapLogger.Log.Append($"Wintap parquet data path: {parquetPath}", LogLevel.Info);
    WintapLogger.Log.Append($"Wintap raw_sensor data path: {rawSensorPath}", LogLevel.Info);
});

WintapLogger.Log.Append("Running app", LogLevel.Info);
app.Run();
