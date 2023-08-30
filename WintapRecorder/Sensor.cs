using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.ComponentModel;
using Microsoft.Win32;

namespace WintapRecorder
{
    /// <summary>
    /// tail log for errors, generate event on log errors
    /// scan log for complete process tree, generate event for clean log
    /// tail log for event count and dropped event count metrics
    /// start/stop wintap
    /// </summary>
    internal class Sensor
    {
        internal enum MetricNameEnum { TotalEventCount, DroppedEventCount, ParquetCount };

        private int registryErrors;

        /// <summary>
        /// When significant errors are encoutered in the Wintap/WintapETL logs
        /// </summary>
        internal event EventHandler<EventArgs> SensorError;
        protected virtual void OnSensorErrorEvent(EventArgs e)
        {
            EventHandler<EventArgs> handler = SensorError;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// When a complete process tree is ready
        /// </summary>
        internal event EventHandler<EventArgs> ProcessTreeReady;
        protected virtual void OnOnProcessTreeReadyEvent(EventArgs e)
        {
            EventHandler<EventArgs> handler = ProcessTreeReady;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// When the total event count metric is updated in the WintapETL log
        /// </summary>
        internal event EventHandler<WintapMetricEventArgs> WintapMetric;
        internal class WintapMetricEventArgs : EventArgs
        {
            public MetricNameEnum MetricName { get; set; }
            public long TotalEvents { get; set; }
            internal long DroppedEvents { get; set; }
            internal string ParquetPath { get; set; }
        }
        protected virtual void OnWintapMetricEvent(WintapMetricEventArgs e)
        {
            EventHandler<WintapMetricEventArgs> handler = WintapMetric;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        internal event EventHandler<WintapStatusEventArgs> WintapStatus;
        internal class WintapStatusEventArgs : EventArgs
        {
            internal string? StatusDetail { get; set; }
        }
        protected virtual void OnWintapStatusEvent(WintapStatusEventArgs e)
        {
            EventHandler<WintapStatusEventArgs> handler = WintapStatus;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        private string wintapLogPath;
        private string etlLogPath;
        private BackgroundWorker sensorWorker;
        private System.Timers.Timer logReadTimer;

        internal Sensor()
        {

        }

        internal void Start()
        {
            logReadTimer = new System.Timers.Timer();
            sensorWorker = new BackgroundWorker();
            sensorWorker.DoWork += SensorWorker_DoWork;
            sensorWorker.WorkerSupportsCancellation = true;
            sensorWorker.RunWorkerAsync();
        }

        internal void Stop()
        {
            sensorWorker.CancelAsync();
            logReadTimer.Stop();
        }

        private void SensorWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            wintapLogPath = "C:\\ProgramData\\Wintap\\Logs\\Wintap.log";
            etlLogPath = "C:\\ProgramData\\Wintap\\Logs\\WintapETL.log";

            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Restarting Wintap sensor" });
            restartWintap();
            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Wintap restarted." });

            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Loading data collect plugin..." });
            System.Threading.Thread.Sleep(10000);

            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Reading sensor logs" });
            bool startupComplete = false;
            string wintapLog = "";
            FileStream stream = File.Open(wintapLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); ;
            while (!startupComplete)
            {
                try
                {
                    stream = File.Open(wintapLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        wintapLog = reader.ReadToEnd();
                        if (wintapLog.Contains("Startup complete."))
                        {
                            startupComplete = true;
                        }
                    }
                }
                catch (Exception ex)
                {

                }
                System.Threading.Thread.Sleep(1000);
                stream.Close();
                stream.Dispose();
            }

            startupComplete = false;
            string etlLog = "";
            while (!startupComplete)
            {
                using (stream = File.Open(etlLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream)
                        {
                            etlLog = reader.ReadToEnd();
                            if (etlLog.Contains("All sensors created"))
                            {
                                startupComplete = true;
                            }
                        }
                    }
                }
                System.Threading.Thread.Sleep(1000);
            }


            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Scanning logs for error conditions" });
            bool logOK = processLogChunk(wintapLog, "Wintap.log");
            if (logOK) { OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Wintap log OK." }); }
            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Waiting on process tree..." });
            if (processLogChunk(etlLog, "WintapETL.log")) { OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Process tree loaded, WintapETL log OK." }); }

            logReadTimer.Elapsed += LogReadTimer_Elapsed;
            logReadTimer.Interval = 1000;
            logReadTimer.AutoReset = true;
            logReadTimer.Start();

            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Initialization Complete." });
        }

        private void restartWintap()
        {
            ServiceController sc = new ServiceController("wintap");
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                System.Threading.Thread.Sleep(250);
            }

            while (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Refresh();
                System.Threading.Thread.Sleep(250);

            }

            sc.Start();
            System.Threading.Thread.Sleep(250);
            sc.Refresh();

            while (sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.Refresh();
                System.Threading.Thread.Sleep(250);
            }
        }

        private void LogReadTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            ReadTail(wintapLogPath);
            ReadTail(etlLogPath);

        }


        internal bool ReadTail(string filename)
        {
            bool logOK = false;
            try
            {
                using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(-1024, SeekOrigin.End);
                    byte[] bytes = new byte[1024];
                    fs.Read(bytes, 0, 1024);
                    string s = Encoding.Default.GetString(bytes);
                    Console.WriteLine(s);
                    logOK = processLogChunk(s, filename);
                }
            }
            catch (Exception Ex)
            {

            }
            return logOK;
        }

        /// <summary>
        /// returns True if log chunk contains no error
        /// </summary>
        /// <param name="s"></param>
        /// <param name="logName"></param>
        /// <returns></returns>
        internal bool processLogChunk(string s, string logName)
        {
            bool logChunkOK = true;
            EventArgs e = new EventArgs();
            string[] logLines = s.Split(new char[] { '\r' });
            foreach (string line in logLines)
            {
                // temporary shortcut
                if (line.ToLower().Contains(" "))
                {
                    OnOnProcessTreeReadyEvent(e);
                }
                else if (line.ToLower().Contains("error creating registry data object"))
                {
                    continue;  // registry collection is noisy, we'll just igore these errors for now.
                }
                else if (line.ToLower().Contains("error"))
                {
                    OnSensorErrorEvent(e);
                    OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Error detected in " + logName + ".  " + line });
                    logChunkOK = false;
                }
                else if (line.ToLower().Contains("total wintap messages received: "))
                {
                    try
                    {
                        string logMessage = line.Split(new char[] { ':' })[line.Split(new char[] { ':' }).Length - 1];
                        long metricValue = (long)Int64.Parse(logMessage.Trim());
                        WintapMetricEventArgs metricArgs = new WintapMetricEventArgs();
                        metricArgs.TotalEvents = metricValue;
                        metricArgs.MetricName = MetricNameEnum.TotalEventCount;
                        OnWintapMetricEvent(metricArgs);
                    }
                    catch (Exception ex)
                    {
                        OnSensorErrorEvent(e);
                        OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "WARN: could not get wintapmessage count " + ex.Message });
                    }
                }
                else if (line.ToLower().Contains("dropped event count on provider: "))
                {
                    try
                    {
                        string logMessage = line.Split(new char[] { ':' })[line.Split(new char[] { ':' }).Length - 1];
                        long metricValue = (long)Int64.Parse(logMessage.Trim());
                        WintapMetricEventArgs metricArgs = new WintapMetricEventArgs();
                        metricArgs.DroppedEvents = metricValue;
                        metricArgs.MetricName = MetricNameEnum.DroppedEventCount;
                        OnWintapMetricEvent(metricArgs);
                        if(metricValue > 0)
                        {
                            OnSensorErrorEvent(e);
                            OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "Error detected in " + logName + ".  " + line });
                            logChunkOK = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        OnSensorErrorEvent(e);
                        OnWintapStatusEvent(new WintapStatusEventArgs() { StatusDetail = "WARN could not get dropped event count " + ex.Message });
                    }
                }
            }
            return logChunkOK;
        }
    }
}
