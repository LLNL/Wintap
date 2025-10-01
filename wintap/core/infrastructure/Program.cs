/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020, SKEXP0070;

using DuckDB.NET.Data;
using DuckDB.NET.Data;
using gov.llnl.wintap;
using gov.llnl.wintap.core.api;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.infrastructure;
using gov.llnl.wintap.Properties;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Sqlite;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using ModelContextProtocol.Client;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

//System.Diagnostics.Debugger.Launch();

WintapLogger.Log.Append($"Wintap is starting.", LogLevel.Info);

// Configure AI Provider selection
string aiProvider = "OpenAI"; // "OpenAI" or "Ollama"
if(Settings.Default.AiApiUrl.Contains("localhost"))
{
    aiProvider = "Ollama";
}

IMcpClient mcpClient;
IChatClient chatClient;

try
{
    string fileRootPath = Strings.FileRootPath;
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


    Console.WriteLine("Connecting client to MCP server");

    // Configure based on provider
    if (aiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
    {
        string ollamaEndpoint = Settings.Default.AiApiUrl;
        string ollamaModel = Settings.Default.AiModel;

        WintapLogger.Log.Append($"Using Ollama provider: {ollamaEndpoint} with model {ollamaModel}", LogLevel.Info);

        // Create custom Ollama client (already implements IChatClient)
        IChatClient ollamaClient = new OllamaChatClient(ollamaEndpoint, ollamaModel);

        // Add function invocation support for MCP tools
        chatClient = ollamaClient
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
    }
    else
    {
        OpenAIClientOptions openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(Settings.Default.AiApiUrl)
        };

        string key = Settings.Default.AiApiKey;
        ApiKeyCredential cred = new ApiKeyCredential(key!);

        string model = Settings.Default.AiModel;
        WintapLogger.Log.Append($"Using OpenAI provider with model {model}", LogLevel.Info);

        var openAIClient = new OpenAIClient(cred, openAIOptions).GetChatClient(model);

        // Convert to IChatClient and add function invocation support
        chatClient = openAIClient
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
    }

    // Initialize chat history (same for both providers)
    List<ChatMessage> chatHistory = [
        new ChatMessage(ChatRole.System,
            File.ReadAllText(Path.Combine(Strings.FileRootPath, "systemprompt.txt"))),
    ];

    // Register services
    builder.Services.AddSingleton(chatHistory);
    builder.Services.AddSingleton<IMcpClient>(mcpClient);
    builder.Services.AddSingleton<IChatClient>(chatClient);
}
catch (Exception ex)
{
    WintapLogger.Log.Append($"Error loading AI Client: {ex.Message}", LogLevel.Error);
}


WintapLogger.Log.Append("Recovering process tree", LogLevel.Info);
FileInfo mainDBInfo = new FileInfo(Path.Combine(Strings.FileDataRoot, "ProcessTree", "main.duckdb"));
mainDBInfo.Delete();
mainDBInfo = new FileInfo(Path.Combine(Strings.FileDataRoot, "ProcessTree", "main.duckdb.wal"));
mainDBInfo.Delete();

CallDatabaseRecovery();

// Configuration for Windows Service and Hosted Service
WintapLogger.Log.Append("Configuring dependencies", LogLevel.Info);
builder.Services.AddWindowsService();
builder.Services.AddHostedService<WinTapSvc>();

//builder.Services.AddSingleton<ProcessTreeDatabaseManager>();
//builder.Services.AddHostedService<ProcessTreeDatabaseManager>(provider => provider.GetService<ProcessTreeDatabaseManager>());

builder.Services.AddSignalR();

WintapLogger.Log.Append("Building app container", LogLevel.Info);
var app = builder.Build();
// make available as singleton 
ServiceProviderAccessor.Services = app.Services;

//app.UseStaticFiles();
//app.UseSpaStaticFiles();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();  // This will map the routes to the API controllers

//   Uncomment for deployment
//app.UseSpa(spa =>
//{
//    spa.Options.SourcePath = "C:\\Program Files\\Wintap\\Workbench";
//    if (app.Environment.IsDevelopment())
//    {
//        spa.UseProxyToSpaDevelopmentServer("http://localhost:8099"); // URL of the prod server
//    }
//});

WintapLogger.Log.Append("setting up API endpoints", LogLevel.Info);
app.UseEndpoints(endpoints =>
{
    endpoints.MapHub<ExplorerHub>("/signalr/ExplorerHub");
    endpoints.MapHub<WorkbenchHub>("/signalr/WorkbenchHub");
    endpoints.MapHub<InferenceHub>("/signalr/inferenceHub");
});


WintapLogger.Log.Append("running app", LogLevel.Info);
app.Run();


//public class TimePlugin
//{
//    [KernelFunction]
//    [Description("Returns the current time")]
//    public string GetCurrentTime()
//    {
//        return DateTime.Now.ToShortTimeString();
//    }
//}

bool CallDatabaseRecovery()
{
    try
    {
        //System.Diagnostics.Debugger.Launch();
        var processInfo = new ProcessStartInfo
        {
            FileName = "WintapCoreSvcMgr.exe",
            Arguments = "RECOVER_DATABASE",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = true
        };

        System.Diagnostics.Process wintapSvcMgr = new System.Diagnostics.Process();
        wintapSvcMgr.StartInfo = processInfo;
        wintapSvcMgr.Start();
        wintapSvcMgr.WaitForExit();
        while (System.Diagnostics.Process.GetProcessesByName("WintapCoreSvcMgr").Length > 0)
        {
            System.Threading.Thread.Sleep(100);
        }
        if (wintapSvcMgr.ExitCode == 0)
        {
            WintapLogger.Log.Append("Database recovery completed successfully", LogLevel.Info);
            return true;
        }
        else
        {
            var error = wintapSvcMgr.StandardError.ReadToEnd();
            WintapLogger.Log.Append($"Database recovery failed: {error}", LogLevel.Error);
            return false;
        }
    }
    catch (Exception ex)
    {
        WintapLogger.Log.Append($"Error calling database recovery: {ex.Message}", LogLevel.Error);
        return false;
    }
}