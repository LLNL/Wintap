using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap
{
    internal static class WintapController
    {
        internal static TimeSpan svcTimeout = new TimeSpan(0, 0, 0, 20);

        internal static bool GetSvcStartMode()
        {
            bool setToAuto = false;
            try
            {
                ServiceController sc = new ServiceController("Wintap");
                if (sc.StartType == ServiceStartMode.Automatic)
                {
                    setToAuto = true;
                }
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR getting Wintap service start type: " + ex.Message);
            }
            Logger.Log.Append("Returning service set to AUTOMATIC: " + setToAuto);
            return setToAuto;
        }

        internal static void SetSvcStartMode()
        {
            try
            {
                Logger.Log.Append("Attempting to set Wintap service start type.");
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\System32\\sc.exe";
                psi.Arguments = "config wintap start=auto";
                System.Diagnostics.Process p = new System.Diagnostics.Process();
                p.StartInfo = psi;
                p.Start();
                
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR setting Wintap service start type: " + ex.Message);
            }
            Logger.Log.Append("Service start type complete");
        }

        /// <summary>
        /// Returns true is Wintap service is currently RUNNING
        /// </summary>
        /// <returns></returns>
        internal static bool GetWintapSvcState()
        {
            bool wintapRunning = false;
            try
            {
                ServiceController sc = new ServiceController("Wintap");
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    wintapRunning = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR getting Wintap service state: " + ex.Message);
            }
            Logger.Log.Append("Wintap service controller QUERY method complete, returning RUNNING state to caller: " + wintapRunning);
            return wintapRunning;
        }

        internal static bool StartWintap()
        {
            bool reqSucceeded = false;
            try
            {
                ServiceController sc = new ServiceController("Wintap");
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, svcTimeout);
                    reqSucceeded = true;
                    Logger.Log.Append("Wintap start request complete.  New state: " + sc.Status);
                }
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR starting Wintap: " + ex.Message);
            }
            Logger.Log.Append("Wintap service controller START method complete, returning RUNNING state to caller: " + reqSucceeded);
            return reqSucceeded;
        }

        internal static bool StopWintap()
        {
            bool stopReqSucceeded = true;
            try
            {
                ServiceController sc = new ServiceController("Wintap");
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, svcTimeout);
                    stopReqSucceeded = true;
                }
                else
                {
                    Logger.Log.Append("Wintap was already in a STOPPED state");
                }
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR attempting to shutdown Wintap: " + ex.Message);
                try
                {
                    System.Diagnostics.Process[] allWintaps = System.Diagnostics.Process.GetProcessesByName("wintap.exe");
                    for(int i = 0; i < allWintaps.Length; i++)
                    {
                        Logger.Log.Append("attempting to terminate wintap process with PID: " + allWintaps[i].Id);
                        allWintaps[i].Kill();
                    }
                    Logger.Log.Append("Wintap process termination complete.");
                }
                catch(Exception ex2)
                {
                    Logger.Log.Append("Error terminating Wintap: " + ex2.Message);
                    stopReqSucceeded = false;
                }
            }
            Logger.Log.Append("Wintap service controller STOP method complete, returning STOP state to caller: " + stopReqSucceeded);
            return stopReqSucceeded;
        }
    }
}
