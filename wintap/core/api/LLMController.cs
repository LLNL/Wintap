
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
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel.Embeddings;
using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.Extensions.Logging;
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
using LogLevel = gov.llnl.wintap.core.infrastructure.LogLevel;
using Microsoft.AspNetCore.Http;
using System.IO;

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
        private ISemanticTextMemory memory;
        private ChatHistory chat;
        private IChatCompletionService ai;

        public LLMController(IHubContext<InferenceHub> _hubContext, ISemanticTextMemory _memory, ChatHistory _chat, IChatCompletionService _ai)
        {
            this.hubContext = _hubContext;
            memory = _memory;
            chat = _chat;
            ai = _ai;
        }

        //[HttpGet]
        //public ChatHistory Get()
        //{
        //    ChatHistory chatHistory = new ChatHistory();
        //    chatHistory.AddMessage(AuthorRole.System, "You are an AI named Wintap, Wintap evaluates 2 samples of windows sensor data, the first is labeled #REFERENCE and represents a known good sample, the second is labeled #SAMPLE, the AI compares the sample to the reference and identifies any significant deviations between the two, keep answers short and specific.");
        //    chatHistory.AddMessage(AuthorRole.User, "Hello, Wintap.");
        //    chatHistory.AddMessage(AuthorRole.Assistant, "Hello. How may I help you today?");
        //    return chatHistory;
        //}

        [HttpPut("Inference")]
        public async Task Put([FromBody] PromptModel promptModel)
        {
            WintapLogger.Log.Append($"Got inference request", LogLevel.Always);
            string collectionName = "wintap2";
            string question = promptModel.Prompt;
            StringBuilder builder = new StringBuilder();
            double minRel = Settings.Default.MinRelevance;
            //minRel = 0.3;

            WintapLogger.Log.Append($"attempting to retrieve RAG inference from ollama endpoint", LogLevel.Always);
            try
            {
                WintapLogger.Log.Append($"collectionName: {collectionName}, question: {question}, minRel: {minRel}", LogLevel.Always);
                await foreach (MemoryQueryResult result in memory.SearchAsync(collectionName, question, 3, minRel, withEmbeddings: true))
                {
                    builder.AppendLine(result.Metadata.Text);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"ERROR in RAG inference request: {ex.Message}", LogLevel.Always);
            }
            int contextToRemove = -1;

            if (builder.Length != 0)
            {
                builder.Insert(0, "Here's some additional information: ");
                contextToRemove = chat.Count;
                chat.AddUserMessage(builder.ToString());
                WintapLogger.Log.Append(builder.ToString(), LogLevel.Always);
            }
            WintapLogger.Log.Append($"Additional info from RAG: ${builder.Length}", LogLevel.Always);
            chat.AddUserMessage(question);
            builder.Clear();
            await foreach (StreamingChatMessageContent message in ai.GetStreamingChatMessageContentsAsync(chat))
            {
                Inference inf = new Inference() { Prompt = question, Response = message.ToString(), TokensUsed = 0 };
                string jsonString = JsonConvert.SerializeObject(inf);
                await this.hubContext.Clients.All.SendAsync("ReceiveMessage", inf, "OK");
                builder.Append(message.Content);
            }

            chat.AddAssistantMessage(builder.ToString());
            if (contextToRemove >= 0)
            {
                chat.RemoveAt(contextToRemove);
            }
            WintapLogger.Log.Append($"inference complete", LogLevel.Always);
        }

        [HttpPost("Clear")]
        public void Post()
        {
            chat.RemoveRange(0, chat.Count);
            string systemPrompt = Settings.Default.SystemPrompt;
            chat = new ChatHistory(systemPrompt);
        }

        [HttpPost("Upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            WintapLogger.Log.Append($"Document embeddings request received", LogLevel.Always);
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            string collectionName = "contextData";

            WintapLogger.Log.Append("LLM Upload is attempting to read file", LogLevel.Always);
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;
                using (var reader = new StreamReader(stream))
                {
                    string fileContent = await reader.ReadToEndAsync();
                    WintapLogger.Log.Append($"Got file text, Splitting lines...", LogLevel.Always);
                    List<string> lines = TextChunker.SplitPlainTextLines(fileContent, 128);
                    int chunkSize = 1500;
                    int overlapSize = 100;
                    WintapLogger.Log.Append($"Line count: {lines.Count}, Splitting paragraphs...", LogLevel.Always);
                    List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, chunkSize, overlapSize, " ");
                    try
                    {
                        for (int i = 0; i < paragraphs.Count; i++)
                        {
                            if (!string.IsNullOrEmpty(paragraphs[i]))
                            {
                                try
                                {
                                    await memory.SaveInformationAsync(collectionName, paragraphs[i], $"paragraph{i}");

                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"ERROR get embeddings failed on paragraph {i}, msg: {ex.Message}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Skipping empty paragraph!");
                            }
                        }
                        WintapLogger.Log.Append($"RAG embeddings successfully saved.", LogLevel.Always);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error saving embeddings: " + ex.Message);
                    }
                }
            }

            return Ok();
        }
    }

    public class PromptModel
    {
        public string Prompt { get; set; }
    }

}
