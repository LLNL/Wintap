using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static WintapRecorder.MainWindow;
using System.Windows.Media;
using static WintapRecorder.Sensor;

namespace WintapRecorder
{
    /// <summary>
    /// </summary>
    internal class Session
    {

        public enum SessionMetricEnum { TotalParquetCount, MergedParquetCount };
        internal bool IsMerging;

        /// <summary>
        /// When the total event count metric is updated in the WintapETL log
        /// </summary>
        internal event EventHandler<SessionMetricEventArgs> SessionMetric;
        internal class SessionMetricEventArgs : EventArgs
        {
            public SessionMetricEnum MetricName { get; set; }
            internal int TotalParquetCount { get; set; }
            internal int MergedParquetCount { get; set; }
            internal string SessionPath { get; set; }
        }
        protected virtual void OnSessionMetricEvent(SessionMetricEventArgs e)
        {
            EventHandler<SessionMetricEventArgs> handler = SessionMetric;
            if (handler != null)
            {
                handler(this, e);
            }
        }


        internal DateTime sessionStartTime;
        private BackgroundWorker sessionWorker;
        private bool sessionRunning;
        private Stopwatch mergeTimer;

        public DateTime SessionStartTime
        {
            get { return sessionStartTime; }
        }


        private string recordingSessionName;
        public string SessionName
        {
            get { return recordingSessionName; }
        }

        internal void Start()
        {
            sessionStartTime = DateTime.UtcNow;
            sessionRunning = true;
            mergeTimer = new Stopwatch();
            sessionWorker = new BackgroundWorker();
            sessionWorker.DoWork += SessionWorker_DoWork;
            sessionWorker.WorkerSupportsCancellation = true;
            sessionWorker.RunWorkerAsync();

        }

        private void SessionWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            // create session
            RegistryKey sessionKey = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Wintap\\Plugins\\WintapETL\\Sessions");
            sessionKey.SetValue("Mode", "Record");
            sessionKey.SetValue("RecordStartTime", sessionStartTime);
            sessionKey.Flush();

            DateTime sessionStartTimeLowPrecision = DateTime.Parse(sessionKey.GetValue("RecordStartTime").ToString());
            mergeTimer.Start();
            recordingSessionName = Environment.MachineName.ToUpper() + "-" + sessionStartTimeLowPrecision.ToFileTimeUtc().ToString();

            sessionKey.Close();
            sessionKey.Dispose();

            while (sessionRunning)
            {
                System.Threading.Thread.Sleep(1000);
                DirectoryInfo sessionDir = new DirectoryInfo(Strings.StreamingParquetDir);
                if (sessionDir.Exists)
                {
                    int parquetCount = sessionDir.GetFiles("*.parquet", SearchOption.AllDirectories).Length;
                    if (parquetCount > 0)
                    {
                        OnSessionMetricEvent(new SessionMetricEventArgs() { MetricName = SessionMetricEnum.TotalParquetCount, TotalParquetCount = parquetCount, SessionPath = sessionDir.FullName });
                    }
                }
                DirectoryInfo recordingDir = new DirectoryInfo(Strings.RecordingsDir + recordingSessionName);
                if (recordingDir.Exists)
                {
                    int parquetRCount = recordingDir.GetFiles("*.parquet", SearchOption.AllDirectories).Length;
                    if (parquetRCount > 0)
                    {
                        OnSessionMetricEvent(new SessionMetricEventArgs() { MetricName = SessionMetricEnum.MergedParquetCount, MergedParquetCount = parquetRCount, SessionPath = recordingDir.FullName });
                    }
                }
            }
        }

        private void createMetaRecords()
        {
            
        }

        internal void Stop()
        {
            sessionRunning = false;
            sessionWorker.CancelAsync();
            try
            {
                RegistryKey sessionKey = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Wintap\\Plugins\\WintapETL\\Sessions");
                if(sessionKey.ValueCount > 0)
                {
                    sessionKey.DeleteValue("Mode");
                    sessionKey.DeleteValue("RecordStartTime");
                    sessionKey.Flush();
                }
                sessionKey.Close();
                sessionKey.Dispose();
            }
            catch(Exception ex)
            {
                MessageBox.Show("Could not set stop flag in registry.  Parquet may accumulate in session directory.  reason: " + ex.Message);
            }


        }

        internal void Merge(DirectoryInfo cacheDir)
        {

            DateTime mergeTime = DateTime.UtcNow;
            foreach (DirectoryInfo sensorDir in cacheDir.GetDirectories())
            {
                if (sensorDir.Name.ToUpper() == "CSV") { continue; }
                if (sensorDir.FullName.ToUpper().Contains("MERGED")) { continue; }
                if (sensorDir.Name.ToLower() == "gov.llnl.wintap.etl.extract.default_sensor")
                {
                    foreach (DirectoryInfo defaultSensor in sensorDir.GetDirectories())
                    {
                        if (defaultSensor.FullName.ToUpper().Contains("MERGED")) { continue; }
                        runCmdLine(defaultSensor.FullName, mergeTime.ToFileTimeUtc());
                    }
                }
                else
                {
                    runCmdLine(sensorDir.FullName, mergeTime.ToFileTimeUtc());
                }

            }
        }

        private void runCmdLine(string path, long eventTime)
        {

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = new FileInfo(Assembly.GetExecutingAssembly().FullName).Directory.Parent.FullName + @"\mergertool\MergeHelper.exe";
            psi.Arguments = path + " " + eventTime;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process helperExe = new Process();
            helperExe.StartInfo = psi;
            helperExe.Start();
            helperExe.WaitForExit();
        }
    }
}
