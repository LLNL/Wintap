/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */
#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050;

using System;
using System.ComponentModel;
using System.ServiceProcess;
using System.IO;
using Microsoft.Win32;
using gov.llnl.wintap.Properties;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.api;
using gov.llnl.wintap.core.shared;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using LogLevel = gov.llnl.wintap.core.infrastructure.LogLevel;
using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using gov.llnl.wintap.core.etl;
using Org.BouncyCastle.Asn1.Pkcs;

namespace gov.llnl.wintap
{
    /// <summary>
    /// Windows Service main entry point.
    /// </summary>
    public partial class WinTapSvc : BackgroundService
    {

        private PluginManager pluginMgr;
        private SubscriptionManager subscriptionMgr;
        private string[] args;

        public WinTapSvc(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory.CreateLogger<WinTapSvc>();
        }

        public ILogger Logger { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            WintapLogger.Log.Append("Creating startup thread.", LogLevel.Always);

            //await test();
            


            BackgroundWorker startupWorker = new BackgroundWorker();
            startupWorker.DoWork += startupWorker_DoWork;
            startupWorker.RunWorkerAsync();
            WintapLogger.Log.Append("Startup method complete.", core.infrastructure.LogLevel.Always);

        }
        
        private async Task test()
        {
            string rag_data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap", "ragdata", "ragdata.txt");

            var kernelBuilder = Kernel.CreateBuilder();
            var kernel = kernelBuilder.AddOpenAIChatCompletion(modelId: "phi3", apiKey: null, endpoint: new Uri("http://127.0.0.1:11434"))
                .Build();

            HttpClient httpClient = new HttpClient();
            httpClient.Timeout = new TimeSpan(0, 5, 0);

            Console.WriteLine("Generating embeddings...");
            ISemanticTextMemory memory = new MemoryBuilder()
                .WithLoggerFactory(kernel.LoggerFactory)
                .WithMemoryStore(new VolatileMemoryStore())
                .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory)) // Replace with your Ollama API URL
                .Build();

            string collectionName = "testCollection";
            string s = System.IO.File.ReadAllText(rag_data);
            List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(TextChunker.SplitPlainTextLines(s, 128), 256);
            try
            {
                for (int i = 0; i < paragraphs.Count; i++)
                {
                    await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}");
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error getting embeddings: " + ex.Message, core.infrastructure.LogLevel.Always);
            }



            IChatCompletionService ai = kernel.GetRequiredService<IChatCompletionService>();

            ChatHistory chat = new("You are an I that helps users understand the shipping status of orders.");

