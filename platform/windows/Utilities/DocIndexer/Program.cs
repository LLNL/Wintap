#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020;

using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Chroma;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;

namespace gov.llnl.wintap.utilities;
internal class Program
{
    private static async Task Main(string[] args)
    {
        if(args.Count() !=1)
        {
            Console.WriteLine("Parameter missing: collection name, example: DocIndexer.exe wintap");
            return;
        }
        string collectionName = args[0];
        Console.WriteLine("  *****************");
        Console.WriteLine("Document Indexer utility");
        Console.WriteLine($"     collection name for this indexing session: {collectionName}");
        Console.WriteLine("  *****************");
        Console.WriteLine();

        string docPath = @"C:\data\docs";


        var kernelBuilder = Kernel.CreateBuilder();
        var kernel = kernelBuilder.AddOpenAIChatCompletion(modelId: "phi3", apiKey: null, endpoint: new Uri("http://127.0.0.1:11434")).Build();

        HttpClient httpClient = new HttpClient();
        httpClient.Timeout = new TimeSpan(0, 5, 0);

        Console.WriteLine($"Attempting to load RAG data from file: {docPath}");

        var chromaMemoryStore = new ChromaMemoryStore("http://127.0.0.1:8000");

        ISemanticTextMemory memory = new MemoryBuilder()
            .WithLoggerFactory(kernel.LoggerFactory)
            .WithMemoryStore(chromaMemoryStore)
            .WithTextEmbeddingGeneration(new OllamaTextEmbeddingGeneration("nomic-embed-text", "http://127.0.0.1:11434", httpClient, kernel.LoggerFactory)) // Replace with your Ollama API URL
            .Build();

        DirectoryInfo indexDir = new DirectoryInfo(docPath);
        foreach(FileInfo doc in indexDir.GetFiles())
        {
            Console.WriteLine($"Chunking data: {docPath}");
            string s = File.ReadAllText(doc.FullName);
            List<string> lines = TextChunker.SplitPlainTextLines(s, 128);
            int chunkSize = 1500;
            int overlapSize = 100; 
            List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, chunkSize, overlapSize, " ");
            try
            {
                Console.WriteLine($"data chunked into {paragraphs.Count} paragraphs. Get embeddings...");
                for (int i = 0; i < paragraphs.Count; i++)
                {
                    Console.WriteLine($"paragraph {i} has a length of {paragraphs[i].Length}");
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
                Console.WriteLine($"all embeddings saved.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving embeddings: " + ex.Message);
            }
        }
        Console.WriteLine("all done!!");
    }
}