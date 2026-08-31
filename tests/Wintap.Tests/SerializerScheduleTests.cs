using gov.llnl.wintap.core.etl.extract;
using gov.llnl.wintap.core.etl.model;
using Newtonsoft.Json;
using Xunit;

namespace Wintap.Tests
{
    public class SerializerScheduleTests
    {
        [Theory]
        [InlineData("fileserializer", 60, 5, 5)]
        [InlineData("processserializer", 60, 5, 60)]
        [InlineData("fileserializer", 60, 0, 60)]
        [InlineData("fileserializer", 0, 0, 60)]
        public void ResolveIntervalSeconds_OnlyOverridesFileSerializer(
            string serializerName,
            int defaultIntervalSeconds,
            int fileIntervalSeconds,
            int expectedIntervalSeconds)
        {
            Assert.Equal(
                expectedIntervalSeconds,
                SerializerSchedule.ResolveIntervalSeconds(serializerName, defaultIntervalSeconds, fileIntervalSeconds));
        }

        [Theory]
        [InlineData("fileserializer", 4999, 5000, false)]
        [InlineData("fileserializer", 5000, 5000, true)]
        [InlineData("processserializer", 10000, 5000, false)]
        [InlineData("fileserializer", 10000, 0, false)]
        public void ShouldRequestHighWaterFlush_OnlyTriggersFileSerializerAtThreshold(
            string serializerName,
            long queueDepth,
            int highWaterEvents,
            bool expected)
        {
            Assert.Equal(expected, SerializerSchedule.ShouldRequestHighWaterFlush(serializerName, queueDepth, highWaterEvents));
        }

        [Fact]
        public void FileScheduleConfiguration_DistinguishesMissingFromExplicitZero()
        {
            ETLConfig missing = JsonConvert.DeserializeObject<ETLConfig>("{}");
            ETLConfig disabled = JsonConvert.DeserializeObject<ETLConfig>(
                "{\"FileSerializationIntervalSec\":0,\"FileSerializationHighWaterEvents\":0}");

            Assert.Null(missing.FileSerializationIntervalSec);
            Assert.Null(missing.FileSerializationHighWaterEvents);
            Assert.Equal(0, disabled.FileSerializationIntervalSec);
            Assert.Equal(0, disabled.FileSerializationHighWaterEvents);
        }
    }
}
