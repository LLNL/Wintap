/*
 * Base eBPF Sensor - Common infrastructure for all eBPF sensors
 * Eliminates duplication between ExecveSensor, OpenatSensor, ExitSensor, etc.
 */

using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// Base class for eBPF-based sensors
    /// Handles common eBPF loading, polling, and cleanup
    /// </summary>
    internal abstract class BaseEbpfSensor : BaseSensor
    {
        // eBPF resources
        protected IntPtr BpfObject = IntPtr.Zero;
        protected IntPtr BpfLink = IntPtr.Zero;
        protected IntPtr RingBuffer = IntPtr.Zero;
        
        // Keep strong reference to callback to prevent GC
        protected LibBpf.RingBufferCallback? RingBufferCallback;
        
        // Threading
        protected Thread? PollingThread;
        protected volatile bool IsRunning = false;

        // Abstract properties - subclasses define these
        protected abstract string BpfObjectFileName { get; }
        protected abstract string BpfProgramName { get; }

        /// <summary>
        /// Start the sensor - loads eBPF program and starts polling
        /// </summary>
        public override bool Start()
        {
            try
            {
                WintapLogger.Log.Append($"Starting {SensorName} sensor...", LogLevel.Info);

                // Check root privileges (required for eBPF)
                if (getuid() != 0)
                {
                    WintapLogger.Log.Append($"{SensorName} requires root privileges", LogLevel.Error);
                    return false;
                }

                // Find eBPF object file
                string bpfPath = FindBpfObject(BpfObjectFileName);
                WintapLogger.Log.Append($"{SensorName} searching for: {BpfObjectFileName}", LogLevel.Debug);
                WintapLogger.Log.Append($"{SensorName} found at: {bpfPath}", LogLevel.Debug);
                
                if (!File.Exists(bpfPath))
                {
                    WintapLogger.Log.Append($"{SensorName} BPF object not found: {bpfPath}", LogLevel.Error);
                    return false;
                }

                // Load and attach eBPF program
                if (!LoadBpfProgram(bpfPath))
                {
                    return false;
                }

                // Allow subclass to do additional initialization
                if (!OnStarting())
                {
                    Stop();
                    return false;
                }

                // Start polling thread
                IsRunning = true;
                PollingThread = new Thread(PollRingBuffer)
                {
                    IsBackground = true,
                    Name = $"{SensorName}-Poller"
                };
                PollingThread.Start();

                WintapLogger.Log.Append($"{SensorName} sensor started", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append($"Failed to start {SensorName}: {ex.Message}", LogLevel.Error);
                WintapLogger.Log.Append($"{SensorName} stack trace: {ex.StackTrace}", LogLevel.Debug);
                Stop();
                return false;
            }
        }

        /// <summary>
        /// Load and attach eBPF program to kernel
        /// </summary>
        private bool LoadBpfProgram(string bpfPath)
        {
            // Open BPF object
            BpfObject = LibBpf.bpf_object__open(bpfPath);
            if (BpfObject == IntPtr.Zero)
            {
                WintapLogger.Log.Append($"{SensorName} failed to open BPF object", LogLevel.Error);
                return false;
            }

            // Load into kernel
            int ret = LibBpf.bpf_object__load(BpfObject);
            if (ret != 0)
            {
                WintapLogger.Log.Append($"{SensorName} failed to load BPF: {ret}", LogLevel.Error);
                LibBpf.bpf_object__close(BpfObject);
                BpfObject = IntPtr.Zero;
                return false;
            }

            // Find program by name
            IntPtr prog = LibBpf.bpf_object__find_program_by_name(BpfObject, BpfProgramName);
            if (prog == IntPtr.Zero)
            {
                WintapLogger.Log.Append($"{SensorName} program '{BpfProgramName}' not found", LogLevel.Error);
                return false;
            }

            // Attach to tracepoint
            BpfLink = LibBpf.bpf_program__attach(prog);
            if (BpfLink == IntPtr.Zero)
            {
                WintapLogger.Log.Append($"{SensorName} failed to attach program", LogLevel.Error);
                return false;
            }

            // Setup ring buffer
            IntPtr eventsMap = LibBpf.bpf_object__find_map_by_name(BpfObject, "events");
            if (eventsMap == IntPtr.Zero)
            {
                WintapLogger.Log.Append($"{SensorName} events map not found", LogLevel.Error);
                return false;
            }

            int mapFd = LibBpf.bpf_map__fd(eventsMap);
            
            // CRITICAL: Store callback in instance field to prevent GC
            RingBufferCallback = GetRingBufferCallback();
            
            RingBuffer = LibBpf.ring_buffer__new(mapFd, RingBufferCallback, IntPtr.Zero, IntPtr.Zero);
            if (RingBuffer == IntPtr.Zero)
            {
                WintapLogger.Log.Append($"{SensorName} failed to create ring buffer", LogLevel.Error);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ring buffer polling thread
        /// </summary>
        private void PollRingBuffer()
        {
            while (IsRunning)
            {
                try
                {
                    int pollRet = LibBpf.ring_buffer__poll(RingBuffer, 100);

                    if (pollRet < 0 && pollRet != -4) // -4 is EINTR
                    {
                        WintapLogger.Log.Append($"{SensorName} poll error: {pollRet}", LogLevel.Warn);
                    }
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append($"{SensorName} poll exception: {ex.Message}", LogLevel.Error);
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Stop the sensor - cleanup eBPF resources
        /// </summary>
        public override void Stop()
        {
            WintapLogger.Log.Append($"Stopping {SensorName} sensor...", LogLevel.Info);

            IsRunning = false;

            // Wait for polling thread
            PollingThread?.Join(TimeSpan.FromSeconds(2));

            // Allow subclass cleanup
            OnStopping();

            // Cleanup eBPF resources
            if (RingBuffer != IntPtr.Zero)
            {
                LibBpf.ring_buffer__free(RingBuffer);
                RingBuffer = IntPtr.Zero;
            }

            if (BpfLink != IntPtr.Zero)
            {
                LibBpf.bpf_link__destroy(BpfLink);
                BpfLink = IntPtr.Zero;
            }

            if (BpfObject != IntPtr.Zero)
            {
                LibBpf.bpf_object__close(BpfObject);
                BpfObject = IntPtr.Zero;
            }

            WintapLogger.Log.Append($"{SensorName} sensor stopped", LogLevel.Info);
        }

        /// <summary>
        /// Find eBPF object file in various locations
        /// </summary>
        protected virtual string FindBpfObject(string fileName)
        {
            var locations = new[]
            {
                fileName,
                $"./sensor/ebpf/tracers/{fileName}",
                $"./platform/linux/sensor/ebpf/tracers/{fileName}",  // Build output path
                $"./tracers/{fileName}",
                $"/opt/wintap/tracers/{fileName}",
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(AppContext.BaseDirectory, "platform", "linux", "sensor", "ebpf", "tracers", fileName),
                Path.Combine(AppContext.BaseDirectory, "tracers", fileName)
            };

            foreach (var location in locations)
            {
                if (File.Exists(location))
                    return location;
            }

            return fileName;
        }

        /// <summary>
        /// Get ring buffer callback - subclass implements this
        /// </summary>
        protected abstract LibBpf.RingBufferCallback GetRingBufferCallback();

        /// <summary>
        /// Hook for subclass initialization (optional)
        /// </summary>
        protected virtual bool OnStarting() => true;

        /// <summary>
        /// Hook for subclass cleanup (optional)
        /// </summary>
        protected virtual void OnStopping() { }

        [DllImport("libc")]
        private static extern uint getuid();
    }
}