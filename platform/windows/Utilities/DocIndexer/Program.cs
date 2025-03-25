#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020, SKEXP0070;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
// Add the correct namespaces for your version
using Microsoft.SemanticKernel.Connectors.Sqlite;

// You may need these instead:
using Microsoft.SemanticKernel.Memory;

namespace EmbeddingGenerator
{
    class Program
    {
        // Configuration settings
        private const string SourceDirectory = @"c:\data\code\test\";
        private const string DatabasePath = "embeddings.db";
        private const string CollectionName = "Wintap";
        private const string OllamaEndpoint = "http://localhost:11434";
        private const string EmbeddingModel = "mxbai-embed-large"; // Your local model name

        static async Task Main(string[] args)
        {
            Console.WriteLine("Embedding Generator for SQLite using Ollama Direct API");
            Console.WriteLine("-----------------------------------------------------");
            Console.WriteLine($"Source directory: {SourceDirectory}");
            Console.WriteLine($"Database path: {DatabasePath}");
            Console.WriteLine($"Ollama endpoint: {OllamaEndpoint}");
            Console.WriteLine($"Collection name: {CollectionName}");
            Console.WriteLine($"Embedding model: {EmbeddingModel}");
            Console.WriteLine();

            try
            {
                // Create HTTP client for Ollama API
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5);

                // Initialize SqliteMemoryStore
                Console.WriteLine("Setting up SQLite memory store");
                var memoryStore = await SqliteMemoryStore.ConnectAsync(DatabasePath);

                // Process all files in the directory
                await ProcessFilesAsync(memoryStore, client, SourceDirectory);

                Console.WriteLine("Embedding generation completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static async Task ProcessFilesAsync(IMemoryStore memoryStore, HttpClient client, string directory)
        {
            // Get all text files in the directory
            var textFiles = Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories);
            var codeFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
            var mdFiles = Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories);

            var allFiles = new string[textFiles.Length + codeFiles.Length + mdFiles.Length];
            textFiles.CopyTo(allFiles, 0);
            codeFiles.CopyTo(allFiles, textFiles.Length);
            mdFiles.CopyTo(allFiles, textFiles.Length + codeFiles.Length);

            Console.WriteLine($"Found {allFiles.Length} files to process.");

            int processedCount = 0;
            int errorCount = 0;

            foreach (var file in allFiles)
            {
                try
                {
                    string content = await File.ReadAllTextAsync(file);
                    if (string.IsNullOrEmpty(content))
                    {
                        Console.WriteLine($"Skipping empty file: {file}");
                        continue;
                    }

                    // Break content into chunks if too large
                    const int MaxChunkSize = 4000;
                    if (content.Length > MaxChunkSize)
                    {
                        int chunkCount = (content.Length / MaxChunkSize) + 1;
                        Console.WriteLine($"File too large, splitting into {chunkCount} chunks: {file}");

                        for (int i = 0; i < chunkCount; i++)
                        {
                            int startIndex = i * MaxChunkSize;
                            int length = Math.Min(MaxChunkSize, content.Length - startIndex);

                            if (length <= 0) break;

                            string chunk = content.Substring(startIndex, length);
                            string chunkId = $"{Path.GetFileName(file)}_chunk_{i + 1}";

                            await SaveDocumentToMemoryAsync(memoryStore, client, chunkId, file, chunk);
                            processedCount++;
                        }
                    }
                    else
                    {
                        // Save the entire file content
                        string id = Guid.NewGuid().ToString();
                        await SaveDocumentToMemoryAsync(memoryStore, client, id, file, content);
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing file {file}: {ex.Message}");
                    errorCount++;
                }
            }

            Console.WriteLine($"Processing complete: {processedCount} documents processed successfully, {errorCount} errors.");
        }

        private static async Task SaveDocumentToMemoryAsync(IMemoryStore memoryStore, HttpClient client, string id, string source, string content)
        {
            Console.WriteLine($"Processing: {Path.GetFileName(source)}");

            try
            {
                // Create a request body for Ollama embeddings API
                var request = new OllamaEmbeddingRequest
                {
                    Model = EmbeddingModel,
                    Prompt = content
                };

                // Send to Ollama API
                var response = await client.PostAsJsonAsync($"{OllamaEndpoint}/api/embeddings", request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException($"Ollama API returned error status code {response.StatusCode}: {errorContent}");
                }

                // Parse the response
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(responseContent);

                if (jsonResponse?.Embedding == null || jsonResponse.Embedding.Length == 0)
                {
                    throw new InvalidOperationException("Received empty embedding from Ollama API");
                }

                Console.WriteLine($"Successfully generated embedding with {jsonResponse.Embedding.Length} dimensions");

                // Convert to ReadOnlyMemory<float> for MemoryStore
                var embeddingMemory = new ReadOnlyMemory<float>(jsonResponse.Embedding);

                // Create a metadata JSON
                var metadataObj = new
                {
                    source = source,
                    id = id
                };
                string metadataJson = JsonSerializer.Serialize(metadataObj);

                // Use the FromJsonMetadata factory method instead of properties
                var memoryRecord = MemoryRecord.FromJsonMetadata(
                    json: JsonSerializer.Serialize(new
                    {
                        isReference = false,
                        id = id,
                        text = content,
                        description = $"Document from {source}",
                        externalSourceName = source,
                        additionalMetadata = metadataJson
                    }),
                    embedding: embeddingMemory,
                    key: id,
                    timestamp: DateTimeOffset.UtcNow
                );

                Console.WriteLine($"Attempting to store in memory collection '{CollectionName}' with ID '{id}'");

                // Store in memory
                await memoryStore.UpsertAsync(
                    CollectionName,
                    memoryRecord
                );

                Console.WriteLine($"Successfully saved: {Path.GetFileName(source)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {Path.GetFileName(source)}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }
    }

    // Classes for Ollama API requests and responses
    public class OllamaEmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; }
    }

    public class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; }
    }
}