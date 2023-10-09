using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace gov.llnl.wintap.collect.shared
{
    public class EtwCollector : BaseCollector
    {
        private PerformanceCounter droppedEventsCounter;
        private PerformanceCounter eventsPerSecondCounter;

        /// <summary>
        /// Current count of events dropped for this ETW session as reported by Performance Monitor
        /// </summary>
        public long EtwDroppedEventCount { get; set; }

        public string EtwSessionName { get; set; }

        /// <summary>
        /// Events per second for this ETW trace session as reported by Performance Monitor.
        /// NOTE: this is not necessarily WintapMessage count due to filtering, but is a good metric for overall system load for this session.
        /// TODO:  add hueristic to Diagnostics class for comparing this value to WintapMessages per second.  WintapMessage count should always be less than this.
        /// </summary>
        public long EtwEventsPerSec { get; set; }

        public EtwCollector() : base()
        {
            EtwDroppedEventCount = 0;
        }

        public override bool Start()
        {
            droppedEventsCounter = new PerformanceCounter("Event Tracing for Windows Session", "Events Lost", EtwSessionName);
            eventsPerSecondCounter = new PerformanceCounter("Event Tracing for Windows Session", "Events Logged per sec", EtwSessionName);
            droppedEventsCounter.NextValue();
            eventsPerSecondCounter.NextValue();
            Timer statsCollectionTimer = new Timer();
            statsCollectionTimer.Interval = 60000;
            statsCollectionTimer.AutoReset = true;
            statsCollectionTimer.Elapsed += StatsCollectionTimer_Elapsed;
            statsCollectionTimer.Start();
            return true;
        }

        private void StatsCollectionTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            updateETWProviderState();
        }

        private void updateETWProviderState()
        {
            try
            {
                long currentDropCount = Convert.ToInt64(droppedEventsCounter.NextValue());
                EtwEventsPerSec = Convert.ToInt64(eventsPerSecondCounter.NextValue());
                WintapLogger.Log.Append("ETW Session: " + EtwSessionName + " events per second: " + EtwEventsPerSec, LogLevel.Always);
                // should probably raise an event here and do the alerting elsewhere, but due to the decoupled design of this section we handle it here to keep it simple.
                if (currentDropCount > EtwDroppedEventCount)
                {
                    StateManager.DroppedEventsDetected = true;
                    WintapMessage alertMsg = new WintapMessage(DateTime.UtcNow, System.Diagnostics.Process.GetCurrentProcess().Id, "WintapAlert");
                    alertMsg.WintapAlert = new WintapMessage.WintapAlertData();
                    alertMsg.WintapAlert.AlertName = WintapMessage.WintapAlertData.AlertNameEnum.EVENT_DROP;
                    alertMsg.WintapAlert.AlertDescription = "ETW Session is dropping events.  Session Name: " + this.EtwSessionName + " Total events dropped since sensor start: " + currentDropCount;
                    EventChannel.Send(alertMsg);
                    WintapLogger.Log.Append(alertMsg.WintapAlert.AlertDescription, LogLevel.Always);
                }
                WintapLogger.Log.Append("Dropped event count on provider: " + this.EtwSessionName + " is: " + EtwDroppedEventCount, LogLevel.Always);
                EtwDroppedEventCount = currentDropCount;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR getting ETW performance statistics for " + EtwSessionName + ".  error: " + ex.Message, LogLevel.Always);
                EtwDroppedEventCount = 0;
            }
        }
    }
}
