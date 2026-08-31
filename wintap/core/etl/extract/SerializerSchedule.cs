namespace gov.llnl.wintap.core.etl.extract
{
    internal static class SerializerSchedule
    {
        internal static int ResolveIntervalSeconds(string serializerName, int defaultIntervalSeconds, int fileIntervalSeconds)
        {
            int fallback = defaultIntervalSeconds > 0 ? defaultIntervalSeconds : 60;
            if (serializerName == "fileserializer" && fileIntervalSeconds > 0)
            {
                return fileIntervalSeconds;
            }

            return fallback;
        }

        internal static bool ShouldRequestHighWaterFlush(string serializerName, long queueDepth, int highWaterEvents)
        {
            return serializerName == "fileserializer" && highWaterEvents > 0 && queueDepth >= highWaterEvents;
        }
    }
}
