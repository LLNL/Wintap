
using Microsoft.Win32;

//  This is a signal generator for testing Wintap data collect
//  it simulates real-world file, registry, imageloading and other behavior
//  all actions are asserted as critical so that when we assess wintap's root cause ability
//  no try/catch so that any intentional breaking of this app will cause the app to fail


Console.WriteLine("Signals is starting");

//  REGISTRY depenedency
Console.WriteLine("Getting dependency path from Registry");
RegistryKey signalsRegKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Signals");
string dependencyPath = signalsRegKey.GetValue("DependencyPath").ToString();
Console.WriteLine($"Got dependency path: {dependencyPath}");
FileInfo dependencyFile = new FileInfo(Path.Combine(dependencyPath, "livai-api.dll"));

//  FILE dependency
Console.WriteLine($"Loading dependency DLL from file system: {dependencyFile.FullName}");
var assembly = System.Reflection.Assembly.LoadFrom(dependencyFile.FullName);



Console.WriteLine("Signals Completed!");
