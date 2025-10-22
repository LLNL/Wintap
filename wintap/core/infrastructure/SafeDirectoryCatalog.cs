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
    /// <summary>
    /// A MEF catalog that loads plugins from isolated AssemblyLoadContexts.
    /// Supports the directory-based plugin convention: .\Plugins\PluginName\PluginName.dll
    /// </summary>
    public class IsolatedPluginCatalog : ComposablePartCatalog
    {
        private readonly AggregateCatalog _catalog;
        private readonly Dictionary<string, PluginDomain> _loadedPlugins = new Dictionary<string, PluginDomain>();

        /// <summary>
        /// Initializes a new instance of the IsolatedPluginCatalog class with explicit plugin paths.
        /// This is the preferred constructor that works with the directory-based convention.
        /// </summary>
        /// <param name="directory">The base plugins directory (used for context/logging)</param>
        /// <param name="pluginPaths">List of specific plugin DLL paths to load</param>
        /// <remarks>
        /// Plugin paths should follow the convention: .\Plugins\PluginName\PluginName.dll
        /// Each plugin is loaded into its own isolated AssemblyLoadContext for proper unloading.
        /// Only plugins with valid MEF exports will be added to the catalog.
        /// </remarks>
        public IsolatedPluginCatalog(string directory, List<string> pluginPaths)
        {
            _catalog = new AggregateCatalog();
            _loadedPlugins = new Dictionary<string, PluginDomain>();

            if (pluginPaths == null || pluginPaths.Count == 0)
            {
                WintapLogger.Log.Append("IsolatedPluginCatalog: No plugin paths provided", LogLevel.Info);
                return;
            }

            WintapLogger.Log.Append($"IsolatedPluginCatalog: Loading {pluginPaths.Count} plugin(s)", LogLevel.Info);

            foreach (var pluginPath in pluginPaths)
            {
                try
                {
                    // Extract plugin name from the path
                    // Path format: .\Plugins\PluginName\PluginName.dll
                    var pluginDir = Path.GetDirectoryName(pluginPath);
                    var pluginName = Path.GetFileName(pluginDir);

                    WintapLogger.Log.Append($"Loading plugin: {pluginName} from {pluginPath}", LogLevel.Debug);

                    if (!File.Exists(pluginPath))
                    {
                        WintapLogger.Log.Append($"Plugin DLL not found: {pluginPath}", LogLevel.Warn);
                        continue;
                    }

                    // Attempt to load the plugin
                    if (TryLoadPluginAssembly(pluginPath, pluginName))
                    {
                        WintapLogger.Log.Append($"Successfully loaded plugin: {pluginName}", LogLevel.Info);
                    }
                    else
                    {
                        WintapLogger.Log.Append($"Failed to load plugin: {pluginName}", LogLevel.Warn);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error loading plugin from {pluginPath}: {ex.Message}", LogLevel.Error);
                    WintapLogger.Log.Append($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                }
            }

            WintapLogger.Log.Append($"IsolatedPluginCatalog: Loaded {_loadedPlugins.Count} plugin(s) successfully", LogLevel.Info);
        }

        /// <summary>
        /// Attempts to load a plugin assembly and add it to the catalog if valid.
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
                    WintapLogger.Log.Append($"Successfully loaded plugin: {pluginName} from {Path.GetFileName(assemblyPath)} in isolated domain ({parts.Count} MEF part(s))", LogLevel.Info);
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
                WintapLogger.Log.Append($"Stack trace: {ex.StackTrace}", LogLevel.Debug);

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

        /// <summary>
        /// Checks if an assembly is signed and trusted.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly to check</param>
        /// <returns>True if the assembly is signed and trusted, false otherwise</returns>
        private bool IsSignedAndTrusted(string assemblyPath)
        {
            try
            {
                // Check for Authenticode signature
                X509Certificate cert = X509Certificate.CreateFromSignedFile(assemblyPath);

                if (cert == null)
                {
                    WintapLogger.Log.Append($"Assembly {Path.GetFileName(assemblyPath)} is not signed", LogLevel.Debug);
                    return false;
                }

                // Create X509Certificate2 for more detailed validation
                X509Certificate2 cert2 = new X509Certificate2(cert);

                // Verify the certificate chain
                X509Chain chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.VerificationTime = DateTime.Now;
                chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 0, 30);

                bool chainIsValid = chain.Build(cert2);

                if (!chainIsValid)
                {
                    WintapLogger.Log.Append($"Assembly {Path.GetFileName(assemblyPath)} certificate chain is not valid", LogLevel.Debug);
                    foreach (X509ChainStatus chainStatus in chain.ChainStatus)
                    {
                        WintapLogger.Log.Append($"  Chain status: {chainStatus.Status} - {chainStatus.StatusInformation}", LogLevel.Debug);
                    }
                    return false;
                }

                WintapLogger.Log.Append($"Assembly {Path.GetFileName(assemblyPath)} is signed and trusted", LogLevel.Debug);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error checking assembly signature for {Path.GetFileName(assemblyPath)}: {ex.Message}", LogLevel.Debug);
                return false;
            }
        }

        /// <summary>
        /// Unloads a specific plugin by name.
        /// </summary>
        /// <param name="pluginName">Name of the plugin to unload</param>
        public void UnloadPlugin(string pluginName)
        {
            if (_loadedPlugins.TryGetValue(pluginName, out var pluginDomain))
            {
                try
                {
                    WintapLogger.Log.Append($"Unloading plugin: {pluginName}", LogLevel.Info);
                    PluginDomainManager.Instance.UnloadPlugin(pluginName);
                    _loadedPlugins.Remove(pluginName);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error unloading plugin {pluginName}: {ex.Message}", LogLevel.Warn);
                }
            }
        }

        /// <summary>
        /// Gets the parts from all loaded plugin catalogs.
        /// </summary>
        public override IQueryable<ComposablePartDefinition> Parts => _catalog.Parts;

        /// <summary>
        /// Gets the number of currently loaded plugins.
        /// </summary>
        public int LoadedPluginCount => _loadedPlugins.Count;

        /// <summary>
        /// Gets the names of all currently loaded plugins.
        /// </summary>
        public IEnumerable<string> LoadedPluginNames => _loadedPlugins.Keys;
    }
}