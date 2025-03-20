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

        public IsolatedPluginCatalog(string directory)
        {
            _catalog = new AggregateCatalog();

            foreach (var pluginDir in Directory.GetDirectories(directory))
            {
                try
                {
                    string pluginName = new DirectoryInfo(pluginDir).Name;
                    string mainAssemblyPath = Path.Combine(pluginDir, $"{pluginName}.dll");

                    if (!File.Exists(mainAssemblyPath))
                    {
                        var dlls = Directory.GetFiles(pluginDir, "*.dll");
                        if (dlls.Length > 0)
                        {
                            mainAssemblyPath = dlls[0];
                        }
                        else
                        {
                            WintapLogger.Log.Append($"No assemblies found for plugin: {pluginName}", LogLevel.Always);
                            continue;
                        }
                    }

                    if (IsSignedAndTrusted(mainAssemblyPath))
                    {
                        try
                        {
                            // Load plugin in isolated domain
                            var pluginDomain = PluginDomainManager.Instance.LoadPlugin(mainAssemblyPath);
                            _loadedPlugins[pluginName] = pluginDomain;

                            // Create catalog for the loaded assembly
                            var asmCat = new AssemblyCatalog(pluginDomain.PluginAssembly);

                            if (asmCat.Parts.ToList().Count > 0)
                            {
                                _catalog.Catalogs.Add(asmCat);
                                WintapLogger.Log.Append($"Successfully loaded plugin: {pluginName} in isolated domain", LogLevel.Always);
                            }
                            else
                            {
                                WintapLogger.Log.Append($"Plugin {pluginName} has no MEF exports", LogLevel.Always);
                                PluginDomainManager.Instance.UnloadPlugin(pluginName);
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append($"Error loading plugin {pluginName} in isolated domain: {ex.Message}", LogLevel.Always);
                        }
                    }
                    else
                    {
                        WintapLogger.Log.Append($"Plugin {pluginName} is not signed or not trusted", LogLevel.Always);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Failed to load plugin from directory {pluginDir}: {ex.Message}", LogLevel.Always);
                }
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
            WintapLogger.Log.Append($"Checking signature for: {filePath}", LogLevel.Always);

#if DEBUG
            WintapLogger.Log.Append("DEBUG mode - bypassing signature verification", LogLevel.Always);
            return true;
#endif

            try
            {
                AssemblyName assemblyName = AssemblyName.GetAssemblyName(filePath);

                byte[] publicKeyToken = assemblyName.GetPublicKeyToken();
                if (publicKeyToken != null && publicKeyToken.Length > 0)
                {
                    string token = BitConverter.ToString(publicKeyToken).Replace("-", "").ToLower();
                    WintapLogger.Log.Append($"Assembly has strong name with token: {token}", LogLevel.Always);
                    return true;
                }
                else
                {
                    WintapLogger.Log.Append("Assembly does not have a strong name", LogLevel.Always);
                    return false;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error verifying assembly signature: {ex.Message}", LogLevel.Always);

#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }
    }
}