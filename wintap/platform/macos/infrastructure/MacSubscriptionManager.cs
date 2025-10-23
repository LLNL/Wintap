using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.macos.sensor;
using System;
using System.Collections.Generic;

namespace gov.llnl.wintap.platform.macos.infrastructure
{
    /// <summary>
    /// Manages lifecycle of macOS security sensors
    /// Mirrors patterns from WindowsSubscriptionManager and LinuxSubscriptionManager
    /// </summary>
    internal class MacSubscriptionManager
    {
        private List<BaseSensor> macCollectors;

        internal MacSubscriptionManager()
        {
            macCollectors = new List<BaseSensor>();
        }

        internal List<BaseSensor> Start()
        {
            WintapLogger.Log.Append("Starting MacOS sensor initialization", LogLevel.Info);

            try
            {
                // Priority 1: Process monitoring (foundation for attribution)
                WintapLogger.Log.Append("Starting macOS ProcessSensor", LogLevel.Info);
                var processSensor = new ProcessSensor();
                if (processSensor.Start())
                {
                    macCollectors.Add(processSensor);
                    WintapLogger.Log.Append("✓ macOS ProcessSensor started", LogLevel.Info);
                }

                // Priority 2: Network monitoring (if OSQuery available)
                if (IsOSQueryAvailable())
                {
                    WintapLogger.Log.Append("OSQuery detected, enabling network sensors", LogLevel.Info);

                    var tcpSensor = new TcpSensor();
                    if (tcpSensor.Start())
                    {
                        macCollectors.Add(tcpSensor);
                        WintapLogger.Log.Append("✓ macOS TcpSensor started", LogLevel.Info);
                    }

                    //var udpSensor = new MacUdpSensor();
                    //if (udpSensor.Start())
                    //{
                    //    macCollectors.Add(udpSensor);
                    //    WintapLogger.Log.Append("✓ macOS UdpSensor started", LogLevel.Info);
                    //}
                }
                else
                {
                    WintapLogger.Log.Append("OSQuery not found - network monitoring disabled", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error starting macOS sensors: {ex.Message}", LogLevel.Error);
            }

            WintapLogger.Log.Append($"MacOS sensor initialization complete ({macCollectors.Count} sensors active)", LogLevel.Info);
            return macCollectors;
        }

        internal void Stop()
        {
            WintapLogger.Log.Append("Stopping macOS sensors", LogLevel.Info);

            foreach (var collector in macCollectors)
            {
                try
                {
                    collector.Stop();
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"Error stopping sensor {collector.SensorName}: {ex.Message}", LogLevel.Error);
                }
            }

            macCollectors.Clear();
            WintapLogger.Log.Append("MacOS sensors stopped", LogLevel.Info);
        }

        private bool IsOSQueryAvailable()
        {
            try
            {
                var result = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "osqueryi",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                });

                result.WaitForExit();
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}