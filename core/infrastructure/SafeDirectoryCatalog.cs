/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */


using gov.llnl.wintap.core.infrastructure;
using Microsoft.Extensions.Logging;
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
                        signedFiles.Add(file);
                #else
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
