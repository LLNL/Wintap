using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace gov.llnl.wintap.core.infrastructure
{
    /// <summary>
    /// Represents an isolated domain for loading and managing plugins.
    /// Each plugin is loaded in its own context to allow for proper unloading.
    /// </summary>
    public class PluginDomain : IDisposable
    {
        private readonly AssemblyLoadContext _loadContext;
        private readonly string _pluginId;
        private readonly string _pluginPath;
        private Assembly _pluginAssembly;
        private bool _isDisposed;

        /// <summary>
        /// Gets the unique identifier for this plugin.
        /// </summary>
        public string PluginId => _pluginId;

        /// <summary>
        /// Gets the loaded assembly for this plugin.
        /// </summary>
        public Assembly PluginAssembly => _pluginAssembly;

        /// <summary>
        /// Initializes a new instance of the PluginDomain class.
        /// </summary>
        /// <param name="pluginId">The unique identifier for the plugin.</param>
        /// <param name="pluginPath">The file path to the plugin assembly.</param>
        public PluginDomain(string pluginId, string pluginPath)
        {
            _pluginId = pluginId;
            _pluginPath = pluginPath;
            _loadContext = new PluginLoadContext(pluginPath);
            _loadContext.Unloading += OnUnloading;
        }

        /// <summary>
        /// Loads the plugin assembly into memory.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when attempting to load an already loaded plugin.</exception>
        public void LoadPlugin()
        {
            if (_pluginAssembly != null)
                throw new InvalidOperationException("Plugin already loaded");

            _pluginAssembly = _loadContext.LoadFromAssemblyPath(_pluginPath);
        }

        /// <summary>
        /// Event handler that is called when the assembly load context is being unloaded.
        /// </summary>
        /// <param name="obj">The assembly load context being unloaded.</param>
        private void OnUnloading(AssemblyLoadContext obj)
        {
            WintapLogger.Log.Append($"Unloading plugin domain for {_pluginId}", LogLevel.Info);
        }

        /// <summary>
        /// Unloads the plugin from memory.
        /// </summary>
        public void Unload()
        {
            if (_loadContext != null)
            {
                _loadContext.Unload();
                _pluginAssembly = null;
            }
        }

        /// <summary>
        /// Disposes the plugin domain, unloading the plugin if it hasn't already been unloaded.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                Unload();
                _isDisposed = true;
            }
        }
    }

    /// <summary>
    /// Manages the loading, unloading, and retrieval of plugin domains.
    /// Implements the singleton pattern to ensure a single instance throughout the application.
    /// </summary>
    public class PluginDomainManager
    {
        private static readonly Lazy<PluginDomainManager> _instance =
            new Lazy<PluginDomainManager>(() => new PluginDomainManager());

        private readonly ConcurrentDictionary<string, PluginDomain> _pluginDomains;

        /// <summary>
        /// Gets the singleton instance of the PluginDomainManager.
        /// </summary>
        public static PluginDomainManager Instance => _instance.Value;

        /// <summary>
        /// Initializes a new instance of the PluginDomainManager class.
        /// Private constructor ensures the singleton pattern.
        /// </summary>
        private PluginDomainManager()
        {
            _pluginDomains = new ConcurrentDictionary<string, PluginDomain>();
        }

        /// <summary>
        /// Loads a plugin from the specified path into an isolated domain.
        /// </summary>
        /// <param name="pluginPath">The file path to the plugin assembly.</param>
        /// <returns>The loaded plugin domain.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the plugin is already loaded or when the loading process fails.</exception>
        public PluginDomain LoadPlugin(string pluginPath)
        {
            var pluginId = Path.GetFileNameWithoutExtension(pluginPath);
            var pluginDomain = new PluginDomain(pluginId, pluginPath);

            if (_pluginDomains.TryAdd(pluginId, pluginDomain))
            {
                try
                {
                    pluginDomain.LoadPlugin();
                    WintapLogger.Log.Append($"Successfully loaded plugin {pluginId} in isolated domain", LogLevel.Info);
                    return pluginDomain;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Failed to load plugin {pluginId}: {ex.Message}", LogLevel.Info);
                    _pluginDomains.TryRemove(pluginId, out _);
                    pluginDomain.Dispose();
                    throw;
                }
            }

            throw new InvalidOperationException($"Plugin {pluginId} already loaded");
        }

        /// <summary>
        /// Unloads a plugin with the specified ID.
        /// </summary>
        /// <param name="pluginId">The ID of the plugin to unload.</param>
        public void UnloadPlugin(string pluginId)
        {
            if (_pluginDomains.TryRemove(pluginId, out var pluginDomain))
            {
                pluginDomain.Dispose();
                WintapLogger.Log.Append($"Unloaded plugin {pluginId}", LogLevel.Info);
            }
        }

        /// <summary>
        /// Unloads all currently loaded plugins.
        /// </summary>
        public void UnloadAllPlugins()
        {
            foreach (var pluginId in _pluginDomains.Keys)
            {
                UnloadPlugin(pluginId);
            }
        }

        /// <summary>
        /// Retrieves a plugin domain by its ID.
        /// </summary>
        /// <param name="pluginId">The ID of the plugin to retrieve.</param>
        /// <returns>The plugin domain if found; otherwise, null.</returns>
        public PluginDomain GetPluginDomain(string pluginId)
        {
            _pluginDomains.TryGetValue(pluginId, out var domain);
            return domain;
        }
    }
}