#pragma warning disable SKEXP0010, SKEXP0001, SKEXP0050, SKEXP0020;

using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Chroma;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace gov.llnl.wintap.utilities.ai;
internal class Program
{
    private static async Task Main(string[] args)
    {
        string tenableString = File.ReadAllText(@"c:\data\tenable\TenablePLugins.json");

        List<PluginInfo> pluginInfoList = JsonConvert.DeserializeObject<List<PluginInfo>>(tenableString);

        Console.WriteLine("Read all plugins, count: " + pluginInfoList.Count);

        var kernelBuilder = Kernel.CreateBuilder();
        var kernel = kernelBuilder.AddOpenAIChatCompletion(modelId: "llama3.1:70b", apiKey: null, endpoint: new Uri("http://10.217.7.126:11434")).Build();

        // no script
        //string systemPrompt = "You are an expert AI designed to help users understand and remediate vulnerabilities on their computers. Users will provide you with a JSON-formatted data sample containing the following fields:\r\n\r\npluginid: The Nessus Tenable Plugin ID, path: The path to the vulnerable file, solution: The recommended solution for the vulnerability, fixed: The version of the software that contains the fix, and cnt: the total number of systems in our organization affected by this vulnerability (valid values are 1-20000), you can use this value to provide a sense for how many other people are affected by this particular vulnerability. Your tasks are:\r\n\r\nGuidance on Applying Solutions: Provide detailed guidance on how to apply the recommended solution to remediate the vulnerability. Provide a single URL for Updates: Attempt to provide URLs to an updated version of the affected software. Alternative Action: If you cannot find a likely URL for the updated software, provide guidance on how to remove or archive the vulnerable files.  If you cannot find the necessary information, explain the steps the user can take to manually search for updates or handle the vulnerability. Example Input:\r\n\r\n{ \"plugin_id\": \"12345\", \"vulnerable_file_path\": \"/path/to/vulnerable/file\", \"recommended_solution\": \"Update to the latest version\", \"fixed_version\": \"2.3.4\" } Example Output:\r\n\r\nGuidance: \"To remediate the vulnerability, download and install the latest version of the software from the official website.\" URL: \"You can download the updated version 2.3.4 from [official_website_url].\" Alternative Action: \"If the update is not available, consider removing the file located at /path/to/vulnerable/file or archiving it to prevent exploitation.\"  You will format your response as JSON, with the following properties:  PluginId, Guidance, URL, and AlternativeAction. Only include a URL that you are confident in, if you don't have a URL that is likely to be correct, just return the word NONE as the URL value.";
        
        // with script code removal/update/archive
        string systemPrompt = "You are an expert AI designed to help users understand and remediate vulnerabilities on their computers. Users will provide you with a JSON-formatted data sample containing the following fields:\r\n\r\npluginid: The Nessus Tenable Plugin ID\r\npath: The path to the vulnerable file\r\nsolution: The recommended solution for the vulnerability\r\nfixed: The version of the software that contains the fix\r\ncnt: The total number of systems in our organization affected by this vulnerability (valid values are 1-20000), you can use this value to provide a sense for how many other people are affected by this particular vulnerability.\r\nYour tasks are:\r\n\r\nGuidance on Applying Solutions: Provide detailed guidance on how to apply the recommended solution to remediate the vulnerability.\r\nProvide a Single URL for Updates: Attempt to provide a URL to an updated version of the affected software. If the software is end-of-life, return 'NONE' as the URL.\r\nAlternative Action: If you cannot find a likely URL for the updated software, provide guidance on how to remove or archive the vulnerable application. If the target application is a Windows Store app, provide the script code to remove the target application using PowerShell's Remove-AppxPackage cmdlet. If the vulnerable file is not part of a Windows Store app, provide script code to uninstall the application or archive the vulnerable file path and password-protect the zip file using the string \"notapassword\". If you cannot find the necessary information, explain the steps the user can take to manually search for updates or handle the vulnerability.\r\nRemoveWindowsStoreAppScript: The PowerShell script to remove the Windows Store application using the Remove-AppxPackage cmdlet.\r\nUninstallApplication: For non-Windows Store apps, the script to uninstall the application. For MSI applications, include a script to pull the MSI Product Code from WMI and uninstall the application. For MacOS applications, provide guidance on how to remove the application.\r\nArchiveFileScript: The script to compress and password protect the vulnerable file.\r\nExample Input:\r\n\r\n{\r\n  \"pluginid\": \"12345\",\r\n  \"path\": \"/path/to/vulnerable/file\",\r\n  \"solution\": \"Update to the latest version\",\r\n  \"fixed\": \"2.3.4\",\r\n  \"cnt\": 1022\r\n}\r\nExample Output:\r\n\r\n{\r\n  \"PluginId\": \"12345\",\r\n  \"Guidance\": \"To remediate the vulnerability, download and install the latest version of the software from the official website.\",\r\n  \"URL\": \"You can download the updated version 2.3.4 from [official_website_url].\",\r\n  \"AlternativeAction\": \"If the update is not available, consider removing the file located at /path/to/vulnerable/file or archiving it to prevent exploitation.\",\r\n  \"RemoveWindowsStoreAppScript\": \"Remove-AppxPackage -Name 'AppName'\",\r\n  \"UninstallApplication\": \"Get-WmiObject -Query \\\"SELECT * FROM Win32_Product WHERE Name = 'AppName'\\\" | ForEach-Object { $_.Uninstall() }\",\r\n  \"ArchiveFileScript\": \"Compress-Archive -Path '/path/to/vulnerable/file' -DestinationPath '/path/to/archive.zip'; Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip = [System.IO.Compression.ZipFile]::Open('/path/to/archive.zip', 'Update'); $entry = $zip.Entries | Where-Object { $_.FullName -eq 'vulnerable/file' }; $entry.Password = 'notapassword'; $zip.Dispose()\"\r\n}\r\nFor MacOS applications, you can provide guidance such as:\r\n\r\nTo remove a MacOS application, drag the application from the Applications folder to the Trash, then empty the Trash.\r\nAlternatively, you can use the rm command in the terminal to remove the application and its associated files.\r\nYou will format your response as JSON, with the following properties: PluginId, Guidance, URL, AlternativeAction, RemoveWindowsStoreAppScript, UninstallApplication, and ArchiveFileScript. Only include a URL that you are confident in; if you don't have a URL that is likely to be correct, just return the word NONE as the URL value.";
        ChatHistory chat = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(systemPrompt);

        IChatCompletionService ai = kernel.GetRequiredService<IChatCompletionService>();

        HttpClient httpClient = new HttpClient();
        httpClient.Timeout = new TimeSpan(0, 5, 0);

        string collectionName = "tenable";
        string outputFile = @"c:\data\tenable\PluginGuidance.json";
        File.Create(outputFile);
        
        StringBuilder builder = new StringBuilder();

        int counter = 0;
        int maxRecords = 50;
        int contextToRemove = -1;
        int lastProcessed = 205012;
        bool startProcessing = true;

        StringBuilder finalOutput = new StringBuilder();

        List<TenableHelper> tenableHelpers = new List<TenableHelper>();

        foreach(PluginInfo plugin in pluginInfoList.OrderByDescending(p => p.Cnt))
        {
            builder.Clear();
            if (counter == maxRecords) { break; }
            //if (plugin.PluginId == lastProcessed)
            //{
            //    startProcessing = true;
            //    continue;
            //}
            if (!startProcessing) 
            {
                Console.WriteLine("Skipping plugin: " + plugin.PluginId);
                continue; 
            }
            
            Console.WriteLine($"{counter} of {maxRecords}");
            Stopwatch stopwatch = Stopwatch.StartNew();
            string question = JsonConvert.SerializeObject(plugin);
            chat.AddUserMessage(question);
            OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new OpenAIPromptExecutionSettings();
            openAIPromptExecutionSettings.Temperature = 0.2;
            await foreach (StreamingChatMessageContent message in ai.GetStreamingChatMessageContentsAsync(chat, openAIPromptExecutionSettings))
            {
                
                builder.Append(message.Content);
            }

            chat.AddAssistantMessage(builder.ToString());
            if (contextToRemove >= 0)
            {
                chat.RemoveAt(contextToRemove);
            }
            Console.WriteLine(builder.ToString());

            try
            {
                TenableHelper th = parseTenableHelper(builder.ToString());
                tenableHelpers.Add(th);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"ERROR parsing json for Plugin ID: {plugin.PluginId}, msg: {ex.Message} ");
            }

            Console.WriteLine("Inference complete, time: " + stopwatch.Elapsed.ToString());
            builder.Clear();
            counter++;
        }

