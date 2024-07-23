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
using SampleApp.Services;
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
// Add services to the DI container.
// If you only need API controllers (no views or pages), use this:
builder.Services.AddControllers();

//builder.Services.AddSpaStaticFiles(configuration =>
//{
//    configuration.RootPath = @"C:\Program Files\Wintap\Workbench";
//});


var kernelBuilder = Kernel.CreateBuilder();
var kernel = kernelBuilder.AddOpenAIChatCompletion(modelId: "phi3", apiKey: null, endpoint: new Uri("http://127.0.0.1:11434"))
    .Build();

HttpClient httpClient = new HttpClient();
httpClient.Timeout = new TimeSpan(0, 5, 0);

//string rag_data = "C:\\programdata\\wintap\\ragdata.txt";
//WintapLogger.Log.Append($"Attempting to load RAG data from file: {rag_data}", LogLevel.Always);

// to use a persistent memory store, do this:
var chromaMemoryStore = new ChromaMemoryStore("http://127.0.0.1:8000");
// then use chromaMemoryStore in WithMemoryStore

//ISemanticTextMemory memory = new MemoryBuilder()
//    .WithLoggerFactory(kernel.LoggerFactory)
//    .WithMemoryStore(new VolatileMemoryStore())
//    .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory)) // Replace with your Ollama API URL
//    .Build();

ISemanticTextMemory memory = new MemoryBuilder()
    .WithLoggerFactory(kernel.LoggerFactory)
    .WithMemoryStore(chromaMemoryStore)
    .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory)) // Replace with your Ollama API URL
    .Build();


BackgroundWorker worker = new BackgroundWorker();
worker.DoWork += Worker_DoWork;
worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
//  skipping this because work is now done from shell command line utility.
//worker.RunWorkerAsync();



void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
{
    WintapLogger.Log.Append("Embedding creator thread completed!!", LogLevel.Always);
}

async void Worker_DoWork(object sender, DoWorkEventArgs e)
{
    //WintapLogger.Log.Append($"Chunking data: {rag_data}", LogLevel.Always);

    //string collectionName = "testCollection";
    ////string s = System.IO.File.ReadAllText(rag_data);
    ////List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(TextChunker.SplitPlainTextLines(s, 128), 6144);

    //// Split text into lines first
    //List<string> lines = TextChunker.SplitPlainTextLines(s, 128);

    //// Split lines into paragraphs/chunks with overlap
    //int chunkSize = 500;
    //int overlapSize = 50; // Specify the overlap size

    ////List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, chunkSize, overlapSize, "DOCUMENT NAME: Process Telemetry for Windows\n");
    //List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, chunkSize, overlapSize, " ");

    //try
    //{
    //    WintapLogger.Log.Append($"data chunked into {paragraphs.Count} paragraphs. Get embeddings...", LogLevel.Always);
    //    for (int i = 0; i < paragraphs.Count; i++)
    //    {
    //        WintapLogger.Log.Append($"paragraph {i} has a length of {paragraphs[i].Length}", LogLevel.Always);
    //        if (!String.IsNullOrEmpty(paragraphs[i]))
    //        {
    //            try
    //            {
    //                //await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}").ConfigureAwait(true);
    //                await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}");
    //            }
    //            catch (Exception ex)
    //            {
    //                WintapLogger.Log.Append($"ERROR get embeddings failed on paragraph {i}, msg: {ex.Message}", LogLevel.Always);
    //            }
    //        }
    //        else
    //        {
    //            WintapLogger.Log.Append($"Skipping empty paragraph!", LogLevel.Always);
    //        }
    //    }
    //    WintapLogger.Log.Append($"all embeddings saved.", LogLevel.Always);
    //}
    //catch (Exception ex)
    //{
    //    WintapLogger.Log.Append("Error getting embeddings: " + ex.Message, gov.llnl.wintap.core.infrastructure.LogLevel.Always);
    //}

}

WintapLogger.Log.Append($"RAG initialization complete.", LogLevel.Always);

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



// Use routing and map controller routes
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
    //endpoints.MapHub<ExplorerHub>("/api/ExplorerHub");
    //endpoints.MapHub<WorkbenchHub>("/api/WorkbenchHub");
    endpoints.MapHub<InferenceHub>("/signalr/inferenceHub");
});

app.Run();  // Runs the application


