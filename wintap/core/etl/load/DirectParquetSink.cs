using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.Timers;

namespace gov.llnl.wintap.core.etl.load
{
    internal static class DirectParquetSink
    {
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<ExpandoObject>> queues = new();
        private static readonly ParquetWriter parquetWriter = new();
        private static readonly Timer flushTimer;

        static DirectParquetSink()
        {
            int flushSeconds = 15;
            if (int.TryParse(Environment.GetEnvironmentVariable("WINTAP_DIRECT_PARQUET_FLUSH_SECONDS"), out int configuredSeconds) && configuredSeconds > 0)
            {
                flushSeconds = configuredSeconds;
            }

            flushTimer = new Timer(flushSeconds * 1000);
            flushTimer.AutoReset = true;
            flushTimer.Elapsed += FlushTimer_Elapsed;
            flushTimer.Start();
        }

        internal static bool IsEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("WINTAP_ENABLE_DIRECT_PARQUET"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("WINTAP_ENABLE_DIRECT_PARQUET"), "1", StringComparison.OrdinalIgnoreCase);

        internal static void Save(WintapMessage message)
        {
            if (!IsEnabled || message == null)
            {
                return;
            }

            string messageType = message.MessageType.ToString();
            ConcurrentQueue<ExpandoObject> queue = queues.GetOrAdd(messageType, _ => new ConcurrentQueue<ExpandoObject>());
            queue.Enqueue(ToExpando(message));
        }

        private static void FlushTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            foreach (KeyValuePair<string, ConcurrentQueue<ExpandoObject>> queuePair in queues)
            {
                List<ExpandoObject> records = new List<ExpandoObject>();
                while (queuePair.Value.TryDequeue(out ExpandoObject record))
                {
                    records.Add(record);
                }

                if (records.Count == 0)
                {
                    continue;
                }

                ConcurrentQueue<ExpandoObject> data = new ConcurrentQueue<ExpandoObject>(records);
                ParquetWriter.Batch batch = new ParquetWriter.Batch(queuePair.Key);
                batch.Add(new ParquetWriter.Batch.SensorData(queuePair.Key, queuePair.Key, data));
                parquetWriter.Add(batch);
                WintapLogger.Log.Append($"DirectParquetSink queued {records.Count} {queuePair.Key} records for parquet write", LogLevel.Info);
            }
        }

        private static ExpandoObject ToExpando(WintapMessage message)
        {
            ExpandoObject expando = new ExpandoObject();
            IDictionary<string, object> values = expando;

            values["MessageType"] = message.MessageType.ToString();
            values["ActivityType"] = message.ActivityType.ToString();
            values["PID"] = message.PID;
            values["EventTime"] = message.EventTime;
            values["AgentId"] = message.AgentId ?? string.Empty;
            values["PidHash"] = message.PidHash ?? string.Empty;
            values["ProcessName"] = message.ProcessName ?? string.Empty;
            values["ProcessPath"] = message.ProcessPath ?? string.Empty;
            values["CapturedUtc"] = DateTime.UtcNow;

            AddScalarProperties(values, "Process", message.Process);
            AddScalarProperties(values, "File", message.File);
            AddScalarProperties(values, "TcpConnection", message.TcpConnection);
            AddScalarProperties(values, "UdpPacket", message.UdpPacket);
            AddScalarProperties(values, "Registry", message.Registry);
            AddScalarProperties(values, "GenericMessage", message.GenericMessage);

            return expando;
        }

        private static void AddScalarProperties(IDictionary<string, object> values, string prefix, object source)
        {
            if (source == null)
            {
                return;
            }

            foreach (PropertyInfo property in source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                object value = property.GetValue(source);
                if (value == null)
                {
                    continue;
                }

                Type valueType = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
                if (valueType.IsPrimitive || valueType.IsEnum || valueType == typeof(string) || valueType == typeof(decimal) || valueType == typeof(DateTime) || valueType == typeof(Guid))
                {
                    values[prefix + "_" + property.Name] = valueType.IsEnum ? value.ToString() : value;
                }
            }
        }
    }
}
