/*
 * Copyright (c) 2025, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.macos.infrastructure;
using System;
using System.Diagnostics;
using System.Text.Json;

namespace gov.llnl.wintap.platform.macos.sensor
{
    /// <summary>
    /// macOS TCP connection monitoring sensor
    /// Uses OSQuery socket_events for network monitoring
    /// </summary>
    internal class TcpSensor : BaseSensor
    {
        private Process _osqueryProcess;
        private MacProcessResolver _processResolver;
        private bool _isRunning;

        public TcpSensor()
        {
            SensorName = "TcpSensor";
            _processResolver = new MacProcessResolver();
        }

        public override bool Start()
        {
            try
            {
                WintapLogger.Log.Append("Starting macOS TcpSensor", LogLevel.Info);
                StartOSQuerySocketEventStream();
                _isRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to start TcpSensor: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public override void Stop()
        {
            _isRunning = false;
            if (_osqueryProcess != null && !_osqueryProcess.HasExited)
            {
                _osqueryProcess.Kill();
                _osqueryProcess.Dispose();
            }
            WintapLogger.Log.Append("TcpSensor stopped", LogLevel.Info);
        }

        private void StartOSQuerySocketEventStream()
        {
            string query = @"
                SELECT 
                    pid,
                    family,
                    protocol,
                    local_address,
                    local_port,
                    remote_address,
                    remote_port,
                    action
                FROM socket_events
                WHERE protocol = 6;  -- TCP only
            ";

            var psi = new ProcessStartInfo
            {
                FileName = "osqueryi",
                Arguments = $"--json \"{query}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _osqueryProcess = new Process { StartInfo = psi };
            _osqueryProcess.OutputDataReceived += OnOSQueryOutput;
            _osqueryProcess.Start();
            _osqueryProcess.BeginOutputReadLine();
        }

        private void OnOSQueryOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data) || !_isRunning)
                return;

            try
            {
                var socketEvent = JsonSerializer.Deserialize<OSQuerySocketEvent>(e.Data);
                HandleTcpEvent(socketEvent);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error parsing socket event: {ex.Message}", LogLevel.Debug);
            }
        }

        private void HandleTcpEvent(OSQuerySocketEvent evt)
        {
            try
            {
                DateTime eventTime = DateTime.UtcNow;
                var processRecord = _processResolver.ResolveProcessAtTime(evt.pid, eventTime, "tcp_event");

                var wintapMessage = new WintapMessage(eventTime, evt.pid, WintapMessage.MessageTypeEnum.TcpConnection)
                {
                    ActivityType = MapSocketAction(evt.action),
                    ProcessName = processRecord.ProcessName,
                    PidHash = processRecord.PidHash,
                    EventTime = eventTime.ToFileTimeUtc(),
                    TcpConnection = new WintapMessage.TcpConnectionObject
                    {
                        SourceAddress = evt.local_address,
                        SourcePort = evt.local_port,
                        DestinationAddress = evt.remote_address,
                        DestinationPort = evt.remote_port
                    }
                };

                EventChannel.Send(wintapMessage);
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Error handling TCP event: {ex.Message}", LogLevel.Error);
            }
        }

        private WintapMessage.ActivityTypeEnum MapSocketAction(string action)
        {
            return action?.ToLower() switch
            {
                "connect" => WintapMessage.ActivityTypeEnum.TcpIpConnect,
                "bind" => WintapMessage.ActivityTypeEnum.Start,
                "close" => WintapMessage.ActivityTypeEnum.TcpIpDisconnect,
                _ => WintapMessage.ActivityTypeEnum.Other
            };
        }

        private class OSQuerySocketEvent
        {
            public int pid { get; set; }
            public int family { get; set; }
            public int protocol { get; set; }
            public string local_address { get; set; }
            public int local_port { get; set; }
            public string remote_address { get; set; }
            public int remote_port { get; set; }
            public string action { get; set; }
        }
    }
}