/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020, SKEXP0070;

using gov.llnl.wintap;
using gov.llnl.wintap.core.api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel;
using System;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.Properties;
// Remove Chroma and add SQLite import
//using Microsoft.SemanticKernel.Connectors.Memory.Sqlite;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Connectors.Sqlite;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
//builder.Services.AddSpaStaticFiles(configuration =>
//{
//    configuration.RootPath = @"C:\Program Files\Wintap\Workbench";
//});

WintapLogger.Log.Append($"Wintap is starting.", LogLevel.Info);

// Match the configuration settings from the embedding generator app
string DatabasePath =  @"c:\program files\wintap7\embeddings.db";
string CollectionName = "Wintap";
string OllamaEndpoint = "http://localhost:11434";
string EmbeddingModel = "mxbai-embed-large";
string llm = "Wintap-llama-3b";

#pragma warning disable SKEXP0070, SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020
// Create a builder with both chat completion and embedding services
var aiBuilder = Kernel.CreateBuilder();

// Add the Ollama chat completion service
aiBuilder.AddOllamaChatCompletion(
    modelId: llm,
    endpoint: new Uri(OllamaEndpoint)
);

// Add the Ollama embedding service - using the same model as in the embedding generator
aiBuilder.AddOllamaTextEmbeddingGeneration(
    modelId: EmbeddingModel,
    endpoint: new Uri(OllamaEndpoint)
);

// Build the kernel with both services
var kernel = aiBuilder.Build();

// Now you can get both services
var chatService = kernel.GetRequiredService<IChatCompletionService>();
var embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();

// Create memory using the embedding service and SQLite
// Use the same database path as the embedding generator
var sqliteMemoryStore = SqliteMemoryStore.ConnectAsync(DatabasePath).GetAwaiter().GetResult();

ISemanticTextMemory memory = new MemoryBuilder()
    .WithLoggerFactory(kernel.LoggerFactory)
    .WithMemoryStore(sqliteMemoryStore)
    .WithTextEmbeddingGeneration(embeddingService)
    .Build();

builder.Services.AddSingleton<ISemanticTextMemory>(provider =>
{
    return memory;
});

builder.Services.AddSingleton<IChatCompletionService>(provider =>
{
    return chatService;
});



builder.Services.AddSingleton<ChatHistory>(provider =>
{
    string systemPrompt = Settings.Default.SystemPrompt;
   
    ChatHistory chat = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(systemPrompt);
    WintapLogger.Log.Append(systemPrompt, LogLevel.Info);
    return chat;
});



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

//app.UseSpa(spa =>
//{
//    spa.Options.SourcePath = "C:\\Program Files\\Wintap\\Workbench";
//    if (app.Environment.IsDevelopment())
//    {
//        spa.UseProxyToSpaDevelopmentServer("http://localhost:8099"); // URL of the dev server
//    }
//});

app.UseEndpoints(endpoints =>
{
    endpoints.MapHub<ExplorerHub>("/signalr/ExplorerHub");
    endpoints.MapHub<WorkbenchHub>("/signalr/WorkbenchHub");
    endpoints.MapHub<InferenceHub>("/signalr/inferenceHub");
});

app.Run();