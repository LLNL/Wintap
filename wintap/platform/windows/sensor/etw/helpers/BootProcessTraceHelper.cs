/*
 * Copyright (c) 2026, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.IO;
using gov.llnl.wintap.core.shared;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32;

namespace gov.llnl.wintap.platform.windows.collect.etw.helpers
{
    internal static class BootProcessTraceHelper
    {
        private const string KernelSessionName = "NT Kernel Logger";
        private const string GlobalLoggerRegistryPath = @"SYSTEM\CurrentControlSet\Control\WMI\GlobalLogger";

        internal static string BootTraceEtlPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Wintap",
            "boot-process-trace.etl");

        internal static bool IsOwnedBootSession(string sessionLogFilePath, string configuredEtlPath)
        {
            if (string.IsNullOrWhiteSpace(sessionLogFilePath) || string.IsNullOrWhiteSpace(configuredEtlPath))
            {
                return false;
            }

            try
            {
                string sessionPath = Path.GetFullPath(sessionLogFilePath.Trim());
                string configuredPath = Path.GetFullPath(configuredEtlPath.Trim());
                return string.Equals(sessionPath, configuredPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static BootTraceLifecycleDecision DecideLifecycle(
            bool settingEnabled,
            bool activeSessionPresent,
            bool ownedActiveSession,
            bool bootEtlExists,
            bool registryOwned)
        {
            bool foreignSessionActive = activeSessionPresent && !ownedActiveSession;
            return new BootTraceLifecycleDecision
            {
                StopOwnedSession = ownedActiveSession,
                DisarmOwnedRegistry = !foreignSessionActive && (registryOwned || ownedActiveSession),
                ArmForNextBoot = settingEnabled,
                ReplayBootEtl = settingEnabled && !foreignSessionActive && bootEtlExists
            };
        }

        internal static string InspectCleanupArmAndGetReplayPath(bool settingEnabled, Action<string, LogLevel> log)
        {
            TraceEventSession activeSession = null;
            bool activeSessionPresent = false;
            bool ownedActiveSession = false;
            bool ownedSessionStopped = false;

            try
            {
                activeSession = TryAttachKernelSession(log);
                if (activeSession != null)
                {
                    activeSessionPresent = true;
                    ownedActiveSession = IsOwnedBootSession(activeSession.FileName, BootTraceEtlPath);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Unable to inspect Global Logger boot process trace session; continuing without boot replay: {ex.Message}", LogLevel.Warn);
            }

            bool bootEtlExists = File.Exists(BootTraceEtlPath);
            bool registryOwned = IsOwnedRegistryState(log);
            BootTraceLifecycleDecision decision = DecideLifecycle(
                settingEnabled,
                activeSessionPresent,
                ownedActiveSession,
                bootEtlExists,
                registryOwned);

            if (activeSessionPresent && !ownedActiveSession)
            {
                log?.Invoke($"Active '{KernelSessionName}' session log file '{activeSession?.FileName}' does not match Wintap boot ETL '{BootTraceEtlPath}'; preserving foreign session and Global Logger state", LogLevel.Warn);
            }

            if (decision.StopOwnedSession)
            {
                try
                {
                    string sessionFileName = activeSession.FileName;
                    activeSession.Stop();
                    ownedSessionStopped = true;
                    log?.Invoke($"Stopped owned Global Logger boot process trace session with ETL '{sessionFileName}'", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Unable to stop owned Global Logger boot process trace session; continuing without boot replay: {ex.Message}", LogLevel.Warn);
                }
            }

            try
            {
                activeSession?.Dispose();
            }
            catch
            {
            }

            if (decision.DisarmOwnedRegistry)
            {
                Disarm(log);
            }

            string replayPath = decision.ReplayBootEtl && (!ownedActiveSession || ownedSessionStopped)
                ? BootTraceEtlPath
                : null;

            if (settingEnabled && replayPath == null && !bootEtlExists && !activeSessionPresent)
            {
                log?.Invoke($"Boot process trace ETL not found at '{BootTraceEtlPath}'; skipping boot replay", LogLevel.Info);
            }

            if (decision.ArmForNextBoot)
            {
                ArmForNextBoot(log);
            }

            return replayPath;
        }

        private static bool IsOwnedRegistryState(Action<string, LogLevel> log)
        {
            try
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(GlobalLoggerRegistryPath, writable: false);
                return IsOwnedBootSession(key?.GetValue("FileName") as string, BootTraceEtlPath);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Unable to inspect Global Logger boot process trace registry key: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        internal static void Disarm(Action<string, LogLevel> log)
        {
            try
            {
                using RegistryKey key = Registry.LocalMachine.CreateSubKey(GlobalLoggerRegistryPath);
                key?.SetValue("Start", 0, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Unable to disarm Global Logger boot process trace registry key: {ex.Message}", LogLevel.Warn);
            }
        }

        internal static void ArmForNextBoot(Action<string, LogLevel> log)
        {
            try
            {
                string directory = Path.GetDirectoryName(BootTraceEtlPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                byte[] enableKernelFlags = new byte[32];
                enableKernelFlags[0] = 0x01;

                using RegistryKey key = Registry.LocalMachine.CreateSubKey(GlobalLoggerRegistryPath);
                key?.SetValue("Start", 1, RegistryValueKind.DWord);
                key?.SetValue("FileName", BootTraceEtlPath, RegistryValueKind.String);
                key?.SetValue("EnableKernelFlags", enableKernelFlags, RegistryValueKind.Binary);
                log?.Invoke($"Armed Global Logger boot process trace for next boot at '{BootTraceEtlPath}'", LogLevel.Info);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Unable to arm Global Logger boot process trace registry key: {ex.Message}", LogLevel.Warn);
            }
        }

        private static TraceEventSession TryAttachKernelSession(Action<string, LogLevel> log)
        {
            try
            {
                return new TraceEventSession(KernelSessionName, TraceEventSessionOptions.Attach);
            }
            catch (Exception ex)
            {
                if (!ex.Message.EndsWith(" is not active.", StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke($"Unable to attach to '{KernelSessionName}' for boot process trace inspection: {ex.Message}", LogLevel.Warn);
                }

                return null;
            }
        }
    }

    internal sealed class BootTraceLifecycleDecision
    {
        internal bool StopOwnedSession { get; set; }
        internal bool DisarmOwnedRegistry { get; set; }
        internal bool ArmForNextBoot { get; set; }
        internal bool ReplayBootEtl { get; set; }
    }
}
