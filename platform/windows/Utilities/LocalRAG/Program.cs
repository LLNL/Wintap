#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020, SKEXP0070;

using Microsoft.SemanticKernel.Connectors.Chroma;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel;
using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Text;
using System.Collections;

namespace LocalRAG
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            string docPath = @"C:\data\code\test";
            string collectionName = "Wintap";

            var kernelBuilder = Kernel.CreateBuilder();
            var kernel = kernelBuilder.AddOllamaChatCompletion(modelId: "gemma3:1b", endpoint: new Uri("http://127.0.0.1:11434")).Build();

            HttpClient httpClient = new HttpClient();
            httpClient.Timeout = new TimeSpan(0, 5, 0);

            //string rag_data = "C:\\programdata\\wintap\\ragdata.txt";
            //WintapLogger.Log.Append($"Attempting to load RAG data from file: {rag_data}", LogLevel.Info);

            // use a persistent memory store:
            var chromaMemoryStore = new ChromaMemoryStore("http://127.0.0.1:8000");
            ISemanticTextMemory memory = new MemoryBuilder()
                .WithLoggerFactory(kernel.LoggerFactory)
                .WithMemoryStore(chromaMemoryStore)
                .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory)) // Replace with your Ollama API URL
                .Build();


            IList<string> allCollections = await memory.GetCollectionsAsync();
            Console.WriteLine(allCollections.Count);

            var memoryResultCollection = memory.SearchAsync(collectionName, query: "What is Wintap?", limit: 1, minRelevanceScore: 0);
            var doc = memoryResultCollection.ToBlockingEnumerable().SingleOrDefault();
            foreach (var memResult in memoryResultCollection.ToBlockingEnumerable())
            {
                Console.WriteLine(memResult.Metadata.Text);
            }
            

            // 68a2cdf0-e634-4ae2-b557-9bf5eae1cf3b
            int j = 0;



            Console.WriteLine("all done!!");

        }
    }
}
