#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020;

using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Chroma;
using Microsoft.SemanticKernel.Memory;
using System.Text;

namespace gov.llnl.wintap.utilities.ai;
internal class Program
{
    private static async Task Main(string[] args)
    {

        if (args.Length == 0)
        {
            Console.WriteLine("usage: LocalInference.exe <collectionName> <relevanceThreshold> <question>");
        }
        var kernelBuilder = Kernel.CreateBuilder();
        var kernel = kernelBuilder.AddOpenAIChatCompletion(modelId: "llama3.1:8b-instruct-q2_K", apiKey: null, endpoint: new Uri("http://127.0.0.1:11434"))
            .Build();

        string systemPrompt = "You are a helpful AI";
        ChatHistory chat = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(systemPrompt);

        IChatCompletionService ai = kernel.GetRequiredService<IChatCompletionService>();

        string question = "Why is the sky blue?";

        StringBuilder builder = new StringBuilder();

        chat.AddUserMessage(question);
        builder.Clear();
        await foreach (StreamingChatMessageContent message in ai.GetStreamingChatMessageContentsAsync(chat))
        {
            builder.Append(message.Content);
        }

        Console.WriteLine("Inference: " + builder.ToString());

        chat.AddAssistantMessage(builder.ToString());

        Console.WriteLine("All Done! " + builder.ToString());
    }

}