using System;
using System.Collections.Generic;
using System.Threading;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// fop-11: short-interval, emit-first aggregation of repeat FileOps
    /// activity at (pid, path, op) granularity.
    ///
    /// Semantics: the FIRST occurrence of a distinct key inside a window is
    /// never absorbed — the caller emits it immediately and unchanged.
    /// Subsequent occurrences of the same key inside the window are absorbed
    /// into a summary entry (repeat count, byte total, first/last kernel
    /// timestamps) that is emitted once on window expiry. Process identity is
    /// captured at first occurrence and carried into the summary, never
    /// re-resolved at flush.
    ///
    /// Thread model: TryAbsorb runs on the poller thread; FlushExpired runs
    /// on a timer thread; a single lock guards the table (contention is one
    /// timer sweep per interval against per-event dictionary ops).
    /// </summary>
    internal sealed class FileOpsAggregator
    {
        internal sealed class AggregateEntry
        {
            public int Pid;
            public string Path;
            public uint OpType;
            public string ProcessName;
            public string PidHash;
            public long WindowStartMs;
            public ulong FirstRepeatNs;
            public ulong LastRepeatNs;
            public long BytesSum;
            public int RepeatCount;
        }

        private readonly Dictionary<(int Pid, string Path, uint OpType), AggregateEntry> _entries;
        private readonly object _gate = new object();
        private readonly int _windowMs;
        private readonly int _maxKeys;
        private readonly Action<AggregateEntry> _emitSummary;

        private long _repeatsFolded;
        private long _summariesEmitted;
        private long _capBypass;
        private long _flushCount;
        private long _flushTicks;
        private long _maxFlushTicks;

        public FileOpsAggregator(int windowMs, int maxKeys, Action<AggregateEntry> emitSummary)
        {
            _windowMs = windowMs > 0 ? windowMs : 1000;
            _maxKeys = maxKeys > 0 ? maxKeys : 32768;
            _emitSummary = emitSummary ?? throw new ArgumentNullException(nameof(emitSummary));
            _entries = new Dictionary<(int, string, uint), AggregateEntry>();
        }

        public int EntryCount
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        public long TakeRepeatsFolded() => Interlocked.Exchange(ref _repeatsFolded, 0);
        public long TakeSummariesEmitted() => Interlocked.Exchange(ref _summariesEmitted, 0);
        public long TakeCapBypass() => Interlocked.Exchange(ref _capBypass, 0);

        public void TakeFlushTiming(out long flushCount, out long flushTicks, out long maxFlushTicks)
        {
            flushCount = Interlocked.Exchange(ref _flushCount, 0);
            flushTicks = Interlocked.Exchange(ref _flushTicks, 0);
            maxFlushTicks = Interlocked.Exchange(ref _maxFlushTicks, 0);
        }

        /// <summary>
        /// Returns true when the event was absorbed as a repeat (caller must
        /// NOT emit it); false when the event is a first occurrence or a
        /// cap/shutdown bypass (caller emits it per-event as today).
        /// </summary>
        public bool TryAbsorb(int pid, string path, uint opType, long bytes, ulong timestampNs,
                              string processName, string pidHash, long nowMs)
        {
            var key = (pid, path, opType);
            AggregateEntry expired = null;
            bool absorbed = false;

            lock (_gate)
            {
                if (_entries.TryGetValue(key, out AggregateEntry entry))
                {
                    if (nowMs - entry.WindowStartMs <= _windowMs)
                    {
                        entry.RepeatCount++;
                        entry.BytesSum += bytes;
                        if (entry.FirstRepeatNs == 0)
                        {
                            entry.FirstRepeatNs = timestampNs;
                        }
                        entry.LastRepeatNs = timestampNs;
                        absorbed = true;
                    }
                    else
                    {
                        // Window rolled over: flush the old entry (outside the
                        // lock) and let this event start a fresh window as a
                        // new first occurrence.
                        _entries.Remove(key);
                        if (entry.RepeatCount > 0)
                        {
                            expired = entry;
                        }
                        StartEntryLocked(key, pid, path, opType, processName, pidHash, nowMs);
                    }
                }
                else if (_entries.Count >= _maxKeys)
                {
                    // Table full: never lose data — bypass aggregation and let
                    // the caller emit per-event, counted.
                    Interlocked.Increment(ref _capBypass);
                }
                else
                {
                    StartEntryLocked(key, pid, path, opType, processName, pidHash, nowMs);
                }
            }

            if (absorbed)
            {
                Interlocked.Increment(ref _repeatsFolded);
            }

            if (expired != null)
            {
                EmitSummary(expired);
            }

            return absorbed;
        }

        public void FlushExpired(long nowMs)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            List<AggregateEntry> due = null;
            try
            {
                lock (_gate)
                {
                    List<(int, string, uint)> remove = null;
                    foreach (var pair in _entries)
                    {
                        if (nowMs - pair.Value.WindowStartMs > _windowMs)
                        {
                            (remove ??= new List<(int, string, uint)>()).Add(pair.Key);
                            if (pair.Value.RepeatCount > 0)
                            {
                                (due ??= new List<AggregateEntry>()).Add(pair.Value);
                            }
                        }
                    }

                    if (remove != null)
                    {
                        foreach (var key in remove)
                        {
                            _entries.Remove(key);
                        }
                    }
                }

                if (due != null)
                {
                    foreach (AggregateEntry entry in due)
                    {
                        EmitSummary(entry);
                    }
                }
            }
            finally
            {
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                Interlocked.Increment(ref _flushCount);
                Interlocked.Add(ref _flushTicks, elapsed);
                UpdateMax(ref _maxFlushTicks, elapsed);
            }
        }

        /// <summary>Shutdown drain: flush every pending summary regardless of age.</summary>
        public void FlushAll()
        {
            List<AggregateEntry> due = null;
            lock (_gate)
            {
                foreach (var pair in _entries)
                {
                    if (pair.Value.RepeatCount > 0)
                    {
                        (due ??= new List<AggregateEntry>()).Add(pair.Value);
                    }
                }
                _entries.Clear();
            }

            if (due != null)
            {
                foreach (AggregateEntry entry in due)
                {
                    EmitSummary(entry);
                }
            }
        }

        private void StartEntryLocked((int, string, uint) key, int pid, string path, uint opType,
                                      string processName, string pidHash, long nowMs)
        {
            _entries[key] = new AggregateEntry
            {
                Pid = pid,
                Path = path,
                OpType = opType,
                ProcessName = processName,
                PidHash = pidHash,
                WindowStartMs = nowMs,
                FirstRepeatNs = 0,
                LastRepeatNs = 0,
                BytesSum = 0,
                RepeatCount = 0,
            };
        }

        private void EmitSummary(AggregateEntry entry)
        {
            try
            {
                _emitSummary(entry);
                Interlocked.Increment(ref _summariesEmitted);
            }
            catch
            {
                // The emit callback owns its own error accounting; the
                // aggregator must never throw into the poller or timer thread.
            }
        }

        private static void UpdateMax(ref long target, long observed)
        {
            while (true)
            {
                long current = Interlocked.Read(ref target);
                if (observed <= current || Interlocked.CompareExchange(ref target, observed, current) == current)
                {
                    return;
                }
            }
        }
    }
}
