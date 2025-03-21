/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020;

using gov.llnl.wintap;
using gov.llnl.wintap.core.api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Web.Services.Description;
using System.Runtime.CompilerServices;
using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel.Embeddings;
using Codeblaze.SemanticKernel.Connectors.Ollama;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.Properties;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;
using System.ComponentModel;
using Microsoft.SemanticKernel.Connectors.Chroma;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

//builder.Services.AddSpaStaticFiles(configuration =>
//{
//    configuration.RootPath = @"C:\Program Files\Wintap\Workbench";
//});

WintapLogger.Log.Append($"Wintap is starting.", LogLevel.Info);

var kernelBuilder = Kernel.CreateBuilder();
var kernel = kernelBuilder.AddOpenAIChatCompletion(modelId: "llama3.1:8b", apiKey: null, endpoint: new Uri("http://127.0.0.1:11434")).Build();

HttpClient httpClient = new HttpClient();
httpClient.Timeout = new TimeSpan(0, 5, 0);

//string rag_data = "C:\\programdata\\wintap\\ragdata.txt";
//WintapLogger.Log.Append($"Attempting to load RAG data from file: {rag_data}", LogLevel.Info);


// use use with in-memory vector store
ISemanticTextMemory memory = new MemoryBuilder()
    .WithLoggerFactory(kernel.LoggerFactory)
    .WithMemoryStore(new VolatileMemoryStore())
    .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory)) // Replace with your Ollama API URL
    .Build();

// use a persistent memory store:
//var chromaMemoryStore = new ChromaMemoryStore("http://127.0.0.1:8000");
//ISemanticTextMemory memory = new MemoryBuilder()
//    .WithLoggerFactory(kernel.LoggerFactory)
//    .WithMemoryStore(chromaMemoryStore)
//    .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory)) // Replace with your Ollama API URL
//    .Build();

builder.Services.AddSingleton<ISemanticTextMemory>(provider =>
{
    return memory;
});


builder.Services.AddSingleton<IChatCompletionService>(provider =>
{
    IChatCompletionService ai = kernel.GetRequiredService<IChatCompletionService>();
    return ai;
});

builder.Services.AddSingleton<ChatHistory>(provider =>
{
    string systemPrompt = Settings.Default.SystemPrompt;
    ChatHistory chat = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(systemPrompt);
    return chat;
});

// If you still need views along with APIs, use:
// builder.Services.AddControllersWithViews();

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


