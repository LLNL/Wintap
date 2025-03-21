using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace gov.llnl.wintap
{
    public class IsolatedPluginCatalog : ComposablePartCatalog
    {
        private readonly AggregateCatalog _catalog;
        private readonly Dictionary<string, PluginDomain> _loadedPlugins = new Dictionary<string, PluginDomain>();


        // <summary>
        /// Initializes a new instance of the IsolatedPluginCatalog class that discovers and loads
        /// plugin assemblies from the specified directory into isolated AppDomains.
        /// </summary>
        /// <param name="directory">The directory to scan for plugin subdirectories</param>
        /// <remarks>
        /// The plugin loading scheme follows these steps for each subdirectory in the specified directory:
        ///
        /// 1. Each subdirectory is treated as a potential plugin with the directory name as the plugin name
        /// 2. The loader first attempts to find a name-matched assembly (PluginName.dll)
        /// 3. If the name-matched assembly exists and contains MEF exports, it's loaded as the plugin
        /// 4. If the name-matched assembly doesn't exist or doesn't contain MEF exports, the loader
        ///    iterates through all DLLs in the directory, looking for the first one with MEF exports
        /// 5. Each candidate assembly is verified to be signed and trusted (except in DEBUG builds)
        /// 6. The first valid assembly with MEF exports is loaded into an isolated AppDomain
        /// 7. If no valid assembly is found, the plugin is skipped with an appropriate log message
        ///
        /// This approach balances convention (preferring name-matched assemblies) with flexibility
        /// (falling back to any assembly with exports), while ensuring only valid plugin assemblies
        /// are loaded into the MEF catalog.
        /// </remarks>
        public IsolatedPluginCatalog(string directory)
        {
            _catalog = new AggregateCatalog();
            _loadedPlugins = new Dictionary<string, PluginDomain>();

            foreach (var pluginDir in Directory.GetDirectories(directory))
            {
                try
                {
                    string pluginName = new DirectoryInfo(pluginDir).Name;
                    WintapLogger.Log.Append($"Attempting to load plugin: {pluginName}", LogLevel.Debug);

                    // Get all potential DLL files
                    var allDlls = Directory.GetFiles(pluginDir, "*.dll");
                    if (allDlls.Length == 0)
                    {
                        WintapLogger.Log.Append($"No assemblies found for plugin: {pluginName}", LogLevel.Info);
                        continue;
                    }

                    // First, try the name-matched DLL
                    string nameMatchedDll = Path.Combine(pluginDir, $"{pluginName}.dll");
                    string mainAssemblyPath = null;

                    if (File.Exists(nameMatchedDll))
                    {
                        // Try loading the name-matched DLL first
                        if (TryLoadPluginAssembly(nameMatchedDll, pluginName))
                        {
                            // Successfully loaded the name-matched assembly
                            continue;
                        }
                        else
                        {
                            WintapLogger.Log.Append($"Name-matched DLL for {pluginName} exists but contains no MEF exports or failed to load, trying other DLLs", LogLevel.Debug);
                        }
                    }

                    // Name-matched DLL wasn't found or didn't have exports, try each DLL in order
                    bool foundValidPlugin = false;
                    foreach (var dllPath in allDlls)
                    {
                        // Skip if we already tried the name-matched DLL
                        if (dllPath.Equals(nameMatchedDll, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (TryLoadPluginAssembly(dllPath, pluginName))
                        {
                            foundValidPlugin = true;
                            break;
                        }
                    }

                    if (!foundValidPlugin)
                    {
                        WintapLogger.Log.Append($"No valid plugin assembly with MEF exports found for: {pluginName}", LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Failed to load plugin from directory {pluginDir}: {ex.Message}", LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// Attempts to load a potential plugin assembly and add it to the catalog if valid
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly to try loading</param>
        /// <param name="pluginName">The name of the plugin</param>
        /// <returns>True if the assembly was successfully loaded as a plugin, false otherwise</returns>
        private bool TryLoadPluginAssembly(string assemblyPath, string pluginName)
        {
            // Check if assembly is signed and trusted, but bypass check in debug builds
            bool isDebugBuild = false;
#if DEBUG
            isDebugBuild = true;
#endif

            if (!isDebugBuild && !IsSignedAndTrusted(assemblyPath))
            {
                WintapLogger.Log.Append($"Assembly {Path.GetFileName(assemblyPath)} for plugin {pluginName} is not signed or not trusted", LogLevel.Debug);
                return false;
            }

            if (isDebugBuild && !IsSignedAndTrusted(assemblyPath))
            {
                WintapLogger.Log.Append($"[DEBUG BUILD] Loading unsigned assembly {Path.GetFileName(assemblyPath)} for plugin {pluginName}", LogLevel.Info);
            }

            try
            {
                // Load plugin in isolated domain
                var pluginDomain = PluginDomainManager.Instance.LoadPlugin(assemblyPath);

                // Check if this assembly has MEF exports
                var asmCat = new AssemblyCatalog(pluginDomain.PluginAssembly);
                var parts = asmCat.Parts.ToList();

                if (parts.Count > 0)
                {
                    // Assembly has MEF exports, add it to our catalog
                    _loadedPlugins[pluginName] = pluginDomain;
                    _catalog.Catalogs.Add(asmCat);
                    WintapLogger.Log.Append($"Successfully loaded plugin: {pluginName} from {Path.GetFileName(assemblyPath)} in isolated domain", LogLevel.Info);
                    return true;
                }
                else
                {
                    // No MEF exports, unload and continue
                    WintapLogger.Log.Append($"Assembly {Path.GetFileName(assemblyPath)} for plugin {pluginName} has no MEF exports", LogLevel.Debug);
                    PluginDomainManager.Instance.UnloadPlugin(pluginName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error loading assembly {Path.GetFileName(assemblyPath)} for plugin {pluginName}: {ex.Message}", LogLevel.Debug);
                // Make sure to unload the plugin domain if it was created
                try
                {
                    if (_loadedPlugins.ContainsKey(pluginName))
                    {
                        PluginDomainManager.Instance.UnloadPlugin(pluginName);
                        _loadedPlugins.Remove(pluginName);
                    }
                }
                catch { /* Suppress any errors during cleanup */ }
                return false;
            }
        }

        public override IQueryable<ComposablePartDefinition> Parts
        {
            get { return _catalog.Parts; }
        }

        public Assembly GetPluginAssembly(string pluginName)
        {
            if (_loadedPlugins.TryGetValue(pluginName, out var domain))
            {
                return domain.PluginAssembly;
            }
            return null;
        }

        public void UnloadPlugin(string pluginName)
        {
            if (_loadedPlugins.ContainsKey(pluginName))
            {
                PluginDomainManager.Instance.UnloadPlugin(pluginName);
                _loadedPlugins.Remove(pluginName);
            }
        }

        private bool IsSignedAndTrusted(string filePath)
        {
            WintapLogger.Log.Append($"Checking signature for: {filePath}", LogLevel.Info);

#if DEBUG
            WintapLogger.Log.Append("DEBUG mode - bypassing signature verification", LogLevel.Info);
            return true;
#endif

            try
            {
                AssemblyName assemblyName = AssemblyName.GetAssemblyName(filePath);

                byte[] publicKeyToken = assemblyName.GetPublicKeyToken();
                if (publicKeyToken != null && publicKeyToken.Length > 0)
                {
                    string token = BitConverter.ToString(publicKeyToken).Replace("-", "").ToLower();
                    WintapLogger.Log.Append($"Assembly has strong name with token: {token}", LogLevel.Info);
                    return true;
                }
                else
                {
                    WintapLogger.Log.Append("Assembly does not have a strong name", LogLevel.Info);
                    return false;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error verifying assembly signature: {ex.Message}", LogLevel.Info);

#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }
    }
}