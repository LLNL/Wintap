using System;
using System.Reflection;
using gov.llnl.wintap.collect.models;
using Xunit;

namespace Wintap.Tests
{
    public class WintapMessageRegistrySchemaTests
    {
        [Fact]
        [Trait("Category", "wrc-05")]
        public void DataTypeEnum_PreservesOrdinalsAndAppendsNewMembers()
        {
            Assert.Equal(0, (int)WintapMessage.DataTypeEnum.STRING);
            Assert.Equal(1, (int)WintapMessage.DataTypeEnum.DWORD);
            Assert.Equal(2, (int)WintapMessage.DataTypeEnum.BINARY);
            Assert.Equal(3, (int)WintapMessage.DataTypeEnum.MULTI_SZ);
            Assert.Equal(4, (int)WintapMessage.DataTypeEnum.EXPAND_SZ);
            Assert.Equal(5, (int)WintapMessage.DataTypeEnum.QWORD);
            Assert.Equal(6, (int)WintapMessage.DataTypeEnum.NONE);
        }

        [Theory]
        [InlineData("QWORD", false, WintapMessage.DataTypeEnum.QWORD, "QWORD")]
        [InlineData("NONE", false, WintapMessage.DataTypeEnum.NONE, "NONE")]
        [InlineData("qword", true, WintapMessage.DataTypeEnum.QWORD, "QWORD")]
        [Trait("Category", "wrc-05")]
        public void DataTypeEnum_ParsesAndRoundTrips(
            string value,
            bool ignoreCase,
            WintapMessage.DataTypeEnum expected,
            string expectedName)
        {
            var parsed = Enum.Parse<WintapMessage.DataTypeEnum>(value, ignoreCase);

            Assert.Equal(expected, parsed);
            Assert.Equal(expectedName, parsed.ToString());
        }

        [Fact]
        [Trait("Category", "wrc-05")]
        public void RegistryMessage_RoundTripsAllRegistryProperties()
        {
            var registry = new WintapMessage.RegActivityObject
            {
                Path = @"\REGISTRY\MACHINE\SOFTWARE\Wintap",
                DataType = WintapMessage.DataTypeEnum.QWORD,
                ValueName = "ExampleQword",
                Data = "0x1122334455667788",
                PreviousData = "0x8877665544332211",
                PreviousDataType = WintapMessage.DataTypeEnum.QWORD,
                PID = 1234
            };
            var message = new WintapMessage(
                DateTime.UtcNow,
                1234,
                WintapMessage.MessageTypeEnum.Registry)
            {
                Registry = registry
            };

            Assert.Equal(@"\REGISTRY\MACHINE\SOFTWARE\Wintap", message.Registry.Path);
            Assert.Equal(WintapMessage.DataTypeEnum.QWORD, message.Registry.DataType);
            Assert.Equal("ExampleQword", message.Registry.ValueName);
            Assert.Equal("0x1122334455667788", message.Registry.Data);
            Assert.Equal("0x8877665544332211", message.Registry.PreviousData);
            Assert.Equal(WintapMessage.DataTypeEnum.QWORD, message.Registry.PreviousDataType);
            Assert.Equal(1234, message.Registry.PID);
        }

        [Fact]
        [Trait("Category", "wrc-05")]
        public void RegistryActivity_FirstWriteEncodingRoundTrips()
        {
            var registry = new WintapMessage.RegActivityObject
            {
                PreviousData = "",
                PreviousDataType = WintapMessage.DataTypeEnum.NONE
            };

            Assert.Equal("", registry.PreviousData);
            Assert.Equal(WintapMessage.DataTypeEnum.NONE, registry.PreviousDataType);
        }

        [Fact]
        [Trait("Category", "wrc-05")]
        public void RegistryActivity_DefaultsRemainDocumentedValues()
        {
            var registry = new WintapMessage.RegActivityObject();

            Assert.Null(registry.PreviousData);
            Assert.Equal(WintapMessage.DataTypeEnum.STRING, registry.PreviousDataType);
        }

        [Theory]
        [InlineData("PreviousData", typeof(string))]
        [InlineData("PreviousDataType", typeof(WintapMessage.DataTypeEnum))]
        [Trait("Category", "wrc-05")]
        public void RegistryActivity_NewPropertiesAreSerializerVisible(string name, Type expectedType)
        {
            AssertPublicReadWriteProperty(name, expectedType);
        }

        [Theory]
        [InlineData("Path", typeof(string))]
        [InlineData("DataType", typeof(WintapMessage.DataTypeEnum))]
        [InlineData("ValueName", typeof(string))]
        [InlineData("Data", typeof(string))]
        [InlineData("PID", typeof(int))]
        [Trait("Category", "wrc-05")]
        public void RegistryActivity_ExistingPropertiesRemainUnchanged(string name, Type expectedType)
        {
            AssertPublicReadWriteProperty(name, expectedType);
        }

        private static void AssertPublicReadWriteProperty(string name, Type expectedType)
        {
            var property = typeof(WintapMessage.RegActivityObject).GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(property);
            Assert.Equal(expectedType, property.PropertyType);
            Assert.True(property.GetMethod.IsPublic);
            Assert.True(property.SetMethod.IsPublic);
        }
    }
}
