using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Configuration.Provider;
using com.espertech.esper.client.annotation;
using System.Diagnostics;
using Org.BouncyCastle.Bcpg.OpenPgp;
using gov.llnl.wintap.collect.models;
using System.Management.Automation.Tracing;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.collect.shared
{

    internal delegate void EtwSampleEventHandler(object sender, EtwSampleEventArgs e);

    public sealed class EtwUtility
    {
        private static readonly ETWProvider etwProvider = new ETWProvider();

        public static ETWProvider ETW
        {
            get
            {
                return etwProvider;
            }
        }

        internal void StartProvider(string providerName)
        {
            etwProvider.Start(providerName);
        }

        internal void StopProvider()
        {
            etwProvider.Stop();
        }
    }

    public class ETWProvider
    {
        internal event EtwSampleEventHandler EtwSampleEvent;
        internal virtual void OnEtwSample(EtwSampleEventArgs e)
        {
            if (EtwSampleEvent != null)
            {
                EtwSampleEvent(this, e);
            }
        }

        /// <summary>
        /// Returns a list of names for all the ETW providers on the system.
        /// </summary>
        /// <returns></returns>
        internal List<string> GetProviders()
        {
            List<string> providers = new List<string>();
            try
            {

                foreach (Guid providerGuid in TraceEventProviders.GetRegisteredOrEnabledProviders())
                {
                    providers.Add(TraceEventProviders.GetProviderName(providerGuid));
                }
            }
            catch (Exception ex) {     }

            return providers;
        }

        internal Guid ProviderGuid { get; set; }
        internal string ProviderName { get; set; }
        internal DateTime startTime;
        internal BackgroundWorker etwWorker;
        internal TraceEventSession userModeSession;
        internal ETWTraceEventSource userModeProvider;


        internal ETWProvider()
        {

        }

        internal void Start(string _name)
        {
            if(_name == this.ProviderName)
            {
                return;
            }
            if(this.ProviderName != null)
            {
                Stop();  // another provider is active, so stop it before creating a new one.
            }
            Guid providerGuid = TraceEventProviders.GetProviderGuidByName(_name);
            this.ProviderGuid = providerGuid;
            this.ProviderName = _name;
            etwWorker = new BackgroundWorker();
            etwWorker.DoWork += EtwWorker_DoWork;
            etwWorker.RunWorkerAsync();
        }

        internal void Stop()
        {
            if (etwWorker == null) { return; }
            try
            {
                this.userModeProvider.StopProcessing();
                this.userModeProvider.Dispose();
                this.userModeSession.Stop();
                this.userModeSession.Dispose();
            }
            catch(Exception ex){ }
        }

        private void EtwWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                userModeSession = new TraceEventSession("ETWScanner." + this.ProviderName, TraceEventSessionOptions.Create);
                userModeSession.BufferSizeMB = 250;
                userModeSession.EnableProvider(this.ProviderGuid);
                userModeProvider = new ETWTraceEventSource("ETWScanner." + this.ProviderName, TraceEventSourceType.Session);
                RegisteredTraceEventParser tdh = new RegisteredTraceEventParser(userModeProvider);
                tdh.All += new Action<TraceEvent>(tdh_All);
                userModeProvider.Process();
            }
            catch (Exception ex) { }
        }

        internal void tdh_All(TraceEvent obj)
        {
            if (obj.ProviderGuid == this.ProviderGuid)
            {
                ETWSample sample = new ETWSample() { EventName = obj.EventName, ProviderName = this.ProviderName, SampleId=Guid.NewGuid().ToString()};
                sample.Message = obj.ToString();
                EtwSampleEventArgs etwSampleEventArgs = new EtwSampleEventArgs();
                etwSampleEventArgs.ProviderName = sample.ProviderName;
                etwSampleEventArgs.ETWSampleEvent = sample;
                OnEtwSample(etwSampleEventArgs);
            }
        }
    }

    internal class ETWSample
    {
        public string ProviderName { get; set; }

        public string EventName { get; set; }

        public string Message { get; set; }

        public string SampleId { get; set; }
    }

    internal class EtwSampleEventArgs
    {
        public string ProviderName { get; set; }
        public ETWSample ETWSampleEvent { get; set; }
    }
}
