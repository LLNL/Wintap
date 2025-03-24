using ChromaDB.Client;

namespace LocalRAG
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            var configOptions = new ChromaConfigurationOptions(uri: "http://localhost:8000/api/v1/");
            using var httpClient = new HttpClient();
            var client = new ChromaClient(configOptions, httpClient);


        }
    }
}
