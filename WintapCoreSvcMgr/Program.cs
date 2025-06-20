using gov.llnl.wintap.core.infrastructure;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
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
            //  PRUNE    -- prunes out terminated processes from the process ETL log that have no ancestors 
            if (args.Length == 0)
            {
                WintapLogger.Log.Append("WintapSvcMgr was invoked with zero arguments.  Process terminating.", LogLevel.Info);
                return;
            }

            WintapLogger.Log.Append("WintapSvcMgr was started with parameter: " + args[0], LogLevel.Info);

            if (args[0].ToUpper() == "HEALTHCHECK")
            {
                WintapLogger.Log.Append("Doing wintap health check.", LogLevel.Info);

                // 1.  Check that Wintap is setup to AUTO start
                WintapLogger.Log.Append("     checking Wintap service start type", LogLevel.Info);
                if (WintapController.GetSvcStartMode())
                {
                    WintapLogger.Log.Append("     Wintap service is to AUTO start", LogLevel.Info);
                }
                else
                {
                    WintapLogger.Log.Append("     Wintap service is NOT set to Auto.   Resetting...", LogLevel.Info);
                    WintapController.SetSvcStartMode();
                }

                // 2.  Check that Wintap is running
                WintapLogger.Log.Append("     checking Wintap service state", LogLevel.Info);
                if (WintapController.GetWintapSvcState())
                {
                    WintapLogger.Log.Append("     Wintap service is RUNNING", LogLevel.Info);
                }
                else
                {
                    WintapLogger.Log.Append("     Wintap service is NOT in a RUNNING state.   Restarting...", LogLevel.Info);
                    WintapController.StopWintap();
                    WintapController.StartWintap();
                }
                WintapLogger.Log.Append("Wintap health check complete.", LogLevel.Info);
            }
            else if (args[0].ToUpper() == "RESTART")
            {
                WintapLogger.Log.Append("Restarting Wintap...", LogLevel.Info);
                WintapController.StopWintap();
                WintapController.StartWintap();
                WintapLogger.Log.Append("Restart complete.", LogLevel.Info);
            }
            else if (args[0].ToUpper() == "RUNDOWN")
            {
                invokeEtwRundown();
            }
            else if (args[0].ToUpper() == "PRUNE")
            {
                pruneProcessETL();
            }
            else
            {
                WintapLogger.Log.Append("Unknown parameter specified.", LogLevel.Info);
            }


            WintapLogger.Log.Append("WintapSvcMgr is complete.", LogLevel.Info);
            try
            {
                WintapLogger.Log.Close();
            }
            catch (Exception ex) { }
        }

        private static void pruneProcessETL()
        {
            
        }

        private static void invokeEtwRundown()
        {
            WintapLogger.Log.Append("Starting ETW file event rundown", LogLevel.Info);
            // TODO: make relative to assembly path
            //string etlFilePath = Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\wintap7\\etl\\kernelrundown.etl";
            string etlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "etl", "kernelrundown.etl");
            using (var session = new TraceEventSession("NT Kernel WintapLogger", etlFilePath))
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.DiskIO |
                                             KernelTraceEventParser.Keywords.DiskFileIO |
                                             KernelTraceEventParser.Keywords.DiskIOInit |
                                             KernelTraceEventParser.Keywords.FileIO |
                                             KernelTraceEventParser.Keywords.FileIOInit);

                // ETW emits rundown events at session stop, so we only need a brief duration
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            WintapLogger.Log.Append("Rundown complete.  ETL File Path: " + etlFilePath, LogLevel.Info);
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
                WintapLogger.Log.Append("Current wintap profile: " + currentProfile, LogLevel.Info);
                if (currentProfile == "Developer")
                {
                    isDeveloper = true;
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("ERROR reading Wintap.exe.config from path: " + AppDomain.CurrentDomain.BaseDirectory + "   error: " + ex.Message, LogLevel.Info);
                WintapLogger.Log.Append("Wintap profile cannot be obtained.  Defaulting to non-developer.", LogLevel.Info);
            }
            return isDeveloper;
        }
    }
}
