/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using Microsoft.Win32;
using System;
using System.IO;

namespace gov.llnl.wintap.platform.windows.infrastructure
{
    /// <summary>
    /// ETW AutoLogger Setup - Configures Windows ETW AutoLogger for boot trace collection
    /// This should be called during Wintap installation or first-time service initialization
    /// </summary>
    public static class ETWAutoLoggerSetup
    {
        private const int EVENT_ENABLE_PROPERTY_SID = 0x00000001;
        private const int EVENT_ENABLE_PROPERTY_PROCESS_START_KEY = 0x00000080;
        private const string AUTOLOGGER_SESSION_NAME = "Wintap.Collectors.Process.ETLFile.BootTrace";
        private const string PROCESS_PROVIDER_GUID = "{22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716}";

        /// <summary>
        /// Initialize ETW AutoLogger for boot trace collection
        /// This configures Windows to automatically start ETW process tracing at boot
        /// </summary>
        /// <param name="bootTraceFilePath">Path where boot trace ETL file should be written</param>
        public static void InitializeBootTraceAutoLogger(string bootTraceFilePath = null)
        {
            try
            {
                // Default path if not specified
                if (string.IsNullOrEmpty(bootTraceFilePath))
                {
                    bootTraceFilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Wintap7", "etl", AUTOLOGGER_SESSION_NAME + ".etl");
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(bootTraceFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                WintapLogger.Log.Append($"Initializing ETW AutoLogger for boot trace: {bootTraceFilePath}", LogLevel.Info);

                // Enable AutoLogger system
                EnableAutoLoggerSystem();

                // Create AutoLogger session configuration
                CreateAutoLoggerSession(bootTraceFilePath);

                // Configure process provider
                ConfigureProcessProvider();

                WintapLogger.Log.Append("ETW AutoLogger initialization completed successfully", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to initialize ETW AutoLogger: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// Remove ETW AutoLogger configuration
        /// This should be called during Wintap uninstallation
        /// </summary>
        public static void RemoveBootTraceAutoLogger()
        {
            try
            {
                WintapLogger.Log.Append("Removing ETW AutoLogger configuration", LogLevel.Info);

                // Remove the entire AutoLogger session key
                var autoLoggerPath = $"SYSTEM\\ControlSet001\\Control\\WMI\\Autologger\\{AUTOLOGGER_SESSION_NAME}";
                Registry.LocalMachine.DeleteSubKeyTree(autoLoggerPath, false);

                WintapLogger.Log.Append("ETW AutoLogger configuration removed successfully", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to remove ETW AutoLogger: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Check if ETW AutoLogger is properly configured
        /// </summary>
        public static bool IsAutoLoggerConfigured()
        {
            try
            {
                var autoLoggerPath = $"SYSTEM\\ControlSet001\\Control\\WMI\\Autologger\\{AUTOLOGGER_SESSION_NAME}";
                using var sessionKey = Registry.LocalMachine.OpenSubKey(autoLoggerPath, false);

                if (sessionKey == null)
                    return false;

                // Check if Start value is set to 1
                var startValue = sessionKey.GetValue("Start");
                return startValue != null && (int)startValue == 1;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error checking AutoLogger configuration: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>
        /// Enable the AutoLogger system
        /// </summary>
        private static void EnableAutoLoggerSystem()
        {
            using var autoLoggerKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\ControlSet001\\Control\\WMI\\Autologger", true);
            if (autoLoggerKey != null)
            {
                autoLoggerKey.SetValue("Status", 1, RegistryValueKind.DWord);
                autoLoggerKey.Flush();
            }
        }

        /// <summary>
        /// Create AutoLogger session configuration
        /// </summary>
        private static void CreateAutoLoggerSession(string bootTraceFilePath)
        {
            var sessionPath = $"SYSTEM\\ControlSet001\\Control\\WMI\\Autologger\\{AUTOLOGGER_SESSION_NAME}";

            using var sessionKey = Registry.LocalMachine.CreateSubKey(sessionPath, true);

            // Session configuration
            sessionKey.SetValue("BufferSize", 8, RegistryValueKind.DWord);
            sessionKey.SetValue("ClockType", 1, RegistryValueKind.DWord);
            sessionKey.SetValue("FileName", bootTraceFilePath, RegistryValueKind.String);
            sessionKey.SetValue("FlushTimer", 0, RegistryValueKind.DWord);
            sessionKey.SetValue("Guid", "{" + Guid.Empty + "}", RegistryValueKind.String);
            sessionKey.SetValue("LogFileMode", 4610, RegistryValueKind.DWord);
            sessionKey.SetValue("MaxFileSize", 1000, RegistryValueKind.DWord);
            sessionKey.SetValue("MaximumBuffers", 0, RegistryValueKind.DWord);
            sessionKey.SetValue("MinimumBuffers", 0, RegistryValueKind.DWord);
            sessionKey.SetValue("Start", 1, RegistryValueKind.DWord);
            sessionKey.Flush();
        }

        /// <summary>
        /// Configure the process provider within the AutoLogger session
        /// </summary>
        private static void ConfigureProcessProvider()
        {
            var providerPath = $"SYSTEM\\ControlSet001\\Control\\WMI\\Autologger\\{AUTOLOGGER_SESSION_NAME}\\{PROCESS_PROVIDER_GUID}";

            using var providerKey = Registry.LocalMachine.CreateSubKey(providerPath, true);

            // Provider configuration
            providerKey.SetValue("Enabled", 1, RegistryValueKind.DWord);
            providerKey.SetValue("EnableLevel", 0, RegistryValueKind.DWord);

            // Enable SID and Process Start Key properties
            int enableProps = EVENT_ENABLE_PROPERTY_SID | EVENT_ENABLE_PROPERTY_PROCESS_START_KEY;
            providerKey.SetValue("EnableProperty", enableProps, RegistryValueKind.DWord);
            providerKey.SetValue("MatchAnyKeyword", 80, RegistryValueKind.QWord);
            providerKey.Flush();
        }

        /// <summary>
        /// Validate AutoLogger configuration and fix common issues
        /// </summary>
        public static void ValidateAndRepairAutoLogger()
        {
            try
            {
                WintapLogger.Log.Append("Validating ETW AutoLogger configuration", LogLevel.Info);

                if (!IsAutoLoggerConfigured())
                {
                    WintapLogger.Log.Append("AutoLogger not configured, initializing...", LogLevel.Warn);
                    InitializeBootTraceAutoLogger();
                }
                else
                {
                    WintapLogger.Log.Append("ETW AutoLogger is properly configured", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to validate AutoLogger configuration: {ex.Message}", LogLevel.Error);
            }
        }
    }
}