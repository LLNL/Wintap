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
        var kernel = kernelBuilder.AddOpenAIChatCompletion(modelId: "phi3", apiKey: null, endpoint: new Uri("http://127.0.0.1:11434"))
            .Build();

        string systemPrompt = "You are a helpful AI assistant that helps people understand the software installed on their machines";
        ChatHistory chat = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(systemPrompt);

        IChatCompletionService ai = kernel.GetRequiredService<IChatCompletionService>();

        HttpClient httpClient = new HttpClient();
        httpClient.Timeout = new TimeSpan(0, 5, 0);

        string collectionName = args[0];
        StringBuilder q = new StringBuilder();
        for (int i = 2; i < args.Length; i++)
        {
            q.Append(args[i] + " ");

        }
        string question = "Do I have any Cisco products installed?";
        question = q.ToString();

        StringBuilder builder = new StringBuilder();
        double minRel = Convert.ToDouble(args[1]);

        Console.WriteLine($"collection: {collectionName} relevance: {minRel} question: {q}");

        var chromaMemoryStore = new ChromaMemoryStore("http://127.0.0.1:8000");
        // then use chromaMemoryStore in WithMemoryStore

        ISemanticTextMemory memory = new MemoryBuilder()
            .WithLoggerFactory(kernel.LoggerFactory)
            .WithMemoryStore(chromaMemoryStore)
            .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory))
            .Build();

        try
        {
            await foreach (MemoryQueryResult result in memory.SearchAsync(collectionName, question, 3, minRel, withEmbeddings: true))
            {
                builder.AppendLine(result.Metadata.Text);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in RAG inference request: {ex.Message}");
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
            builder.Append(message.Content);
        }

        Console.WriteLine("Inference: " + builder.ToString());

        chat.AddAssistantMessage(builder.ToString());
        if (contextToRemove >= 0)
        {
            chat.RemoveAt(contextToRemove);
        }

        Console.WriteLine("All Done!");
    }

}