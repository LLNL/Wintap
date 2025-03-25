#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020, SKEXP0070;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // URL for your local Ollama instance
        string ollamaEndpoint = "http://localhost:11434";

        // The model name as configured in Ollama
        string modelName = "gemma3:1b";

        // Configure the kernel with Ollama
        var builder = Kernel.CreateBuilder();
        builder.AddOllamaTextGeneration(modelName, new Uri(ollamaEndpoint));
        var kernel = builder.Build();

        // Get the text generation service
        var textService = kernel.GetRequiredService<ITextGenerationService>();

        // Create OpenAI settings that Ollama will use
        var openAISettings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.2,
            MaxTokens = 500
        };

        Console.WriteLine("Gemma 3 Streaming Chat (Type 'exit' to quit)");
        Console.WriteLine("-------------------------------------------");

        while (true)
        {
            Console.Write("\nYour prompt: ");
            string userPrompt = Console.ReadLine();

            if (userPrompt.ToLower() == "exit")
                break;

            Console.WriteLine("\nResponse:");

            // Stream the response with OpenAI settings
            await foreach (var chunk in textService.GetStreamingTextContentsAsync(
                userPrompt,
                openAISettings))
            {
                Console.Write(chunk);
            }

            Console.WriteLine("\n");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}