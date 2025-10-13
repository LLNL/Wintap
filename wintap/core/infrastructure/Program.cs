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
using gov.llnl.wintap.Properties;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

// ═══════════════════════════════════════════════════════════════════════════
// APPLICATION INITIALIZATION
// ═══════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
WintapLogger.Log.Append($"Wintap is starting.", LogLevel.Info);

// ═══════════════════════════════════════════════════════════════════════════
// AI INTEGRATION CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════

// ─── AI Provider Selection ─────────────────────────────────────────────────
string aiProvider = "OpenAI"; // "OpenAI" or "Ollama"
if (Settings.Default.AiApiUrl.Contains("localhost"))
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

    mcpClient = await McpClientFactory.CreateAsync(
        new StdioClientTransport(new()
        {
            Command = commandPath,
            Arguments = [],
            Name = "ai_mcp_server",
        })
    );

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
    }
    else
    {
        // OpenAI-Compatible API Configuration
        OpenAIClientOptions openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(Settings.Default.AiApiUrl)
        };

        string key = Settings.Default.AiApiKey;
        ApiKeyCredential cred = new ApiKeyCredential(key!);
        string model = Settings.Default.AiModel;

        WintapLogger.Log.Append($"Using OpenAI provider with model {model}", LogLevel.Info);

        var openAIClient = new OpenAIClient(cred, openAIOptions).GetChatClient(model);

        chatClient = openAIClient
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
    }

    // ─── Chat History Initialization ───────────────────────────────────────
    List<ChatMessage> chatHistory = [
        new ChatMessage(ChatRole.System,
            File.ReadAllText(Path.Combine(Env.FileRootPath, "systemprompt.txt"))),
    ];

    // ─── AI Service Registration ──────────────────────────────────────────
    builder.Services.AddSingleton(chatHistory);
    builder.Services.AddSingleton<IMcpClient>(mcpClient);
    builder.Services.AddSingleton<IChatClient>(chatClient);
}
catch (Exception ex)
{
    WintapLogger.Log.Append($"Error loading AI Client: {ex.Message}", LogLevel.Error);
}

// ═══════════════════════════════════════════════════════════════════════════
// SERVICE CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════

WintapLogger.Log.Append("Configuring dependencies", LogLevel.Info);

// ─── Windows Service & Hosted Services ─────────────────────────────────────
builder.Services.AddWindowsService();
builder.Services.AddHostedService<WinTapSvc>();

// ─── SignalR Configuration ─────────────────────────────────────────────────
builder.Services.AddSignalR();

// ═══════════════════════════════════════════════════════════════════════════
// APPLICATION PIPELINE CONFIGURATION
// ═══════════════════════════════════════════════════════════════════════════

WintapLogger.Log.Append("Building app container", LogLevel.Info);
var app = builder.Build();

// Make service provider available as singleton for dependency access
ServiceProviderAccessor.Services = app.Services;

// ─── Middleware Pipeline ───────────────────────────────────────────────────
//app.UseStaticFiles();
//app.UseSpaStaticFiles();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();  // Map routes to API controllers

// ─── SPA Configuration (Deployment) ────────────────────────────────────────
// Uncomment for deployment with production build
//app.UseSpa(spa =>
//{
//    spa.Options.SourcePath = "C:\\Program Files\\Wintap\\Workbench";
//    if (app.Environment.IsDevelopment())
//    {
//        spa.UseProxyToSpaDevelopmentServer("http://localhost:8099");
//    }
//});

// ─── SignalR Hub Endpoints ─────────────────────────────────────────────────
WintapLogger.Log.Append("Setting up API endpoints", LogLevel.Info);
app.UseEndpoints(endpoints =>
{
    endpoints.MapHub<ExplorerHub>("/signalr/ExplorerHub");
    endpoints.MapHub<WorkbenchHub>("/signalr/WorkbenchHub");
    endpoints.MapHub<InferenceHub>("/signalr/inferenceHub");
});

// ═══════════════════════════════════════════════════════════════════════════
// APPLICATION STARTUP
// ═══════════════════════════════════════════════════════════════════════════

WintapLogger.Log.Append("Running app", LogLevel.Info);
app.Run();
