using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace gov.llnl.wintap.core.infrastructure
{
    public class PluginDomain : IDisposable
    {
        private readonly AssemblyLoadContext _loadContext;
        private readonly string _pluginId;
        private readonly string _pluginPath;
        private Assembly _pluginAssembly;
        private bool _isDisposed;

        public string PluginId => _pluginId;
        public Assembly PluginAssembly => _pluginAssembly;

        public PluginDomain(string pluginId, string pluginPath)
        {
            _pluginId = pluginId;
            _pluginPath = pluginPath;
            _loadContext = new PluginLoadContext(pluginPath);
            _loadContext.Unloading += OnUnloading;
        }

        public void LoadPlugin()
        {
            if (_pluginAssembly != null)
                throw new InvalidOperationException("Plugin already loaded");

            _pluginAssembly = _loadContext.LoadFromAssemblyPath(_pluginPath);
        }

        private void OnUnloading(AssemblyLoadContext obj)
        {
            WintapLogger.Log.Append($"Unloading plugin domain for {_pluginId}", LogLevel.Always);
        }

        public void Unload()
        {
            if (_loadContext != null)
            {
                _loadContext.Unload();
                _pluginAssembly = null;
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Unload();
                _isDisposed = true;
            }
        }
    }

    public class PluginDomainManager
    {
        private static readonly Lazy<PluginDomainManager> _instance =
            new Lazy<PluginDomainManager>(() => new PluginDomainManager());

        private readonly ConcurrentDictionary<string, PluginDomain> _pluginDomains;

        public static PluginDomainManager Instance => _instance.Value;

        private PluginDomainManager()
        {
            _pluginDomains = new ConcurrentDictionary<string, PluginDomain>();
        }

        public PluginDomain LoadPlugin(string pluginPath)
        {
            var pluginId = Path.GetFileNameWithoutExtension(pluginPath);
            var pluginDomain = new PluginDomain(pluginId, pluginPath);

            if (_pluginDomains.TryAdd(pluginId, pluginDomain))
            {
                try
                {
                    pluginDomain.LoadPlugin();
                    WintapLogger.Log.Append($"Successfully loaded plugin {pluginId} in isolated domain", LogLevel.Always);
                    return pluginDomain;
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Failed to load plugin {pluginId}: {ex.Message}", LogLevel.Always);
                    _pluginDomains.TryRemove(pluginId, out _);
                    pluginDomain.Dispose();
                    throw;
                }
            }

            throw new InvalidOperationException($"Plugin {pluginId} already loaded");
        }

        public void UnloadPlugin(string pluginId)
        {
            if (_pluginDomains.TryRemove(pluginId, out var pluginDomain))
            {
                pluginDomain.Dispose();
                WintapLogger.Log.Append($"Unloaded plugin {pluginId}", LogLevel.Always);
            }
        }

        public void UnloadAllPlugins()
        {
            foreach (var pluginId in _pluginDomains.Keys)
            {
                UnloadPlugin(pluginId);
            }
        }

        public PluginDomain GetPluginDomain(string pluginId)
        {
            _pluginDomains.TryGetValue(pluginId, out var domain);
            return domain;
        }
    }
}