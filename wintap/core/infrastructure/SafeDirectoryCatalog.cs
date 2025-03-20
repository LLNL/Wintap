/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */


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
        private readonly List<PluginLoadContext> _loadContexts = new List<PluginLoadContext>();
        private readonly Dictionary<string, Assembly> _loadedPlugins = new Dictionary<string, Assembly>();

        public IsolatedPluginCatalog(string directory)
        {
            _catalog = new AggregateCatalog();
            System.Diagnostics.Debugger.Launch();
            // Scan for plugin directories
            foreach (var pluginDir in Directory.GetDirectories(directory))
            {
                try
                {
                    // Look for a main plugin assembly (assuming it has the same name as the directory)
                    string pluginName = new DirectoryInfo(pluginDir).Name;
                    string mainAssemblyPath = Path.Combine(pluginDir, $"{pluginName}.dll");

                    // If main assembly doesn't exist by convention, look for any DLL in the root
                    if (!File.Exists(mainAssemblyPath))
                    {
                        var dlls = Directory.GetFiles(pluginDir, "*.dll");
                        if (dlls.Length > 0)
                        {
                            // Use the first DLL found as the main assembly
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
                        // Create a load context for this plugin
                        var loadContext = new PluginLoadContext(mainAssemblyPath);
                        _loadContexts.Add(loadContext);

                        // Load the plugin assembly in its isolated context
                        Assembly pluginAssembly = loadContext.LoadFromAssemblyPath(mainAssemblyPath);
                        _loadedPlugins[pluginName] = pluginAssembly;

                        // Create a catalog for this assembly
                        var asmCat = new AssemblyCatalog(pluginAssembly);

                        // Verify the assembly contains exports
                        if (asmCat.Parts.ToList().Count > 0)
                        {
                            _catalog.Catalogs.Add(asmCat);
                            WintapLogger.Log.Append($"Successfully loaded plugin: {pluginName} in isolated context", LogLevel.Always);
                        }
                        else
                        {
                            WintapLogger.Log.Append($"Plugin {pluginName} has no MEF exports", LogLevel.Always);
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
            if (_loadedPlugins.TryGetValue(pluginName, out var assembly))
            {
                return assembly;
            }
            return null;
        }

        private bool IsSignedAndTrusted(string filePath)
        {
            WintapLogger.Log.Append($"Checking signature for: {filePath}", LogLevel.Always);

            // For development purposes, bypass signature verification
#if DEBUG
            WintapLogger.Log.Append("DEBUG mode - bypassing signature verification", LogLevel.Always);
            return true;
#endif

            try
            {
                // Attempt to load assembly to verify strong name
                AssemblyName assemblyName = AssemblyName.GetAssemblyName(filePath);

                // If the assembly has a public key token, it's signed with a strong name
                byte[] publicKeyToken = assemblyName.GetPublicKeyToken();
                if (publicKeyToken != null && publicKeyToken.Length > 0)
                {
                    string token = BitConverter.ToString(publicKeyToken).Replace("-", "").ToLower();
                    WintapLogger.Log.Append($"Assembly has strong name with token: {token}", LogLevel.Always);

                    // For now, accept any strong-named assembly as trusted
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

                // In development, we might want to bypass verification errors
#if DEBUG
                return true;
#else
        return false;
#endif
            }
        }
    }



    // workaround for MEF loading exceptions
    // see:  https://stackoverflow.com/a/4475117
    public class SafeDirectoryCatalog : ComposablePartCatalog
    {
        private readonly AggregateCatalog _catalog;

        public SafeDirectoryCatalog(string directory)
        {
            var files = Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            files = enumerateSignedPlugins(files.ToList());

            _catalog = new AggregateCatalog();

            foreach (var file in files)
            {
                try
                {
                    var asmCat = new AssemblyCatalog(file);

                    //Force MEF to load the plugin and figure out if there are any exports
                    // good assemblies will not throw the RTLE exception and can be added to the catalog
                    if (asmCat.Parts.ToList().Count > 0)
                        _catalog.Catalogs.Add(asmCat);
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    WintapLogger.Log.Append("WARN: problem loading plugin.  Name: " + file + " error: " + rtle.Message, core.infrastructure.LogLevel.Always);
                }
                catch (BadImageFormatException)
                {
                }
            }
        }
        public override IQueryable<ComposablePartDefinition> Parts
        {
            get { return _catalog.Parts; }
        }

        private List<string> enumerateSignedPlugins(List<string> files)
        {
            List<string> signedFiles = new List<string>();
            foreach (string file in files)
            {
                try
                {
#if DEBUG
                    WintapLogger.Log.Append("loading plugins in debug mode", core.infrastructure.LogLevel.Always);
                    signedFiles.Add(file);
#else
                    WintapLogger.Log.Append("loading plugins in release mode", core.infrastructure.LogLevel.Always);
                    if (isSignedAndTrusted(file))
                    {
                        signedFiles.Add(file);
                    }
                    else
                    {
                        WintapLogger.Log.Append(file + ": did NOT pass signature validation and will not be loaded.", core.infrastructure.LogLevel.Always);
                    }
#endif
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("WARN: " + file + " is NOT signed by a trusted authority and will not be loaded.", core.infrastructure.LogLevel.Always);
                }
            }
            return signedFiles;
        }

        private bool isSignedAndTrusted(string filePath)
        {
            bool isSigned = false;
            X509Certificate2Collection certificates = new X509Certificate2Collection();
            certificates.Import(filePath);
            if (certificates.Count > 0)
            {
                foreach (var cert in certificates)
                {
                    using (X509Chain chain = new X509Chain(true)) // true=only evaluate machine store
                    {
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                        bool isValid = chain.Build(cert);
                        chain.ChainPolicy.VerificationTime = DateTime.Now;
                        if (isValid)
                        {
                            isSigned = true;
                            break;
                        }
                    }
                }
            }
            return isSigned;
        }
    }
}