            string question = "Jane Smith";
            StringBuilder builder = new StringBuilder();
            await foreach (MemoryQueryResult result in memory.SearchAsync(collectionName, question, 3, 0, withEmbeddings: true))
            {
                builder.AppendLine(result.Metadata.Text);
            }
            int contextToRemove = -1;
            if (builder.Length != 0)
            {
                builder.Insert(0, "Here's some additional information: ");
                contextToRemove = chat.Count;
                chat.AddUserMessage(builder.ToString());
            }
            chat.AddUserMessage(question);
            builder.Clear();
            await foreach (StreamingChatMessageContent message in ai.GetStreamingChatMessageContentsAsync(chat))
            {
                Console.Write(message);
                builder.Append(message.Content);
            }
            chat.AddAssistantMessage(builder.ToString());
           
        }


        //protected override void OnStop()
        //{
        //    WintapLogger.Log.Append("Stop command received.  Attempting to shutdown plugins", LogLevel.Always);
        //    try
        //    {
        //        RegistryKey wintapKey = Registry.LocalMachine.CreateSubKey(Strings.RegistryRootPath);
        //        wintapKey.SetValue("LastRestart", DateTime.Now, RegistryValueKind.String);
        //        wintapKey.SetValue("WatchdogRestart", Watchdog.PerformanceBreach, RegistryValueKind.DWord);
        //        wintapKey.Flush();
        //        wintapKey.Close();
        //        wintapKey.Dispose();
        //        try
        //        {
        //            pluginMgr.UnregisterPlugins();
        //        }
        //        catch (Exception ex)
        //        {
        //            WintapLogger.Log.Append("exception in plugin shutdown: " + ex.Message, LogLevel.Always);
        //        }
        //        subscriptionMgr.Stop();
        //    }
        //    catch (Exception ex)
        //    {
        //        WintapLogger.Log.Append("Error in shutdown: " + ex.Message, LogLevel.Always);
        //    }
        //    StreamsController.Stop();
        //    WintapLogger.Log.Append("Shutdown complete.", LogLevel.Always);
        //    WintapLogger.Log.Close();
        //}

        private void startupWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                Utilities.SetDirectoryPermissions(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wintap"));
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("NOT setting permissions on Wintap data path, cannot continue.  Message: " + ex.Message, LogLevel.Always);
            }


            WintapLogger.Log.Append("Wintap Agent ID: " + StateManager.AgentId.ToString(), LogLevel.Always);

            WintapLogger.Log.Append("loading plugin manager...", LogLevel.Always);
            pluginMgr = new PluginManager();

            WintapLogger.Log.Append("Creating performance monitor", LogLevel.Always);
            Watchdog watchdog = new Watchdog();
            try
            {
                WintapLogger.Log.Append("attempting to register plugins...", LogLevel.Always);
                pluginMgr.RegisterPlugins(watchdog);
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Exception loaderException in ex.LoaderExceptions)
                {
                    WintapLogger.Log.Append("Loader exception: " + loaderException.ToString(), LogLevel.Always);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error loading plugin: " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("workbench config value: " + Properties.Settings.Default.EnableWorkbench, LogLevel.Always);
            if (Properties.Settings.Default.EnableWorkbench)
            {
                WintapLogger.Log.Append("Starting Workbench", LogLevel.Always);
                startWorkbench(args);
            }

            // ETW rundown to resolve file paths.  TODO:  only do if FILE events are enabled.
            WintapLogger.Log.Append("NOT Doing ETW File path rundown", LogLevel.Always);
            //ProcessStartInfo rundownPsi = new ProcessStartInfo();
            //rundownPsi.FileName = Strings.FileRootPath + "\\WintapSvcMgr.exe";
            //rundownPsi.Arguments = "RUNDOWN";
            //System.Diagnostics.Process rundown = new Process();
            //rundown.StartInfo = rundownPsi;
            //rundown.Start();
            //rundown.WaitForExit();


            System.Threading.Thread.Sleep(5000);  // allow plugins to init



            try
            {
                WintapLogger.Log.Append("Starting Wintap collectors", LogLevel.Always);
                subscriptionMgr = new SubscriptionManager();
                subscriptionMgr.Start();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR starting event subscription manager: " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("Startup complete.", LogLevel.Always);
        }
        private void startWorkbench(string[] args)
        {
            WintapLogger.Log.Append("extracting workbench", LogLevel.Always);
            try
            {

                string wintapDir = Strings.FileRootPath + "\\";
                DirectoryInfo workbenchInfo = new DirectoryInfo(wintapDir + "\\Workbench");
                if (!workbenchInfo.Exists)
                {
                    workbenchInfo.Create();
                    WintapLogger.Log.Append("extraction path: " + wintapDir, LogLevel.Always);
                    System.IO.Compression.ZipFile.ExtractToDirectory(wintapDir + "workbench.zip", wintapDir);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("error in workbench extraction: " + ex.Message, LogLevel.Always);
            }

            StreamsController.LoadInteractiveQueries();  // load from disk
            string baseAddress = "http://127.0.0.1:" + Properties.Settings.Default.ApiPort + "/";

            try
            {
                // previous location for starting Owin
                //CreateHostBuilder(args).Build().Run();

            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error starting local web api: " + ex.Message, LogLevel.Always);
            }
            WintapLogger.Log.Append("accepting connections at: " + baseAddress, LogLevel.Always);
        }

    //    public static IHostBuilder CreateHostBuilder(string[] args) =>
    //    Host.CreateDefaultBuilder(args)
    //        .ConfigureWebHostDefaults(webBuilder =>
    //        {
    //            webBuilder.UseStartup<Startup>();
    //        })
    //        .UseWindowsService(options =>
    //        {
    //            options.ServiceName = "Wintap";
    //        });


    }
}
