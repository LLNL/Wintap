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
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;

namespace WintapRecorder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        internal enum SessionStateEnum { Stopped, Started, Starting, Stopping, Error, TimeOut };
        private bool processTreeOK = false;
        private bool logsOK = true;
        private bool eventsStreamOK = false;
        private long lastTotalEventMetric = 0;
        private int lastTotalParquetMetric = 0;
        private Session session;
        private Sensor sensor;
        private FileInfo wintapConfigInfo;
        private XmlDocument config;
        internal SessionStateEnum SessionState;


        public MainWindow()
        {
            InitializeComponent();
            initEventScroller();
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            CheckBox cb = sender as CheckBox;
            XmlNodeList settings = config.GetElementsByTagName("setting");
            foreach (XmlNode setting in settings)
            {
                if (setting.Attributes["name"].Value == cb.Tag.ToString())
                {
                    setting.FirstChild.InnerText = "False";
                }
            }
            using (FileStream fs = new FileStream(wintapConfigInfo.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                config.Save(fs);
                fs.SetLength(fs.Position);
                fs.Close();
            }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox cb = sender as CheckBox;
            XmlNodeList settings = config.GetElementsByTagName("setting");
            foreach (XmlNode setting in settings)
            {
                if (setting.Attributes["name"].Value == cb.Tag.ToString())
                {
                    setting.FirstChild.InnerText = "True";
                }
            }
            using (FileStream fs = new FileStream(wintapConfigInfo.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                config.Save(fs);
                fs.SetLength(fs.Position);
                fs.Close();
            }
        }

        private void startBtn_Click(object sender, RoutedEventArgs e)
        {
            clearLocalParquetCache();
            SessionState = SessionStateEnum.Starting;
            startTimeData.Content = "NA";
            openDir.IsEnabled = false;
            lastTotalEventMetric = 0;
            lastTotalParquetMetric = 0;
            base.Dispatcher.Invoke(delegate
            {
                stopBtn.IsEnabled = true;
                startBtn.IsEnabled = false;
                foreach (object current in EventCollectors.Children)
                {
                    CheckBox checkBox = (CheckBox)current;
                    checkBox.IsEnabled = false;
                }
            });
            statusDetail.Text = "";
            statusData.Text = "Initializing collect...   Please wait.";
            statusData.Foreground = Brushes.DodgerBlue;
            eventCountData.Content = 0;
            parquetData.Content = 0;
            droppedCountData.Content = 0;
            sensor = new Sensor();
            sensor.WintapMetric += Sensor_WintapMetric;
            sensor.ProcessTreeReady += Sensor_ProcessTreeReady;
            sensor.SensorError += Sensor_SensorError;
            sensor.WintapStatus += Sensor_WintapStatus;
            sensor.Start();
            Timer initTimer = new Timer(120000.0);
            initTimer.Elapsed += InitTimer_Elapsed;
            initTimer.AutoReset = false;
            initTimer.Start();

        }

        private void Session_SessionMetric(object? sender, Session.SessionMetricEventArgs e)
        {
            Session.SessionMetricEventArgs e2 = e;
            if (e2.MetricName == Session.SessionMetricEnum.TotalParquetCount)
            {
                base.Dispatcher.Invoke(delegate
                {
                    if(e2.TotalParquetCount > lastTotalParquetMetric)
                    {
                        int prevVal = Convert.ToInt32(this.parquetData.Content);
                        this.parquetData.Content = prevVal + e2.TotalParquetCount;
                    }
                    if(e2.TotalParquetCount > 0)
                    {
                        if (SessionState == SessionStateEnum.Stopping && e2.TotalParquetCount > lastTotalParquetMetric)
                        {
                            BackgroundWorker finalParquetWaitWorker = new BackgroundWorker();
                            finalParquetWaitWorker.DoWork += FinalParquetWaitWorker_DoWork;
                            finalParquetWaitWorker.RunWorkerCompleted += FinalParquetWaitWorker_RunWorkerCompleted;
                            finalParquetWaitWorker.RunWorkerAsync();
                        }
                    }
                });
                lastTotalParquetMetric = e2.TotalParquetCount;
            }
            if (e2.MetricName == Session.SessionMetricEnum.MergedParquetCount)
            {
                base.Dispatcher.Invoke(delegate
                {
                    this.recordingData.Content = e2.MergedParquetCount;
                    if (e2.MergedParquetCount > 0)
                    {
                        this.openDir.Tag = e2.SessionPath;
                        this.openDir.IsEnabled = true;
                    }
                });
            }
        }

        private void clearLocalParquetCache()
        {
            DirectoryInfo streamingParquetDir = new DirectoryInfo(Strings.StreamingParquetDir);
            if (streamingParquetDir.Exists)
            {
                streamingParquetDir.Delete(true);
            }
            streamingParquetDir.Create();
        }

        private void FinalParquetWaitWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            statusDetail.AppendText("Merging parquet... \r\n");
            try
            {
                DirectoryInfo cacheDir = new DirectoryInfo(Strings.StreamingParquetDir);
                session.Merge(cacheDir);
            }
            catch (Exception ex)
            {
                SessionState = SessionStateEnum.Error;
                statusData.Text = "Parquet merge error.";
                statusData.Foreground = Brushes.Red;
                statusDetail.AppendText("Error merging parquet: " + ex.Message);
                stopFinalize();
            }
            stopFinalize();
        }

        private void FinalParquetWaitWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            System.Threading.Thread.Sleep(10000);
        }

        private void initEventScroller()
        {
            stopBtn.IsEnabled = false;
            EventCollectors.Children.Clear();
            RegistryKey wintapSvcKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\Wintap");
            string wintapPath = wintapSvcKey.GetValue("ImagePath")!.ToString();
            string configPath = wintapPath.Replace(".exe", ".exe.config");
            configPath = configPath.Replace("\"", "");
            wintapConfigInfo = new FileInfo(configPath);
            if (!wintapConfigInfo.Exists)
            {
                return;
            }
            config = new XmlDocument();
            config.Load(wintapConfigInfo.FullName);
            foreach (XmlElement configValue in config.GetElementsByTagName("setting"))
            {
                if (configValue.GetAttribute("name").Contains("Collector"))
                {
                    string collectorName = configValue.GetAttribute("name").Replace("Collector", "");
                    if (collectorName.ToUpper().Contains("LANDESK")) { continue; }
                    if (collectorName.ToUpper().Contains("WEBACTIVITY")) { continue; }
                    collectorName = collectorName.Replace("MicrosoftWindowsKernelProcess", "ProcessTerminate");
                    collectorName = collectorName.Replace("MicrosoftWindows", "");
                    collectorName = collectorName.Replace("Kernel", "");
                    collectorName = collectorName.Replace("Win32k", "FocusChange");
                    collectorName = collectorName.Replace("Sens", "ScreenLock");
                    CheckBox checkBox = new CheckBox();
                    checkBox.Tag = configValue.GetAttribute("name");
                    if (bool.Parse(configValue.FirstChild!.InnerText))
                    {
                        checkBox.IsChecked = true;
                    }
                    checkBox.Content = collectorName;
                    checkBox.Name = collectorName;
                    checkBox.Checked += CheckBox_Checked;
                    checkBox.Unchecked += CheckBox_Unchecked;
                    EventCollectors.Children.Add(checkBox);
                }
            }
            foreach (XmlElement configValue in config.GetElementsByTagName("setting"))
            {
                if (configValue.GetAttribute("name").Contains("CollectFileRead") || configValue.GetAttribute("name").Contains("CollectRegistryRead"))
                {
                    string collectorName = configValue.GetAttribute("name").Replace("Collect", "");
                    CheckBox checkBox = new CheckBox();
                    checkBox.Tag = configValue.GetAttribute("name");
                    if (bool.Parse(configValue.FirstChild!.InnerText))
                    {
                        checkBox.IsChecked = true;
                    }
                    checkBox.Content = collectorName;
                    checkBox.Name = collectorName;
                    checkBox.Checked += CheckBox_Checked;
                    checkBox.Unchecked += CheckBox_Unchecked;
                    EventCollectors.Children.Add(checkBox);
                }
            }
        }

        private void InitTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!processTreeOK)
            {
                MessageBox.Show("Wintap could not create a complete process tree.  Please reboot this computer and retry");
            }
            base.Dispatcher.Invoke(delegate
            {
                if (SessionState == SessionStateEnum.Starting)
                {
                    SessionState = SessionStateEnum.TimeOut;
                    statusData.Text = "Initalization timed out.";
                    statusData.Foreground = Brushes.Red;
                    statusDetail.AppendText("Restart collect or reboot computer and then restart collect");
                    stopFinalize();
                }
            });

        }

        private void stopBtn_Click(object sender, RoutedEventArgs e)
        {
            base.Dispatcher.Invoke(delegate
            {
                stopBtn.IsEnabled = false;
            });
            statusDetail.AppendText("Stop collection requested at: " + DateTime.Now.ToString() + "\r\n");
            if (SessionState == SessionStateEnum.Started)
            {
                SessionState = SessionStateEnum.Stopping;
                statusDetail.AppendText("waiting for final parquet to flush to disk... \r\n");
                statusData.Text = "Finalizing collect.  Please wait...";
                statusData.Foreground = Brushes.DodgerBlue;
            }
            else
            {
                SessionState = SessionStateEnum.Stopping;
                stopFinalize();
            }
        }

        private void stopFinalize()
        {
            if(SessionState != SessionStateEnum.TimeOut)
            {
                session.Stop();
            }
            base.Dispatcher.Invoke(delegate
            {
                stopBtn.IsEnabled = false;
                startBtn.IsEnabled = true;
                foreach (object current in EventCollectors.Children)
                {
                    CheckBox checkBox = (CheckBox)current;
                    checkBox.IsEnabled = true;
                }
                if (SessionState == SessionStateEnum.Stopping)  // only reset status on clean stop.  leave errors and timeout messages on screen
                {
                    statusData.Text = "Recording Not Started";
                    statusData.Foreground = Brushes.Gray;
                }
                statusDetail.AppendText("Collect stopped at: " + DateTime.Now.ToString() + "\r\n");
            });
            SessionState = SessionStateEnum.Stopped;
            sensor.Stop();
        }

        private void openDir_Click(object sender, RoutedEventArgs e)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                Arguments = openDir.Tag.ToString(),
                FileName = "explorer.exe"
            };
            Process.Start(startInfo);
        }

        private void Sensor_WintapStatus(object? sender, Sensor.WintapStatusEventArgs e)
        {
            Sensor.WintapStatusEventArgs e2 = e;
            base.Dispatcher.Invoke(delegate
            {
                statusDetail.AppendText(e2.StatusDetail + "\r\n");
                statusDetail.Focus();
                statusDetail.CaretIndex = statusDetail.Text.Length;
                statusDetail.ScrollToEnd();
            });
        }

        private void Sensor_SensorError(object? sender, EventArgs e)
        {
            logsOK = false;
            base.Dispatcher.Invoke(delegate
            {
                statusData.Text = "SENSOR ERROR";
                statusData.Foreground = Brushes.Red;
            });
            SessionState = SessionStateEnum.Error;
            stopFinalize();
        }

        private void Sensor_ProcessTreeReady(object? sender, EventArgs e)
        {
            processTreeOK = true;
            if (SessionState == SessionStateEnum.Starting)
            {
                session = new Session();
                session.SessionMetric += Session_SessionMetric;
                session.Start();
                SessionState = SessionStateEnum.Started;
                if (lastTotalEventMetric > 0 || logsOK)
                {
                    base.Dispatcher.Invoke(delegate
                    {
                        statusData.Text = "COLLECTING DATA";
                        statusData.Foreground = Brushes.LimeGreen;
                        startTimeData.Content = session.SessionStartTime.ToLocalTime();
                    });
                }
            }

        }

        private void Sensor_WintapMetric(object? sender, Sensor.WintapMetricEventArgs e)
        {
            Sensor.WintapMetricEventArgs e2 = e;
            if (e2.MetricName == Sensor.MetricNameEnum.TotalEventCount)
            {
                if (e2.TotalEvents > lastTotalEventMetric)
                {
                    eventsStreamOK = true;
                    lastTotalEventMetric = e2.TotalEvents;
                    base.Dispatcher.Invoke(delegate
                    {
                        eventCountData.Content = lastTotalEventMetric;
                    });
                }
                else
                {
                    eventsStreamOK = false;
                }
            }

            if (e2.MetricName == Sensor.MetricNameEnum.DroppedEventCount && e2.DroppedEvents > 0)
            {
                eventsStreamOK = false;
                base.Dispatcher.Invoke(delegate
                {
                    droppedCountData.Content = e2.DroppedEvents;
                });
            }
            if (e2.MetricName == Sensor.MetricNameEnum.ParquetCount)
            {
                FileInfo parquetInfo = new FileInfo(e2.ParquetPath);
                DirectoryInfo parquetDir = parquetInfo.Directory;
                int parquetCount = parquetDir.GetFiles("*.parquet", SearchOption.AllDirectories).Length;
                base.Dispatcher.Invoke(delegate
                {
                    int existingValue = Convert.ToInt32(parquetData.Content);
                    parquetData.Content = existingValue + parquetCount;
                    openDir.Tag = parquetDir.FullName;
                    openDir.IsEnabled = true;
                });
            }
        }

    }
}
