using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using System;
using System.Diagnostics;

namespace gov.llnl.wintap.core.collect
{
    public abstract class BaseCollector
    {

        /// <summary>
        /// The name of the thing that generates the events this instance collects. 
        /// For ETW sources, you may want to use the ProviderName or the EventName fields.
        /// </summary>
        internal string CollectorName { get; set; }

        public virtual bool Start()
        {
            return true;
        }

        public virtual void Stop()
        {

        }

        internal BaseCollector()
        {
           
        }

    }
}
