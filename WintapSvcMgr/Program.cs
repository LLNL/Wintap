using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace gov.llnl.wintap
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //  MODES:
            //  UPDATE
            //  HEALTHCHECK
            //  RESTART
            //  RUNDOWN
            if(args.Length == 0)
            {
                Logger.Log.Append("WintapSvcMgr was invoked with zero arguments.  Process terminating.");
                return;
            }

            Logger.Log.Append("WintapSvcMgr was started with parameter: " + args[0]);

            if (args[0].ToUpper() == "HEALTHCHECK")
            {
                Logger.Log.Append("Doing wintap health check.");
                
                // 1.  Check that Wintap is setup to AUTO start
                Logger.Log.Append("     checking Wintap service start type");
                if(WintapController.GetSvcStartMode())
                {
                    Logger.Log.Append("     Wintap service is to AUTO start");
                }
                else
                {
                    Logger.Log.Append("     Wintap service is NOT set to Auto.   Resetting...");
                    WintapController.SetSvcStartMode();
                }

                // 2.  Check that Wintap is running
                Logger.Log.Append("     checking Wintap service state");
                if (WintapController.GetWintapSvcState())
                {
                    Logger.Log.Append("     Wintap service is RUNNING");
                }
                else
                {
                    Logger.Log.Append("     Wintap service is NOT in a RUNNING state.   Restarting...");
                    WintapController.StopWintap();
                    WintapController.StartWintap();
                }
                Logger.Log.Append("Wintap health check complete.");
            }
            else if (args[0].ToUpper() == "RESTART")
            {
                Logger.Log.Append("Restarting Wintap...");
                WintapController.StopWintap();
                WintapController.StartWintap();
                Logger.Log.Append("Restart complete.");
            }
            else if (args[0].ToUpper() == "RUNDOWN")
            {
                invokeEtwRundown();
            }
            else
            {
                Logger.Log.Append("Unknown parameter specified.");
            }


            Logger.Log.Append("WintapSvcMgr is complete.");
            try
            {
                Logger.Log.Close();
            }
            catch (Exception ex) { }
        }

        private static void invokeEtwRundown()
        {
            Logger.Log.Append("Starting ETW file event rundown");
            string etlFilePath = Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\wintap\\etl\\kernelrundown.etl";
            using (var session = new TraceEventSession("NT Kernel Logger", etlFilePath))
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskIO |
                                             KernelTraceEventParser.Keywords.DiskFileIO |
                                             KernelTraceEventParser.Keywords.DiskIOInit |
                                             KernelTraceEventParser.Keywords.FileIO |
                                             KernelTraceEventParser.Keywords.FileIOInit);

                // ETW emits rundown events at session stop, so we only need a brief duration
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            Logger.Log.Append("Rundown complete.  ETL File Path: " + etlFilePath);
        }

        private static bool isDeveloper()
        {
            bool isDeveloper = false;
            try
            {
                string wintapConfig = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "\\Wintap.exe.config");
                XmlDocument configDoc = new XmlDocument();
                configDoc.LoadXml(wintapConfig);
                string currentProfile = configDoc.SelectNodes("//setting[@name='Profile']")[0].FirstChild.InnerText;
                Logger.Log.Append("Current wintap profile: " + currentProfile);
                if(currentProfile == "Developer")
                {
                    isDeveloper = true;
                }
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR reading Wintap.exe.config from path: " + AppDomain.CurrentDomain.BaseDirectory + "   error: " + ex.Message);
                Logger.Log.Append("Wintap profile cannot be obtained.  Defaulting to non-developer.");
            }
            return isDeveloper;
        }
    }
}
