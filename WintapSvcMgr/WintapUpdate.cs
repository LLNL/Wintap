using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace gov.llnl.wintap
{
    internal class WintapUpdate
    {

        internal bool ApplyUpdates()
        {
            bool changesApplied = false;
            bool updateThis = false; // special handling for updating the updater.
            int updateCount = 0;
            string thisName = "WintapSvcMgr.exe";
            List<Update> updates = new List<Update>();  // the relevant update list

            string urlString = "http://gov.llnl.wintap.update.s3-website-us-gov-west-1.amazonaws.com";

            Logger.Log.Append("Wintap update check is starting...");
            Logger.Log.Append("   update path: " + urlString);

            Uri rootUrl = new Uri(urlString);
            Uri url = new Uri(urlString + "/VersionInfo.json");
            dynamic versionInfo;
            try
            {
                var json = new WebClient().DownloadString(url);
                versionInfo = JsonConvert.DeserializeObject<List<ExpandoObject>>(json);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Could not find Version information file.  error: " + ex.Message);
                Logger.Log.Close();
                return changesApplied;
            }

            Logger.Log.Append("Scanning for updates...");
            foreach (var component in versionInfo)
            {
                try
                {
                    Logger.Log.Append("checking component: " + component.name);
                    Logger.Log.Append("    remote version: " + component.version);
                    string[] remoteVersionParts = component.version.Split('.');
                    FileInfo localComponentInfo = new FileInfo(Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles) + "\\" + component.location + "\\" + component.name);
                    if (localComponentInfo.Exists)
                    {
                        FileVersionInfo localVersionInfo = FileVersionInfo.GetVersionInfo(localComponentInfo.FullName);
                        Version localVersion;
                        if (component.name.ToLower() == "wintap.exe.config")
                        {
                            localVersion = parseConfigVersion();
                        }
                        else
                        {
                            localVersion = new Version(localVersionInfo.FileMajorPart, localVersionInfo.FileMinorPart, localVersionInfo.FileBuildPart, localVersionInfo.FilePrivatePart);
                        }
                        
                        Version remoteVersion = new Version(Int32.Parse(remoteVersionParts[0]), Int32.Parse(remoteVersionParts[1]), Int32.Parse(remoteVersionParts[2]), Int32.Parse(remoteVersionParts[3]));
                        if (component.name.ToLower() != "wintap.exe.config")
                        {
                            Logger.Log.Append("    local version: " + FileVersionInfo.GetVersionInfo(localComponentInfo.FullName).FileVersion.ToString());
                        }
                        if (remoteVersion > localVersion)
                        {
                            Logger.Log.Append("    Relevant update detected: " + component.name);
                            Update update = new Update();
                            update.Name = component.name;
                            update.LocalPath = localComponentInfo.FullName;
                            if (update.Name.ToLower() == "wintapsvcmgr.exe")
                            {
                                updateThis = true;
                                update.LocalPath = Environment.GetEnvironmentVariable("WINDIR") + "\\Temp\\" + thisName;
                            }
                            updates.Add(update);
                        }
                    }
                    else
                    {
                        Logger.Log.Append("    Relevant new component detected:  " + component.name);
                        //downloadComponent(component.name, localComponentInfo.FullName, rootUrl);
                        Update update = new Update() { Name = component.name, LocalPath = localComponentInfo.FullName };
                        updates.Add(update);
                    }
                }
                catch(Exception ex)
                {
                    Logger.Log.Append("Error applying update on " + component.name + ",  msg: " + ex.Message);
                }
                
            }
            Logger.Log.Append("Scan Complete.  Updates required: " + updates.Count);

            if (updates.Count > 0)
            {
                Logger.Log.Append("Applying updates...");
                if (updateThis)
                {
                    Logger.Log.Append("     Attempting update on WintapSvcMgr...");
                    applyPatch(thisName, Environment.GetEnvironmentVariable("WINDIR") + "\\temp\\wintapsvcmgr.exe", rootUrl);
                    selfUpdate(thisName, rootUrl);
                    if (updates.Count > 1)
                    {
                        Logger.Log.Append("     WintapSvcMgr patched. remaining updates will be applied at next run of WintapSvcMgr.");
                    }
                }
                else
                {
                    if(WintapController.StopWintap())
                    {
                        foreach (Update update in updates)
                        {
                            Logger.Log.Append("    Attempting update on component: " + update.Name);
                            changesApplied = true;
                            applyPatch(update.Name, update.LocalPath, rootUrl);
                        }
                    }
                }
            }

            if (changesApplied)
            {
                WintapController.StartWintap();
            }
            Logger.Log.Append("Wintap Update complete.  Changes applied: " + changesApplied);
            return changesApplied;
        }

        private Version parseConfigVersion()
        {
            Logger.Log.Append("Starting config parser...");
            Version configVersion = new Version(0, 0, 0, 0);
            XmlDocument configDoc = new XmlDocument();
            string configPath = AppDomain.CurrentDomain.BaseDirectory + "\\Wintap.exe.config";
            Logger.Log.Append("config path: " + configPath);
            FileInfo configInfo = new FileInfo(configPath);
            if(configInfo.Exists)
            {
                configDoc.Load(configInfo.FullName);

                if(File.ReadAllText(configInfo.FullName).Contains("setting name=\"ConfigVersion\""))
                {
                    string versionString = configDoc.SelectNodes("//setting[@name='ConfigVersion']")[0].FirstChild.InnerText;
                    Logger.Log.Append("version string from config: " + versionString);
                    string[] versionArray = versionString.Split(new char[] { '.' });
                    configVersion = new Version(int.Parse(versionArray[0]), int.Parse(versionArray[1]), int.Parse(versionArray[2]), int.Parse(versionArray[3]));
                }
            }
            Logger.Log.Append("config version parser returning: " + configVersion.ToString() + " as local version");
            return configVersion;
        }

        private static void selfUpdate(dynamic name, Uri rootUrl)
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("WINDIR") + "\\Temp\\wintap_selfupdate.cmd", "TIMEOUT /T 5 \n xcopy %WINDIR%\\temp\\wintapsvcmgr.exe \"%PROGRAMFILES%\\Wintap\\\" /y");
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\system32\\cmd.exe";
            psi.Arguments = "/C " + Environment.GetEnvironmentVariable("WINDIR") + "\\temp\\wintap_selfupdate.cmd";
            Process cmd = new Process(); ;
            cmd.StartInfo = psi;
            Logger.Log.Append("Attempting to run: " + psi.FileName + " " + psi.Arguments);
            cmd.Start();
        }

        private static void applyPatch(dynamic componentName, string localFullName, Uri url)
        {
            FileInfo localComponent = new FileInfo(localFullName);
            DirectoryInfo backupDir = new DirectoryInfo(localComponent.Directory.FullName + "\\backup");
            if (!backupDir.Exists)
            {
                backupDir.Create();
            }
            else
            {
                backupDir.Delete(true);
                backupDir.Create();
            }
            if (componentName != "WintapSvcMgr.exe")
            {
                try
                {
                    localComponent.MoveTo(backupDir.FullName + "\\" + componentName);
                }
                catch (Exception ex)
                {
                    Logger.Log.Append("Error backing up component " + localComponent.FullName + " to: " + backupDir.FullName + "\\" + componentName);
                    Logger.Log.Append("     error: " + ex.Message);
                }
            }
            try
            {
                downloadComponent(componentName, localFullName, url);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("error downloading component update: " + ex.Message);
                Logger.Log.Append("Rolling back update...");
                FileInfo backupComponent = new FileInfo(backupDir.FullName + "\\" + componentName);
                backupComponent.MoveTo(localFullName);
            }
            Logger.Log.Append("Done updating component: " + componentName);
        }

        private static void downloadComponent(dynamic componentName, string localFullName, Uri url)
        {
            FileInfo localComponentInfo = new FileInfo(localFullName);
            localComponentInfo.Delete();
            using (var client = new WebClient())
            {
                Logger.Log.Append("Attempting to download component: " + url.OriginalString + "/" + componentName);
                if (componentName == "WintapSvcMgr.exe")
                {
                    FileInfo wintapUpdateTemp = new FileInfo(Environment.GetEnvironmentVariable("WINDIR") + "\\temp\\wintapsvcmgr.exe");
                    if (wintapUpdateTemp.Exists)
                    {
                        wintapUpdateTemp.Delete();
                    }
                    localFullName = wintapUpdateTemp.FullName;
                }
                client.DownloadFile(url.OriginalString + "/" + componentName, localFullName);
            }
        }
    }

    internal class Update
    {
        public string Name { get; set; }
        public string LocalPath { get; set; }
    }
}
