/*
 * Copyright (c) 2026, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using gov.llnl.wintap.core.infrastructure;
using System;
using System.Threading;
using System.Timers;

namespace gov.llnl.wintap.platform.windows.collect.etw.helpers
{
    /// <summary>
    /// Periodic canary write to a Wintap-owned registry key. The canary's own
    /// SetValueKey event proves that capture mode is populating KeyName.
    /// </summary>
    internal sealed class RegistryCaptureCanary : IDisposable
    {
        internal const string CanaryValueName = "CaptureCanary";
        internal static readonly TimeSpan CanaryInterval = TimeSpan.FromSeconds(60);

        private readonly Action writeAction;
        private readonly int wintapPid;
        private readonly Action onLossSuspected;
        private readonly Action<string, LogLevel> log;
        private readonly object stateLock = new object();
        private System.Timers.Timer timer;
        private bool ticksEnabled = true;
        private bool expectationPending;
        private bool healthConfirmed;
        private bool lossActive;
        private bool recoveryFailed;
        private bool disposed;
        private long lossCount;
        private DateTime lossStartedUtc;

        internal RegistryCaptureCanary(
            Action writeAction,
            int wintapPid,
            Action onLossSuspected,
            Action<string, LogLevel> logOverride = null)
        {
            this.writeAction = writeAction ?? throw new ArgumentNullException(nameof(writeAction));
            this.wintapPid = wintapPid;
            this.onLossSuspected = onLossSuspected ?? throw new ArgumentNullException(nameof(onLossSuspected));
            log = logOverride ?? ((message, level) => WintapLogger.Log.Append(message, level));
        }

        internal long LossCount => Interlocked.Read(ref lossCount);

        internal bool RecoveryFailed
        {
            get
            {
                lock (stateLock)
                {
                    return recoveryFailed;
                }
            }
        }

        internal bool IsCanaryEvent(int processId, string valueName)
        {
            return processId == wintapPid
                && string.Equals(valueName, CanaryValueName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Observes a SetValueKey event. A matched event is always suppressed by
        /// the caller, including when its KeyName indicates capture loss.
        /// </summary>
        internal bool Observe(int processId, string valueName, string keyName)
        {
            if (!IsCanaryEvent(processId, valueName))
            {
                return false;
            }

            lock (stateLock)
            {
                if (!ticksEnabled || disposed)
                {
                    return true;
                }

                // Only the event belonging to the currently armed write may
                // change health. Duplicate or delayed canary events remain
                // suppressed without advancing the state machine.
                if (!expectationPending)
                {
                    return true;
                }

                expectationPending = false;
                if (string.IsNullOrEmpty(keyName))
                {
                    HandleLoss();
                }
                else
                {
                    HandleRecovery();
                }
            }

            return true;
        }

        /// <summary>Runs one canary cycle without requiring a live timer.</summary>
        internal void OnCanaryTick()
        {
            lock (stateLock)
            {
                if (!ticksEnabled || disposed)
                {
                    return;
                }

                if (expectationPending)
                {
                    expectationPending = false;
                    HandleLoss();
                }

                try
                {
                    writeAction();
                    expectationPending = true;
                }
                catch (Exception ex)
                {
                    log("Registry capture canary write failed; will retry: " + ex.Message, LogLevel.Info);
                }
            }
        }

        internal void Start()
        {
            lock (stateLock)
            {
                ThrowIfDisposed();
                StopTimerCore();
                ticksEnabled = true;
                expectationPending = false;
                timer = new System.Timers.Timer(CanaryInterval.TotalMilliseconds)
                {
                    AutoReset = false
                };
                timer.Elapsed += TimerElapsed;
            }

            OnCanaryTick();
            lock (stateLock)
            {
                if (ticksEnabled && !disposed)
                {
                    timer?.Start();
                }
            }
        }

        internal void Stop()
        {
            lock (stateLock)
            {
                ticksEnabled = false;
                expectationPending = false;
                StopTimerCore();
            }
        }

        public void Dispose()
        {
            lock (stateLock)
            {
                if (disposed)
                {
                    return;
                }

                ticksEnabled = false;
                expectationPending = false;
                StopTimerCore();
                disposed = true;
            }
        }

        private void HandleLoss()
        {
            Interlocked.Increment(ref lossCount);
            try
            {
                onLossSuspected();
            }
            catch
            {
                // The next failed cycle performs the required recovery-failed
                // escalation. Keep the timer alive without per-cycle noise.
            }

            if (!lossActive)
            {
                healthConfirmed = false;
                lossActive = true;
                lossStartedUtc = DateTime.UtcNow;
                log("Registry capture LOST — canary event did not contain a populated KeyName", LogLevel.Error);
                return;
            }

            if (!recoveryFailed)
            {
                recoveryFailed = true;
                log(
                    "registry capture recovery FAILED — capture filter re-assert did not restore KeyName population",
                    LogLevel.Error);
            }
        }

        private void HandleRecovery()
        {
            if (!lossActive)
            {
                if (!healthConfirmed)
                {
                    healthConfirmed = true;
                    log("Registry capture canary healthy — KeyName populated", LogLevel.Info);
                }

                return;
            }

            TimeSpan outage = DateTime.UtcNow - lossStartedUtc;
            log($"Registry capture RECOVERED after {outage.TotalSeconds:F0} seconds", LogLevel.Info);
            healthConfirmed = true;
            lossActive = false;
            recoveryFailed = false;
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            OnCanaryTick();
            lock (stateLock)
            {
                if (ticksEnabled && !disposed && ReferenceEquals(timer, sender))
                {
                    timer.Start();
                }
            }
        }

        private void StopTimerCore()
        {
            if (timer == null)
            {
                return;
            }

            timer.Stop();
            timer.Elapsed -= TimerElapsed;
            timer.Dispose();
            timer = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RegistryCaptureCanary));
            }
        }
    }
}
