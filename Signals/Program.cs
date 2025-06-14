
using Microsoft.Win32;
using System.Net;
using System.Net.Sockets;
using System.Text;

//  This is a signal generator for testing Wintap data collect
//  it simulates real-world file, registry, imageloading and other behavior
//  all actions are asserted as critical so that when we assess wintap's root cause ability
//  no try/catch so that any intentional breaking of this app will cause the app to fail


Console.WriteLine("Signals is starting  awaiting pid harvest (press any key to continue)");
Console.ReadLine();

if(args.Contains("setup"))
{
    Console.WriteLine("Signals is entering SETUP mode");
    RegistryKey regKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Signals");
    regKey.SetValue("DependencyPath", @"C:\Program Files\Signals\Lib");

    Console.WriteLine("SETUP mode complete, Signals is exiting.");
    return;
}



//  REGISTRY depenedency
Console.WriteLine("Getting dependency path from Registry");
RegistryKey signalsRegKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Signals");
string dependencyPath = signalsRegKey.GetValue("DependencyPath").ToString();
Console.WriteLine($"Got dependency path: {dependencyPath}");
FileInfo dependencyFile = new FileInfo(Path.Combine(dependencyPath, "livai-api.dll"));


if (args.Contains("gensignal"))
{
    Console.WriteLine("Signals is entering SIGNAL GENERATION mode");
    Console.WriteLine("Signals is deleting the dependency");
    dependencyFile.Delete();
    Console.WriteLine("Signals generation complete");
    return;
}

//  FILE dependency
Console.WriteLine($"Loading dependency DLL from file system: {dependencyFile.FullName}");
var assembly = System.Reflection.Assembly.LoadFrom(dependencyFile.FullName);


// CONNECT to the internet
using (HttpClient client = new HttpClient())
{
    // CONNECT to google
    // URL to connect to (example: Google's homepage)
    string url = "https://www.google.com ";
    // Send a GET request
    HttpResponseMessage response = await client.GetAsync(url);
    // Ensure the response is successful (status code 200-299)
    response.EnsureSuccessStatusCode();
    // Read the response content as a string
    string responseBody = await response.Content.ReadAsStringAsync();
    // Output the response
    Console.WriteLine("Response received:");
    Console.WriteLine(responseBody);
}



Console.WriteLine("Signals Completed!");
