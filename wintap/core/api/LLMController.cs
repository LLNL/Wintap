
#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050;

using com.espertech.esper.compat.collections;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.Properties;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Connectors.OpenAI;
//using Microsoft.SemanticKernel;
//using Microsoft.SemanticKernel.ChatCompletion;
//using Microsoft.SemanticKernel.Memory;
//using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel.Embeddings;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.ServiceModel.Channels;
using System.Speech.Synthesis;
using System.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Configuration;
using WinTAP.Properties;



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
        private IMcpClient mcpClient;
        private IChatClient chatClient;
        private List<ChatMessage> chatHistory;

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

            // system prompt override
            if(question == "An application recently stopped working, can you help me diagnose it?")
            {
                string existingPrompt = chatHistory.First().Text;
                chatHistory.RemoveAt(0);
                chatHistory.Add(new ChatMessage(ChatRole.System, existingPrompt));
            }

            IList<McpClientTool> tools = await mcpClient.ListToolsAsync();
            // Create the message list with our time question
            ChatMessage userQuestion = new ChatMessage(ChatRole.User, question);
            chatHistory.Add(userQuestion);

            try
            {
                var chatOptions = new ChatOptions
                {
                    Tools = [.. tools], // Make MCP tools available to the model
                    Temperature = 1.0f,
                    // Remove AllowMultipleToolCalls entirely
                    ToolMode = ChatToolMode.Auto
                };

                if (chatOptions.AdditionalProperties == null)
                {
                    chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary()
                    {
                        ["reasoning_effort"] = "minimal"  // Set to desired level: minimal, low, medium, or high
                    };
                }
                chatOptions.AdditionalProperties["disabled_params"] = new Dictionary<string, object>
                {
                    ["parallel_tool_calls"] = null
                };


                var response = await chatClient.GetResponseAsync(
                    chatHistory,
                    chatOptions
                );

                Inference inf = new Inference() { Prompt = question, Response = response.Text, TokensUsed = 0 };
                string jsonString = JsonConvert.SerializeObject(inf);
                await this.hubContext.Clients.All.SendAsync("ReceiveMessage", inf, "OK");
                chatHistory.Add(new ChatMessage(ChatRole.Assistant, response.Messages[0].Text));
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error on inference: {ex.Message}", LogLevel.Error);
            }

            WintapLogger.Log.Append($"inference complete", LogLevel.Info);
        }

        [HttpPost("Clear")]
        public void Post()
        {
            WintapLogger.Log.Append($"LLM Clear method called", LogLevel.Info);

            OpenAIClientOptions openAIOptions = new OpenAIClientOptions();
            openAIOptions = new OpenAIClientOptions() { Endpoint = new Uri("https://livai-api-dev.llnl.gov/v1") };

            string? key = "";
            key = System.IO.File.ReadAllText(Path.Combine(Strings.FileDataRoot, "ai", "api-key.txt"));

            ApiKeyCredential cred = new ApiKeyCredential(key!);
            var openAIClient = new OpenAIClient(cred, openAIOptions).GetChatClient("gpt-4.1");

            // Create a sampling client.
            using IChatClient chatClient = openAIClient.AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();

            List<ChatMessage> chatHistory = [new ChatMessage(ChatRole.System, System.IO.File.ReadAllText(Path.Combine(Strings.FileRootPath, "systemprompt.txt"))),];
        }
    }

    public class PromptModel
    {
        public string Prompt { get; set; }
    }

}