        //await File.AppendAllTextAsync(outputFile, JsonConvert.SerializeObject(pluginInfoList));
        await File.AppendAllTextAsync(outputFile, JsonConvert.SerializeObject(tenableHelpers));
        Console.WriteLine("All Done!");
    }

    private static TenableHelper parseTenableHelper(string input)
    {

        string pattern = @"\{[\s\S]*\}";
        Match match = Regex.Match(input, pattern);
        string json = match.Value;

        // Clean up the JSON string
        json = json.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();


        TenableHelper th = JsonConvert.DeserializeObject<TenableHelper>(json);
        return th;
    }

    public class PluginInfo
    {
        [JsonProperty("PluginId")]
        public int PluginId { get; set; }

        [JsonProperty("Path")]
        public string Path { get; set; }

        [JsonProperty("Solution")]
        public string Solution { get; set; }

        [JsonProperty("Fixed")]
        public string Fixed { get; set; }

        [JsonProperty("Cnt")]
        public int Cnt { get; set; }

    }

    public class TenableHelper
    {
        public int PluginId { get; set; }
        public string Guidance {  get; set; }
        public string URL { get; set; }
        public string AlternativeAction { get; set; }
        public string RemoveWindowsStoreAppScript { get; set; }
        public string UninstallApplication { get; set; }
        public string ArchiveFileScript { get; set; }

    }
}