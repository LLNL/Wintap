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
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

//System.Diagnostics.Debugger.Launch();

WintapLogger.Log.Append($"Wintap is starting.", LogLevel.Info);

#region Local Ollama Implementation
//       for SLMs 
// Match the configuration settings from the embedding generator app
//string DatabasePath = @"c:\program files\wintap7\embeddings.db";
//string CollectionName = "Wintap";
//string OllamaEndpoint = "http://localhost:11434";
//string EmbeddingModel = "mxbai-embed-large";
//string llm = "llama3.1:8b";

// Create a builder with both chat completion and embedding services
//var aiBuilder = Kernel.CreateBuilder();

//       for SLMs 
// Add the Ollama chat completion service
//aiBuilder.AddOllamaChatCompletion(
//    modelId: llm,
//    endpoint: new Uri(OllamaEndpoint)
//);

//       for SLMs 
// Add the Ollama embedding service - using the same model as in the embedding generator
//aiBuilder.AddOllamaTextEmbeddingGeneration(
//    modelId: EmbeddingModel,
//    endpoint: new Uri(OllamaEndpoint)
//);

// Build the kernel with both services
//var kernel = aiBuilder.Build();

//kernel.Plugins.AddFromType<TimePlugin>();

//var chatService = kernel.GetRequiredService<IChatCompletionService>();
//var embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();

//     For SLMs
// Create memory using the embedding service and SQLite
// Use the same database path as the embedding generator
//  var sqliteMemoryStore = SqliteMemoryStore.ConnectAsync(DatabasePath).GetAwaiter().GetResult();

//ISemanticTextMemory memory = new MemoryBuilder()
//    .WithLoggerFactory(kernel.LoggerFactory)
//    .WithMemoryStore(sqliteMemoryStore)
//    .WithTextEmbeddingGeneration(embeddingService)
//    .Build();

//builder.Services.AddSingleton<ISemanticTextMemory>(provider =>
//{
//    return memory;
//});

//builder.Services.AddSingleton<IChatCompletionService>(provider =>
//{
//    return chatService;
//});

//builder.Services.AddSingleton<Kernel>(provider =>
//{
//    return kernel; 
//});


//builder.Services.AddSingleton<ChatHistory>(provider =>
//{
//    string systemPrompt = Settings.Default.SystemPrompt;
//    ChatHistory chat = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(systemPrompt);
//    WintapLogger.Log.Append(systemPrompt, LogLevel.Info);
//    return chat;
//}); 
#endregion

IMcpClient mcpClient;
try
{
    mcpClient = await McpClientFactory.CreateAsync(
    new StdioClientTransport(new()
    {
        Command = "C:\\Repos\\BCB-AI\\ai_mcp_server\\bin\\debug\\net8.0\\ai_mcp_server.exe",
        Arguments = [],
        Name = "ai_mcp_server",
    })
);
    // Connect to an MCP server
    Console.WriteLine("Connecting client to MCP server");

    OpenAIClientOptions openAIOptions = new OpenAIClientOptions();
    openAIOptions = new OpenAIClientOptions() { Endpoint = new Uri("https://livai-api-dev.llnl.gov/v1") };
    

    string? key = "sk-eU9jfjiaKRN3tpLPyx2Dmw";
    //key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    ApiKeyCredential cred = new ApiKeyCredential(key!);
    var openAIClient = new OpenAIClient(cred, openAIOptions).GetChatClient("gpt-4.1");

    // Create a sampling client.
    using IChatClient chatClient = openAIClient.AsIChatClient()
        .AsBuilder()
        .UseFunctionInvocation()
        .Build();

    List<ChatMessage> chatHistory = [
        new ChatMessage(ChatRole.System, System.IO.File.ReadAllText(Path.Combine(Strings.FileRootPath, "systemprompt.txt"))),
    ];
    builder.Services.AddSingleton(chatHistory);

    builder.Services.AddSingleton<IMcpClient>(mcpClient);
    builder.Services.AddSingleton(chatClient);
}
catch(Exception ex)
{
    WintapLogger.Log.Append($"Error loading MCP Client: {ex.Message}", LogLevel.Error);
}




// Configuration for Windows Service and Hosted Service
builder.Services.AddWindowsService();
builder.Services.AddHostedService<WinTapSvc>();
builder.Services.AddSignalR();

var app = builder.Build();

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

app.UseEndpoints(endpoints =>
{
    endpoints.MapHub<ExplorerHub>("/signalr/ExplorerHub");
    endpoints.MapHub<WorkbenchHub>("/signalr/WorkbenchHub");
    endpoints.MapHub<InferenceHub>("/signalr/inferenceHub");
});



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