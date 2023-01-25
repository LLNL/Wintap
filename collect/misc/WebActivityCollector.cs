/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using com.espertech.esper.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.collect.shared;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;


namespace gov.llnl.wintap.collect
{
    internal class WebActivityCollector : BaseCollector
    {
        private DateTime mostRecentActivity;
        private DateTime mostRecentActivityFF;
        private DateTime mostRecentActivityEdge;
        BackgroundWorker browserPollingWorker;

        public WebActivityCollector() : base()
        {
            // For ETW events set source name here to be the Event Provider name
            this.CollectorName = "WebActivity";

            mostRecentActivity = DateTime.Now.AddMinutes(-10).ToUniversalTime();
            mostRecentActivityEdge = mostRecentActivity;
            mostRecentActivityFF = mostRecentActivity;
            browserPollingWorker = new System.ComponentModel.BackgroundWorker();
            browserPollingWorker.DoWork += browserPollingWorker_DoWork;
            browserPollingWorker.RunWorkerCompleted += browserPollingWorker_RunWorkerCompleted;
            browserPollingWorker.RunWorkerAsync();
        }

        void browserPollingWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            gatherBrowserInfo();
            browserPollingWorker.RunWorkerAsync();
        }

        void browserPollingWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            System.Threading.Thread.Sleep(1000);
        }

        private void gatherBrowserInfo()
        {
            string user = getUser();
            // gather Chrome
            try
            {
                if(user.ToUpper() == "PHOTONUSER")
                {
                    string chromeHistoryPath = Environment.GetEnvironmentVariable("SYSTEMDRIVE") + "\\users\\" + user + @"\AppData\Local\Google\Chrome\User Data\Default\History";
                    WintapLogger.Log.Append("Attempting to load Chrome history data from appstream file: " + chromeHistoryPath, LogLevel.Always);
                    gatherChrome(chromeHistoryPath);
                }
                else
                {
                    string chromeProfileName = getChromeProfileName();  
                    string chromeHistoryPath = Environment.GetEnvironmentVariable("SYSTEMDRIVE") + "\\users\\" + user + @"\AppData\Local\Google\Chrome\User Data\" + chromeProfileName + @"\History";
                    gatherChrome(chromeHistoryPath);
                }
            }
            catch(FileNotFoundException fnoEx)
            {
                
            }
            catch(Exception ex)
            {
               WintapLogger.Log.Append("Error in chrome activity gather: " + ex.Message, LogLevel.Always);
            }

            // gather Firefox
            try
            {
                DirectoryInfo ffRoot = new DirectoryInfo(Environment.GetEnvironmentVariable("SYSTEMDRIVE") + "\\users\\" + user + @"\AppData\Roaming\Mozilla\Firefox\Profiles");
                string ffProfile = ffRoot.EnumerateDirectories().FirstOrDefault().FullName;
                if(user.ToUpper() == "PHOTONUSER")
                {
                    foreach (DirectoryInfo ffDir in ffRoot.EnumerateDirectories())
                    {
                        if (ffDir.FullName.ToUpper().EndsWith("-RELEASE"))
                        {
                            ffProfile = ffDir.FullName;
                        }
                    }
                }
                gatherFireFox(ffProfile + "\\places.sqlite");
            }
            catch(FileNotFoundException fnoEx)
            {

            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("Error in firefox activity gather: " + ex.Message, LogLevel.Always);
            }

            // Gather Edge
            try
            {
                if (user.ToUpper() == "PHOTONUSER")
                {
                    string edgeHistoryPath = Environment.GetEnvironmentVariable("SYSTEMDRIVE") + "\\users\\" + user + @"\AppData\Local\Microsoft\Edge\User Data\Default\History";
                    WintapLogger.Log.Append("Attempting to load Edge history data from appstream file: " + edgeHistoryPath, LogLevel.Always);
                    gatherEdge(edgeHistoryPath);
                }
                else
                {
                    string edgeProfileName = getEdgeProfileName();  // will get file not found exception if no chrome
                    string edgeHistoryPath = Environment.GetEnvironmentVariable("SYSTEMDRIVE") + "\\users\\" + user + @"\AppData\Local\Microsoft\Edge\User Data\" + edgeProfileName + @"\History";
                    gatherEdge(edgeHistoryPath);
                }

            }
            catch (FileNotFoundException fnoEx)
            {
                // do nothing
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error edge activity gather: " + ex.Message, LogLevel.Always);
            }

        }

        private string getChromeProfileName()
        {
            string chromeSettingsFile = Environment.GetEnvironmentVariable("SYSTEMDRIVE") + "\\users\\" + getUser() + @"\AppData\Local\Google\Chrome\User Data\Local State";
            if(StateManager.ActiveUser.ToUpper() == "PHOTONUSER")
            {
                chromeSettingsFile = chromeSettingsFile.Replace("\\Local State", "\\Default");
            }
            string json = File.ReadAllText(chromeSettingsFile);
            dynamic jsonObject = JObject.Parse(json);
            return jsonObject.profile.last_used;
        }

        private string getEdgeProfileName()
        {
            string edgeSettingsFile = Environment.GetEnvironmentVariable("SYSTEMDRIVE") + "\\users\\" + getUser() + @"\AppData\Local\Microsoft\Edge\User Data\Local State";
            if (StateManager.ActiveUser.ToUpper() == "PHOTONUSER")
            {
                edgeSettingsFile = edgeSettingsFile.Replace("\\Local State", "\\Default");
            }
            string json = File.ReadAllText(edgeSettingsFile);
            dynamic jsonObject = JObject.Parse(json);
            return jsonObject.profile.last_used;
        }

        /// <summary>
        /// Returns the currently logged in username (stripping the DOMAIN portion out)
        /// </summary>
        public string getUser()
        {
            string currentUser = "None";
            try
            {
                ManagementObjectSearcher mos = new ManagementObjectSearcher("root\\cimV2", "Select * from WIN32_ComputerSystem");
                foreach (ManagementObject mo in mos.Get())
                {
                    string userName = mo.Properties["UserName"].Value.ToString();
                    string[] cuArray = userName.Split(new Char[] { '\\' });
                    currentUser = cuArray[1].ToString();
                }
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("ERROR obtaining current username from WMI: " + ex.Message, LogLevel.Always);
            }
            if(currentUser == "None")
            {
                WintapLogger.Log.Append("Unable to resolve username, evaluating environment for special conditions...", LogLevel.Always);
                DirectoryInfo profileRoot = new DirectoryInfo("C:\\Users");
                foreach(DirectoryInfo profileDir in profileRoot.GetDirectories())
                {
                    if(profileDir.Name.ToUpper() == "PHOTONUSER")
                    {
                        currentUser = "PhotonUser";
                    }
                }
            }
            WintapLogger.Log.Append("getUser returning: " + currentUser, LogLevel.Always);
            return currentUser;
        }

        private void gatherChrome(string sqlLitePath)
        {
            string filePath = copyDB(sqlLitePath);
            WintapLogger.Log.Append("Attempting to gather CHROME from file: " + filePath, LogLevel.Always);
            try
            {
                SQLiteConnection conn = new SQLiteConnection("Data Source=" + filePath);
                conn.Open();
                SQLiteCommand cmd = new SQLiteCommand();
                cmd.Connection = conn;
                cmd.CommandText = "Select * From urls order by last_visit_time asc";
                cmd.CommandTimeout = 10;
                SQLiteDataReader dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    DateTime lastVisit = convertGoogleTime((long)Convert.ToInt64(dr["last_visit_time"].ToString()));
                    if (lastVisit > mostRecentActivity)
                    {
                        System.Diagnostics.Process browserProcess = System.Diagnostics.Process.GetProcessById(StateManager.PidFocus);
                        if(browserProcess.ProcessName.ToLower().StartsWith("chrome"))
                        {
                            sendBrowserEvent(lastVisit, browserProcess.Id, "chrome", dr["url"].ToString(), dr["title"].ToString());
                        }
                        mostRecentActivity = lastVisit;
                    }

                }
                conn.Close();
            }
            catch(Exception ex)
            {
                WintapLogger.Log.Append("ERROR gathering chrome: " + ex.Message, LogLevel.Always);
            }
        }

        private void gatherEdge(string sqlLitePath)
        {
            string filePath = copyDB(sqlLitePath);
            //string filePath = sqlLitePath;
            WintapLogger.Log.Append("Attempting to gather EDGE from file: " + filePath, LogLevel.Always);
            try
            {
                SQLiteConnection conn = new SQLiteConnection("Data Source=" + filePath);
                conn.Open();
                SQLiteCommand cmd = new SQLiteCommand();
                cmd.Connection = conn;
                cmd.CommandText = "Select * From urls order by last_visit_time asc";
                cmd.CommandTimeout = 10;
                SQLiteDataReader dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    DateTime lastVisit = convertGoogleTime((long)Convert.ToInt64(dr["last_visit_time"].ToString()));
                    if (lastVisit > mostRecentActivityEdge)
                    {
                        System.Diagnostics.Process browserProcess = System.Diagnostics.Process.GetProcessById(StateManager.PidFocus);
                        if (browserProcess.ProcessName.ToLower().StartsWith("msedge"))
                        {
                            sendBrowserEvent(lastVisit, browserProcess.Id, "edge", dr["url"].ToString(), dr["title"].ToString());
                        }
                        mostRecentActivityEdge = lastVisit;
                    }

                }
                conn.Close();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR gathering edge: " + ex.Message, LogLevel.Always);
            }
        }

        private void gatherFireFox(string sqlLitePath)
        {
            try
            {
                //string filePath = copyDB(sqlLitePath);
                string filePath = sqlLitePath;  //  firefox seems to allow reading of the live DB - which is faster...
                WintapLogger.Log.Append("Attempting to gather firefox from file: " + filePath, LogLevel.Always);
                SQLiteConnection conn = new SQLiteConnection("Data Source=" + filePath);
                conn.Open();
                SQLiteCommand cmd = new SQLiteCommand();
                cmd.Connection = conn;
                cmd.CommandText = "Select * From moz_places";
                cmd.CommandTimeout = 10;
                SQLiteDataReader dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    try
                    {
                        string lastVisitStr = dr["last_visit_date"].ToString();
                        DateTime lastVisit = convertMozillaTime((long)Convert.ToInt64(lastVisitStr));
                        lastVisit = lastVisit.ToUniversalTime();
                        string url = dr["url"].ToString();
                        if (lastVisit > mostRecentActivityFF)
                        {
                            System.Diagnostics.Process browserProcess = System.Diagnostics.Process.GetProcessById(StateManager.PidFocus);
                            if (browserProcess.ProcessName.ToLower().StartsWith("firefox"))
                            {
                                sendBrowserEvent(lastVisit, browserProcess.Id, "firefox", dr["url"].ToString(), dr["title"].ToString());
                            }
                            else
                            {
                                WintapLogger.Log.Append("FIREFOX DOES NOT HAVE FOCUS.   Current focus is: " + StateManager.PidFocus, LogLevel.Always);
                            }
                            mostRecentActivityFF = lastVisit;
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }
                conn.Close();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR gathering firefox: " + ex.Message,LogLevel.Always);
            }

        }

        private void sendBrowserEvent(DateTime eventTime, int pid, string browser, string url, string title)
        {
            WintapLogger.Log.Append("Sending browser event! " + browser + ": " + url + ", title: " + title + " PID: " + pid, LogLevel.Always);
            WintapMessage wm = new WintapMessage(eventTime, pid, this.CollectorName);
            WintapMessage.WebActivityData browserEvent = new WintapMessage.WebActivityData();
            browserEvent.Browser = browser.ToString();
            browserEvent.TabTitle = title;
            browserEvent.Url = url;
            browserEvent.UserName = StateManager.ActiveUser;
            if(browserEvent.UserName.ToUpper() == "PHOTONUSER")
            {
                browserEvent.UserName = resolvePhotonUser();
            }
            wm.ActivityType = "VISIT";
            wm.WebActivity = browserEvent;
            EventChannel.Send(wm); 
        }

        private string resolvePhotonUser()
        {
            string user = "PhotonUser";
            WintapLogger.Log.Append("Attempting to map PhotonUser...", LogLevel.Always);
            RegistryKey usersRoot = Registry.Users;
            foreach (var userKey in usersRoot.GetSubKeyNames())
            {
                try
                {
                    if (userKey.StartsWith("S-1-5-21-"))
                    {
                        RegistryKey currentUserKey = usersRoot.OpenSubKey(userKey);

                        if (currentUserKey.GetSubKeyNames().Contains("Environment"))
                        {
                            RegistryKey envKey = currentUserKey.OpenSubKey("Environment");
                            user = envKey.GetValue("AppStream_UserName").ToString();
                            envKey.Close();
                            envKey.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Error reading environment for key: " + userKey + "  exception: " + ex.Message, LogLevel.Always);
                }
            }
            WintapLogger.Log.Append("PhotonUser resolution complete.  User: " + user, LogLevel.Always);
            return user;
        }

        private string copyDB(string filePath)
        {
            FileInfo sourceDB = new FileInfo(filePath);
            FileInfo copyDB = new FileInfo(filePath + "2");
            sourceDB.CopyTo(copyDB.FullName, true);
            return copyDB.FullName;
        }

        /// <summary>
        /// credit:  http://stackoverflow.com/questions/18898652/in-what-format-does-google-chrome-store-extension-install-dates
        /// </summary>
        /// <param name="rawTime"></param>
        /// <returns></returns>
        private DateTime convertGoogleTime(long rawTime)
        {
            DateTime googleDate = new DateTime(1601, 1, 1).AddSeconds(rawTime/1000000);
            return googleDate;
        }

        /// <summary>
        /// credit: http://stackoverflow.com/questions/19429577/converting-the-date-within-the-places-sqlite-file-in-firefox-to-a-datetime
        /// </summary>
        /// <param name="unixTime"></param>
        /// <returns></returns>
        private DateTime convertMozillaTime(long unixTime)
        {
            unixTime = unixTime / 1000000;
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(unixTime).ToLocalTime();
        }
    }
}
