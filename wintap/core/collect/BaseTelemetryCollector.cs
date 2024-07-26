using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using System;
using System.Diagnostics;
using com.espertech.esper.pattern.observer;

namespace gov.llnl.wintap.core.collect
{
    public abstract class BaseTelemetryCollector
    {
        private int _eventsPerSecond;
        private int _eventAccumulator;
        private int _droppedEvents;
        private int _lastDroppedEvents;
        private int _currentDroppedEvents;
        private int _totalEvents;

        /// <summary>
        /// The name of the thing that generates the events this instance collects. 
        /// For ETW sources, you may want to use the ProviderName or the EventName fields.
        /// </summary>
        internal string CollectorName { get; set; }

        internal BaseTelemetryCollector()
        {
            _droppedEvents = 0;
            _lastDroppedEvents = 0;

            System.Timers.Timer dataProcessingTimer = new System.Timers.Timer();
            dataProcessingTimer.Elapsed += DataProcessingTimer_Elapsed;
            dataProcessingTimer.AutoReset = true;
            dataProcessingTimer.Interval = 1000;
            dataProcessingTimer.Start();
        }

        // snapshots the total events received in the previous second
        private void DataProcessingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _eventsPerSecond = _eventAccumulator;
            _eventAccumulator = 0;
            if (_currentDroppedEvents > _lastDroppedEvents)
            {
                _lastDroppedEvents = _currentDroppedEvents;
                StateManager.DroppedEventsDetected = true;
                WintapMessage alertMsg = new WintapMessage(DateTime.UtcNow, Process.GetCurrentProcess().Id, "WintapAlert");
                alertMsg.WintapAlert = new WintapMessage.WintapAlertData();
                alertMsg.WintapAlert.AlertName = WintapMessage.WintapAlertData.AlertNameEnum.EVENT_DROP;
                alertMsg.WintapAlert.AlertDescription = "ETW Session is dropping events.  Session Name: " + CollectorName + " Total events dropped since sensor start: " + _droppedEvents;
                EventChannel.Send(alertMsg);
                WintapLogger.Log.Append(alertMsg.WintapAlert.AlertDescription, core.infrastructure.LogLevel.Always);
            }
            WintapLogger.Log.Append("ETW Session: " + CollectorName + " events per second: " + _eventsPerSecond, core.infrastructure.LogLevel.Always);
        }


        /// <summary>
        /// An average of events over a 10 second duration.  Value will be 0 until enough events accumulate to compute the 10 second average.
        /// </summary>
        internal int EventsPerSecond
        {
            get
            {
                return _eventsPerSecond;
            }
        }

        protected void UpdateStatistics()
        {
            UpdateStatistics(0);
        }

        /// <summary>
        /// Updates per-collector metrics.  Dropped event value passed here is the net new number of dropped events since last call.
        /// </summary>
        /// <param name="droppedEvents"></param>
        protected void UpdateStatistics(int droppedEvents)
        {
            _lastDroppedEvents = droppedEvents;
            _totalEvents++;
            _eventAccumulator++;
            _droppedEvents = _lastDroppedEvents;
            
        }

    }
}
