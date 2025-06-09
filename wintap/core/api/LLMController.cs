
#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using gov.llnl.wintap.core.shared;
using System.Threading;
//using Microsoft.SemanticKernel;
//using Microsoft.SemanticKernel.ChatCompletion;
//using Microsoft.SemanticKernel.Memory;
//using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel.Embeddings;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Net.Http.Json;
using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using gov.llnl.wintap.core.infrastructure;
using Newtonsoft.Json;
using com.espertech.esper.compat.collections;
using gov.llnl.wintap.Properties;
using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Speech.Synthesis;
using WinTAP.Properties;
using System.ComponentModel;
using ModelContextProtocol.Client;
using Microsoft.Extensions.AI;
using System.ServiceModel.Channels;



namespace gov.llnl.wintap.core.api
{
    public class Inference
    {
        public string Prompt { get; set; }
        public string Response { get; set; }
        public int TokensUsed { get; set; }
    }

    public class InferenceHub : Hub
    {

        public async Task Send(Inference inference)
        {
            StateManager.LastWorkbenchActivity = DateTime.Now;
            await Clients.All.SendAsync("ReceiveMessage", inference);
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class LLMController : ControllerBase
    {
        private readonly IHubContext<InferenceHub> hubContext;
        //private ISemanticTextMemory memory;
        //private ChatHistory chat;
        //private IChatCompletionService ai;
        //private Kernel kernel;

        private IMcpClient mcpClient;
        private IChatClient chatClient;
        private List<ChatMessage> chatHistory;

        //public LLMController(IHubContext<InferenceHub> _hubContext, ISemanticTextMemory _memory, ChatHistory _chat, IChatCompletionService _ai, Kernel _kernel)
        //{
        //    this.hubContext = _hubContext;
        //    memory = _memory;
        //    chat = _chat;
        //    ai = _ai;
        //    kernel = _kernel;
        //}

        public LLMController(IHubContext<InferenceHub> _hubContext, IMcpClient _mcpClient, IChatClient _chatClient, List<ChatMessage> _chatHistory)
        {
            this.hubContext = _hubContext;
            mcpClient = _mcpClient;
            chatClient = _chatClient;
            chatHistory = _chatHistory;
        }

        [HttpPut("Inference")]
        public async Task Put([FromBody] PromptModel promptModel)
        {
            string question = promptModel.Prompt;
            WintapLogger.Log.Append($"Got inference request with question: {question}", LogLevel.Info);
            IList<McpClientTool> tools = await mcpClient.ListToolsAsync();
            // Create the message list with our time question
            ChatMessage userQuestion = new ChatMessage(ChatRole.User, question);
            chatHistory.Add(userQuestion);

            try
            {
                var response = await chatClient.GetResponseAsync(
                    chatHistory,
                    new ChatOptions
                    {
                        Tools = [.. tools], // Make MCP tools available to the model
                        Temperature = 0.0f,
                        AllowMultipleToolCalls = true,
                        ToolMode = ChatToolMode.Auto
                    }
                );

                Inference inf = new Inference() { Prompt = question, Response = response.Text, TokensUsed = 0 };
                string jsonString = JsonConvert.SerializeObject(inf);
                await this.hubContext.Clients.All.SendAsync("ReceiveMessage", inf, "OK");
                chatHistory.Add(new ChatMessage(ChatRole.Assistant, response.Messages[0].Text));
            }
            catch (Exception ex)
            {
                int i = 0;
            }

            WintapLogger.Log.Append($"inference complete", LogLevel.Info);
        }

        //[HttpPut("Inference")]
        //public async Task OldPut([FromBody] PromptModel promptModel)
        //{
        //    WintapLogger.Log.Append($"Got inference request", LogLevel.Info);
        //    string collectionName = "Wintap";
        //    string question = promptModel.Prompt;
        //    StringBuilder builder = new StringBuilder();
        //    //double minRel = Settings.Default.MinRelevance;
        //    double minRel = 0.66;


        //    OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new OpenAIPromptExecutionSettings();
        //    openAIPromptExecutionSettings.Temperature = .2;
        //    openAIPromptExecutionSettings.MaxTokens = 20000;
        //    openAIPromptExecutionSettings.ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions;
        //    openAIPromptExecutionSettings.ToolCallBehavior = ToolCallBehavior.EnableKernelFunctions;
        //    openAIPromptExecutionSettings.FunctionChoiceBehavior = FunctionChoiceBehavior.Auto();


        //    var result = await kernel.InvokePromptAsync("what time is it?", new KernelArguments(openAIPromptExecutionSettings));
        //    await this.hubContext.Clients.All.SendAsync("ReceiveMessage", result, "OK");

        //    try
        //    {
        //        // Add this debugging code
        //        var collections = await memory.GetCollectionsAsync();
        //        foreach (var collection in collections)
        //        {
        //            WintapLogger.Log.Append($"Found collection: {collection}", LogLevel.Info);
        //            // Optionally, get and log a sample of records to verify content
        //            var sample = await memory.SearchAsync(collection, "*", limit: 2, minRelevanceScore: 0.7).ToListAsync();
        //            foreach (var item in sample)
        //            {
        //                WintapLogger.Log.Append($"Sample record - ID: {item.Metadata.Id}, Text length: {item.Metadata.Text.Length}", LogLevel.Info);
        //            }
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        WintapLogger.Log.Append($"Error checking collections: {ex.Message}", LogLevel.Error);
        //    }



        //    if (chat.Where(c => c.Role.Label == "assistant").Count() == 0)
        //    {
        //        //WintapLogger.Log.Append("Stuffing system prompt!!!!", LogLevel.Info);
        //        //string systemPrompt = "here is some additional information: ";
        //        //foreach (string ctxFilePath in Directory.GetFiles("C:\\data\\code\\test").ToList())
        //        //{
        //        //    systemPrompt += " \n\n" + System.IO.File.ReadAllText(ctxFilePath);
        //        //}
        //        //chat.AddUserMessage(systemPrompt);
        //        //WintapLogger.Log.Append($"attempting to retrieve RAG inference from ollama endpoint", LogLevel.Info);
        //        //try
        //        //{
        //        //    WintapLogger.Log.Append($"collectionName: {collectionName}, question: {question}, minRel: {minRel}", LogLevel.Info);
        //        //    int resultCount = 0;
        //        //    await foreach (MemoryQueryResult result in memory.SearchAsync(collectionName, question, 3, minRel, withEmbeddings: true))
        //        //    {
        //        //        resultCount++;
        //        //        WintapLogger.Log.Append($"Found match: Relevance={result.Relevance}", LogLevel.Info);
        //        //        builder.AppendLine(result.Metadata.Text);
        //        //    }
        //        //    WintapLogger.Log.Append($"Search complete. Found {resultCount} results", LogLevel.Info);
        //        //}
        //        //catch (Exception ex)
        //        //{
        //        //    WintapLogger.Log.Append($"ERROR in RAG inference request: {ex.Message}", LogLevel.Info);
        //        //}

        //    }


        //    int contextToRemove = -1;
        //    if (builder.Length != 0)
        //    {
        //        string contextMessage = $" Here's some additional information that may help you answer this question: {builder.ToString()}";
        //        WintapLogger.Log.Append($"Adding context to chat: {contextMessage}...", LogLevel.Info);
        //        contextToRemove = chat.Count;
        //        chat.AddUserMessage("USER: " + question + "\n\n" + " Retrieved data: " + contextMessage);
        //    }
        //    else
        //    {
        //        chat.AddUserMessage("USER: " + question + "\n\n");
        //    }
        //    //WintapLogger.Log.Append($"Additional info from RAG: {builder.ToString()}", LogLevel.Info);



        //    StringBuilder completeResponse = new StringBuilder();

        //    //await foreach (StreamingChatMessageContent message in ai.GetStreamingChatMessageContentsAsync(chat, openAIPromptExecutionSettings))
        //    //{
        //    //    try
        //    //    {
        //    //        completeResponse.Append(message.Content);
        //    //        Inference inf = new Inference() { Prompt = question, Response = message.Content, TokensUsed = 0 };
        //    //        string jsonString = JsonConvert.SerializeObject(inf);
        //    //        await this.hubContext.Clients.All.SendAsync("ReceiveMessage", inf, "OK");
        //    //    }
        //    //    catch (Exception ex) 
        //    //    {
        //    //        int i = 0;
        //    //    }
                
        //    //}

        //    // Add the complete response as a single message after streaming
        //    chat.AddAssistantMessage(completeResponse.ToString());

        //    builder.Clear();
        //    WintapLogger.Log.Append($"inference complete", LogLevel.Info);
        //}

        //[HttpPost("Clear")]
        //public void Post()
        //{
        //    WintapLogger.Log.Append($"LLM Clear method called", LogLevel.Info);
        //    chat.RemoveRange(0, chat.Count);
        //    //string systemPrompt = Settings.Default.SystemPrompt;
        //    //chat = new ChatHistory(systemPrompt);
        //}

        //[HttpPost("Upload")]
        //public async Task<IActionResult> Upload(IFormFile file)
        //{
        //    WintapLogger.Log.Append($"Document embeddings request received", LogLevel.Info);
        //    if (file == null || file.Length == 0)
        //    {
        //        return BadRequest("No file uploaded.");
        //    }

        //    // string collectionName = "contextData";
        //    string collectionName = "Wintap";

        //    WintapLogger.Log.Append("LLM Upload is attempting to read file", LogLevel.Info);
        //    using (var stream = new MemoryStream())
        //    {
        //        await file.CopyToAsync(stream);
        //        stream.Position = 0;
        //        using (var reader = new StreamReader(stream))
        //        {
        //            string fileContent = await reader.ReadToEndAsync();
        //            WintapLogger.Log.Append($"Got file text, Splitting lines...", LogLevel.Info);
        //            List<string> lines = TextChunker.SplitPlainTextLines(fileContent, 128);
        //            int chunkSize = 1500;
        //            int overlapSize = 100;
        //            WintapLogger.Log.Append($"Line count: {lines.Count}, Splitting paragraphs...", LogLevel.Info);
        //            List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, chunkSize, overlapSize, " ");
        //            try
        //            {
        //                for (int i = 0; i < paragraphs.Count; i++)
        //                {
        //                    if (!string.IsNullOrEmpty(paragraphs[i]))
        //                    {
        //                        try
        //                        {
        //                            await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}");

        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            Console.WriteLine($"ERROR get embeddings failed on paragraph {i}, msg: {ex.Message}");
        //                        }
        //                    }
        //                    else
        //                    {
        //                        Console.WriteLine($"Skipping empty paragraph!");
        //                    }
        //                }
        //                WintapLogger.Log.Append($"RAG embeddings successfully saved.", LogLevel.Info);
        //            }
        //            catch (Exception ex)
        //            {
        //                Console.WriteLine("Error saving embeddings: " + ex.Message);
        //            }
        //        }
        //    }

        //    return Ok();
        //}
    }

    public class PromptModel
    {
        public string Prompt { get; set; }
    }

}


