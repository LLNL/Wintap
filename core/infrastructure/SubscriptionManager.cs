/*
 * Copyright (c) 2016, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Reflection;

namespace gov.llnl.wintap.core.infrastructure
{

    public class SubscriptionManager
    {
        private List<EtwProviderCollector> etwCollectors;
        private List<BaseCollector> baseCollectors;
        private Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords kernelFlags;

        internal SubscriptionManager()
        {
            baseCollectors = new List<BaseCollector>();
            etwCollectors = new List<EtwProviderCollector>();
        }

        internal void Start()
        {
            // start modelled collectors
            string nameSpace = "gov.llnl.wintap.collect";
            foreach (SettingsProperty sp in Properties.Settings.Default.Properties)
            {
                if (sp.Name.EndsWith("Collector") && Properties.Settings.Default[sp.Name].ToString() == "True")
                {
                    System.Threading.Thread.Sleep(500);  // without this you will sometimes get an exception from TraceEventSession - race condition? 
                    string collectorName = nameSpace + "." + sp.Name;
                    WintapLogger.Log.Append("Attempting to load collector with name: " + collectorName, LogLevel.Always);
                    try
                    {
                        Type type = Type.GetType(collectorName);
                        object instance = Activator.CreateInstance(type, null);
                        MethodInfo method = type.GetMethod("Start");
                        if ((bool)method.Invoke(instance, null))
                        {
                            baseCollectors.Add((BaseCollector)instance); // save the collectors so we can call thier Stop() methods on shutdown.
                        }
                        try
                        {
                            // there can only be one kernel logger.  collectors that want to consume from NT Kernel Logger will declare this via kernel trace flags which we append to the global list.
                            WintapLogger.Log.Append("Inspecting collector for Kernel trace flags: " + collectorName, LogLevel.Always);
                            PropertyInfo pi = type.GetProperty("KernelTraceEventFlags");
                            PropertyInfo pinfo = instance.GetType().GetProperty("KernelTraceEventFlags");
                            if(pinfo != null)  // only true for nt kernel logger collectors
                            {
                                KernelTraceEventParser.Keywords newFlags = (KernelTraceEventParser.Keywords)pinfo.GetValue(instance, null);
                                kernelFlags = kernelFlags | newFlags;
                                WintapLogger.Log.Append("Found Kernel trace flags on " + collectorName + " flags: " + newFlags.ToString(), LogLevel.Always);
                            }
                        }
                        catch (Exception ex)
                        {
                            WintapLogger.Log.Append("Error looking for Kernel trace flags on " + collectorName + ", error: " + ex.Message, LogLevel.Debug);
                        }
                    }
                    catch (Exception ex)
                    {
                        WintapLogger.Log.Append(sp.Name + " error loading collector: " + ex.Message, LogLevel.Always);
                    }

                }
            }
            WintapLogger.Log.Append("Done loading modelled collectors", LogLevel.Always);

            // Start unmodelled (aka generic) collectors
            WintapLogger.Log.Append("loading unmodelled collectors", LogLevel.Always);
            int genericCounter = 0;
            foreach (string genericProvider in Properties.Settings.Default.GenericProviders)
            {
                genericCounter++;
                string etwCollectorName = genericProvider;
                WintapLogger.Log.Append("Found generic etw provider in config: " + etwCollectorName, LogLevel.Always);
                System.Threading.Thread.Sleep(1000);
                GenericCollector gc = new GenericCollector() { CollectorName = etwCollectorName, EtwProviderId = genericProvider };
                if (gc.Start())
                {
                    //TraceSession.Instance.Session.EnableProvider(genericProvider);
                    baseCollectors.Add((BaseCollector)gc);
                }
            }
            WintapLogger.Log.Append("Done loading unmodelled collectors", LogLevel.Always);

            // Create the shared Kernel logger session with the required event flags
            WintapLogger.Log.Append("Creating Kernel event listening thread (ETW)...", LogLevel.Always);
            BackgroundWorker etwKernelModeListeningThread = new BackgroundWorker();
            etwKernelModeListeningThread.WorkerSupportsCancellation = true;
            etwKernelModeListeningThread.DoWork += new DoWorkEventHandler(etwKernelModeListeningThread_DoWork);
            etwKernelModeListeningThread.RunWorkerAsync();


            wintapPID = System.Diagnostics.Process.GetCurrentProcess().Id;
            WintapLogger.Log.Append("Done loading collectors", LogLevel.Always);
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("Sensor shutting down. ", LogLevel.Always);
            foreach(BaseCollector collector in baseCollectors)
            {
                collector.Stop();
            }
            KernelSession.Instance.EtwSession.Stop();
            WintapLogger.Log.Append("Sensor shutdown", LogLevel.Always);
        }

        private int wintapPID;
        /// <summary>
        /// The Process ID for the wintap process, used in event filtering / feedback avoidance
        /// </summary>
        public int WintapPID
        {
            get { return wintapPID; }
        }

        /// <summary>
        /// ETW listening thread for the NT Kernel Mode Logger
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void etwKernelModeListeningThread_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                WintapLogger.Log.Append("starting kernel mode ETW event handler", LogLevel.Always);
                KernelSession.Instance.EtwSession.EnableKernelProvider(kernelFlags);
                KernelSession.Instance.Start();
                ETWTraceEventSource source = KernelSource.Instance.EtwSource;
                source.Process();  // this is a blocking call!  an etw-ism
                WintapLogger.Log.Append("CRITICAL: Kernel mode etw listening thread has stopped", LogLevel.Always);
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("ERROR starting ETW kernel mode session: " + ex.Message, LogLevel.Always);
            }
           
        }
    }
}
