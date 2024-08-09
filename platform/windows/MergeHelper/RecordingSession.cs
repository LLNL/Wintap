/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.IO;
using System.Linq;
using gov.llnl.wintap.etl.shared;
using Microsoft.Win32;
using gov.llnl.wintap.etl.helpers.utils;

namespace gov.llnl.wintap.etl.load
{
    /// <summary>
    /// Integration with WintapRecorder.
    /// The wintap recorder tool allows local caching of time bounded parquet data.
    /// If recording is active, we want MergeHelper to be aware so that it can mirror the upload files to
    /// the WintapRecorder session directory.
    /// </summary>
    internal static class RecordingSession
    {

        internal enum SessionModes { Off, Record, Playback };
        internal static bool NowRecording(Logit log)
        {
            bool nowRecording = false;
            log.Append("Checking recording session state.", LogVerboseLevel.Normal);
            try
            {
                RegistryKey wintapKey = Registry.LocalMachine.OpenSubKey(Strings.RecordingSessionRegPath);
                if (wintapKey != null)
                {
                    string sessionModeStr = wintapKey.GetValue("Mode").ToString();
                    SessionModes sessionMode = (SessionModes)Enum.Parse(typeof(SessionModes), sessionModeStr, true);
                    if (sessionMode == SessionModes.Record)
                    {
                        DateTime sessionStartTime = DateTime.Parse(wintapKey.GetValue("RecordStartTime").ToString());
                        if (DateTime.Now.Subtract(sessionStartTime) < new TimeSpan(1, 0, 0, 0))
                        {
                            log.Append("Session Recording enabled", LogVerboseLevel.Normal);
                            nowRecording = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Append("Error determining recording session state: " + ex.Message, LogVerboseLevel.Debug);
            }
            log.Append("Returning recording session state: " + nowRecording, LogVerboseLevel.Normal);
            return nowRecording;
        }


        /// <summary>
        /// Copies a merged parquet file to the WintapRecorder's  session directory.
        /// Files in the session directory are NOT auto-deleted by the mergehelper tool upon upload.
        /// </summary>
        /// <param name="parquetPath"></param>
        /// <param name="sensorName"></param>
        /// <param name="log"></param>
        internal static void Record(string parquetPath, string sensorName, Logit log)
        {
            FileInfo parquetInfo = new FileInfo(parquetPath);
            try
            {
                RegistryKey wintapKey = Registry.LocalMachine.OpenSubKey(Strings.RecordingSessionRegPath);
                if (wintapKey != null)
                {
                    DateTime sessionStartTime = DateTime.Parse(wintapKey.GetValue("RecordStartTime").ToString());
                    string recordingSessionName = Environment.MachineName.ToUpper() + "-" + sessionStartTime.ToFileTimeUtc().ToString();
                    log.Append("Recording session name: " + recordingSessionName, LogVerboseLevel.Normal);
                    DirectoryInfo recordingSessionInfo = new DirectoryInfo(Strings.RecordingDataPath + recordingSessionName);
                    log.Append("Recording session directory: " + recordingSessionInfo.FullName, LogVerboseLevel.Normal);
                    if (!recordingSessionInfo.Exists)
                    {
                        recordingSessionInfo.Create();
                    }

                    string rawSensorPath = recordingSessionInfo.FullName + "\\raw_sensor\\";
                    DirectoryInfo sensorSessionDir = new DirectoryInfo(rawSensorPath + sensorName);

                    // add path components required for post-upload processing
                    if (sensorName.ToLower() == "tcp_process_conn_incr")
                    {
                        rawSensorPath = rawSensorPath + "process_conn_incr\\proto=TCP\\";
                        sensorSessionDir = new DirectoryInfo(rawSensorPath);
                    }
                    if (sensorName.ToLower() == "udp_process_conn_incr")
                    {
                        rawSensorPath = rawSensorPath + "process_conn_incr\\proto=UDP\\";
                        sensorSessionDir = new DirectoryInfo(rawSensorPath);
                    }

                    if (!sensorSessionDir.Exists)
                    {
                        sensorSessionDir.Create();
                    }
                    string destFileName = parquetInfo.Name;
                    destFileName = destFileName.Replace(".merged.parquet", ".parquet");
                    parquetInfo.CopyTo(sensorSessionDir.FullName + "\\" + destFileName);
                    log.Append("Recording session written at: " + sensorSessionDir.FullName, LogVerboseLevel.Normal);
                }
            }
            catch (Exception ex)
            {
                log.Append("Error appending to recording session: " + ex.Message, LogVerboseLevel.Normal);
            }
        }
    }
}
