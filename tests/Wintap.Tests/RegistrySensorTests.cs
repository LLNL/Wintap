using System;
using System.Collections.Generic;
using System.IO;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.platform.windows.collect.etw;
using Xunit;

namespace Wintap.Tests
{
    [CollectionDefinition("wrc-06-registry-sensor", DisableParallelization = true)]
    public sealed class RegistrySensorCollection : ICollectionFixture<RegistrySensorFixture>
    {
    }

    [Collection("wrc-06-registry-sensor")]
    public sealed class RegistrySensorTests
    {
        private static readonly DateTime Timestamp = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        private const string KeyName = @"\REGISTRY\MACHINE\SOFTWARE\Wrc";

        public static IEnumerable<object[]> DataTypeCases
        {
            get
            {
                yield return TypeCase(0, WintapMessage.DataTypeEnum.NONE);
                yield return TypeCase(1, WintapMessage.DataTypeEnum.STRING);
                yield return TypeCase(2, WintapMessage.DataTypeEnum.EXPAND_SZ);
                yield return TypeCase(3, WintapMessage.DataTypeEnum.BINARY);
                yield return TypeCase(4, WintapMessage.DataTypeEnum.DWORD);
                yield return TypeCase(7, WintapMessage.DataTypeEnum.MULTI_SZ);
                yield return TypeCase(11, WintapMessage.DataTypeEnum.QWORD);
                yield return TypeCase(5, WintapMessage.DataTypeEnum.NONE);
                yield return TypeCase(6, WintapMessage.DataTypeEnum.NONE);
                yield return TypeCase(8, WintapMessage.DataTypeEnum.NONE);
                yield return TypeCase(12, WintapMessage.DataTypeEnum.NONE);
                yield return TypeCase(99, WintapMessage.DataTypeEnum.NONE);
                yield return TypeCase(-1, WintapMessage.DataTypeEnum.NONE);
            }
        }

        public static IEnumerable<object[]> WriteCases
        {
            get
            {
                yield return WriteCase(1,
                    "67-00-6F-00-6F-00-64-00-62-00-79-00-65-00-2D-00-77-00-72-00-63-00-00-00",
                    "68-00-65-00-6C-00-6C-00-6F-00-2D-00-77-00-72-00-63-00-00-00",
                    "goodbye-wrc", "hello-wrc", WintapMessage.DataTypeEnum.STRING);
                yield return WriteCase(2,
                    "25-00-54-00-4D-00-50-00-25-00-5C-00-77-00-72-00-63-00-32-00-00-00",
                    "25-00-54-00-45-00-4D-00-50-00-25-00-5C-00-77-00-72-00-63-00-00-00",
                    @"%TMP%\wrc2", @"%TEMP%\wrc", WintapMessage.DataTypeEnum.EXPAND_SZ);
                yield return WriteCase(3, "CA-FE-BA-BE", "DE-AD-BE-EF",
                    "CA-FE-BA-BE", "DE-AD-BE-EF", WintapMessage.DataTypeEnum.BINARY);
                yield return WriteCase(4, "21-43-65-87", "78-56-34-12",
                    "0x87654321", "0x12345678", WintapMessage.DataTypeEnum.DWORD);
                yield return WriteCase(7,
                    "64-00-65-00-6C-00-74-00-61-00-00-00-65-00-70-00-73-00-69-00-6C-00-6F-00-6E-00-00-00-00-00",
                    "61-00-6C-00-70-00-68-00-61-00-00-00-62-00-65-00-74-00-61-00-00-00-67-00-61-00-6D-00-6D-00-61-00",
                    "delta|epsilon", "alpha|beta|gamma", WintapMessage.DataTypeEnum.MULTI_SZ);
                yield return WriteCase(11, "11-22-33-44-55-66-77-88", "88-77-66-55-44-33-22-11",
                    "0x8877665544332211", "0x1122334455667788", WintapMessage.DataTypeEnum.QWORD);
            }
        }

        [Theory, Trait("Category", "wrc-06")]
        [MemberData(nameof(DataTypeCases))]
        public void MapDataTypeMapsNativeTypesAndDefaultsToNone(int nativeType, WintapMessage.DataTypeEnum expected)
        {
            Assert.Equal(expected, RegistrySensor.MapDataType(nativeType));
        }

        [Theory, Trait("Category", "wrc-06")]
        [InlineData(@"\REGISTRY\MACHINE\SOFTWARE\Waves Audio\MaxxAudio\General", "registry\\machine\\software\\waves audio\\maxxaudio\\general")]
        [InlineData("Registry\\Machine\\Software", "registry\\machine\\software")]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void NormalizeKeyPathProducesLegacyQualifiedForm(string input, string expected)
        {
            Assert.Equal(expected, RegistrySensor.NormalizeKeyPath(input));
        }

        [Fact, Trait("Category", "wrc-06")]
        public void AssembleCreateKeyPathHandlesRelativeAndAbsoluteNames()
        {
            Assert.Equal(
                "registry\\machine\\software\\microsoft\\windows\\currentversion\\capabilityaccessmanager",
                RegistrySensor.AssembleCreateKeyPath(
                    @"\REGISTRY\MACHINE",
                    @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager"));
            Assert.Equal(
                "registry\\machine\\software\\wrcabs",
                RegistrySensor.AssembleCreateKeyPath("ignored", @"\Registry\Machine\Software\WrcAbs"));
        }

        [Fact, Trait("Category", "wrc-06")]
        public void AssembleCreateKeyPathHandlesEdges()
        {
            Assert.Equal("registry\\user", RegistrySensor.AssembleCreateKeyPath(@"\REGISTRY\USER", ""));
            Assert.Equal("", RegistrySensor.AssembleCreateKeyPath("", "relative"));
            Assert.Equal("registry\\machine\\software", RegistrySensor.AssembleCreateKeyPath(@"\REGISTRY\MACHINE\", "Software"));
        }

        [Fact, Trait("Category", "wrc-06")]
        public void HandleCreateKeyEmitsCompleteExplicitContract()
        {
            var messages = new List<WintapMessage>();
            var sensor = Sensor(messages);

            sensor.HandleCreateKey(Timestamp, 42, @"\REGISTRY\MACHINE", @"Software\Wrc");

            WintapMessage message = Assert.Single(messages);
            Assert.Equal(WintapMessage.MessageTypeEnum.Registry, message.MessageType);
            Assert.Equal(WintapMessage.ActivityTypeEnum.CreateKey, message.ActivityType);
            Assert.Equal(42, message.PID);
            Assert.Equal("registry\\machine\\software\\wrc", message.Registry.Path);
            AssertNoDataContract(message, "");
            Assert.Equal(0, message.Registry.PID);
        }

        [Fact, Trait("Category", "wrc-06")]
        public void UnqualifiedCreateKeyIsDropped()
        {
            var messages = new List<WintapMessage>();
            Sensor(messages).HandleCreateKey(Timestamp, 42, "", "relative");
            Assert.Empty(messages);
        }

        [Fact, Trait("Category", "wrc-06")]
        public void FirstWriteEmitsNoneForMissingPreviousValue()
        {
            var messages = new List<WintapMessage>();
            Sensor(messages).HandleSetValue(Timestamp, 7, KeyName, "Value", 1,
                Bytes("68-00-65-00-6C-00-6C-00-6F-00-2D-00-77-00-72-00-63-00-00-00"),
                0, Array.Empty<byte>());

            WintapMessage message = Assert.Single(messages);
            Assert.Equal(WintapMessage.ActivityTypeEnum.Write, message.ActivityType);
            Assert.Equal("hello-wrc", message.Registry.Data);
            Assert.Equal(WintapMessage.DataTypeEnum.STRING, message.Registry.DataType);
            Assert.Equal("", message.Registry.PreviousData);
            Assert.Equal(WintapMessage.DataTypeEnum.NONE, message.Registry.PreviousDataType);
            Assert.Equal(6, (int)message.Registry.PreviousDataType);
            AssertQualified(message);
        }

        [Theory, Trait("Category", "wrc-06")]
        [MemberData(nameof(WriteCases))]
        public void HandleSetValueDecodesSixTypeOverwriteMatrix(
            int type,
            byte[] current,
            byte[] previous,
            string expectedCurrent,
            string expectedPrevious,
            WintapMessage.DataTypeEnum expectedType)
        {
            var messages = new List<WintapMessage>();
            Sensor(messages).HandleSetValue(Timestamp, 7, KeyName, "Value", type, current, type, previous);

            WintapMessage message = Assert.Single(messages);
            Assert.Equal(expectedCurrent, message.Registry.Data);
            Assert.Equal(expectedPrevious, message.Registry.PreviousData);
            Assert.Equal(expectedType, message.Registry.DataType);
            Assert.Equal(expectedType, message.Registry.PreviousDataType);
            AssertQualified(message);
        }

        [Fact, Trait("Category", "wrc-06")]
        public void EmptyKeyNameDropsEveryKeyNameBasedKind()
        {
            var messages = new List<WintapMessage>();
            RegistrySensor sensor = Sensor(messages);

            sensor.HandleSetValue(Timestamp, 1, null, "v", 1, Array.Empty<byte>(), 0, Array.Empty<byte>());
            sensor.HandleDeleteKey(Timestamp, 1, "");
            sensor.HandleDeleteValue(Timestamp, 1, null, "v");
            sensor.HandleQueryValue(Timestamp, 1, "", "v");

            Assert.Empty(messages);
        }

        [Fact, Trait("Category", "wrc-06")]
        public void DeleteRowsNormalizePathsAndSetNoDataExplicitly()
        {
            var messages = new List<WintapMessage>();
            RegistrySensor sensor = Sensor(messages);
            sensor.HandleDeleteKey(Timestamp, 1, KeyName);
            sensor.HandleDeleteValue(Timestamp, 2, KeyName, "Gone");

            Assert.Equal(2, messages.Count);
            Assert.Equal(WintapMessage.ActivityTypeEnum.DeleteKey, messages[0].ActivityType);
            Assert.Equal("registry\\machine\\software\\wrc", messages[0].Registry.Path);
            AssertNoDataContract(messages[0], "");
            Assert.Equal(WintapMessage.ActivityTypeEnum.DeleteValue, messages[1].ActivityType);
            Assert.Equal("registry\\machine\\software\\wrc", messages[1].Registry.Path);
            AssertNoDataContract(messages[1], "Gone");
            Assert.All(messages, AssertQualified);
        }

        [Fact, Trait("Category", "wrc-06")]
        public void ReadGateSeamIsConsultedAndReadCarriesNoData()
        {
            bool consulted = false;
            var messages = new List<WintapMessage>();
            var disabled = new RegistrySensor(messages.Add, () => { consulted = true; return false; });

            Assert.False(disabled.ShouldCollectRegistryRead());
            Assert.True(consulted);
            if (disabled.ShouldCollectRegistryRead())
            {
                disabled.HandleQueryValue(Timestamp, 1, KeyName, "Value");
            }
            Assert.Empty(messages);

            var enabled = new RegistrySensor(messages.Add, () => true);
            if (enabled.ShouldCollectRegistryRead())
            {
                enabled.HandleQueryValue(Timestamp, 1, KeyName, "Value");
            }
            WintapMessage read = Assert.Single(messages);
            Assert.Equal(WintapMessage.ActivityTypeEnum.Read, read.ActivityType);
            AssertNoDataContract(read, "Value");
            AssertQualified(read);
        }

        [Fact, Trait("Category", "wrc-06")]
        public void NonexistentKeyPathEmitsWithoutLiveEnrichment()
        {
            var messages = new List<WintapMessage>();
            Sensor(messages).HandleSetValue(
                Timestamp,
                3,
                @"\REGISTRY\MACHINE\SOFTWARE\WrcDoesNotExist\Child",
                "Value",
                3,
                Bytes("DE-AD-BE-EF"),
                0,
                Array.Empty<byte>());

            WintapMessage message = Assert.Single(messages);
            Assert.Equal("registry\\machine\\software\\wrcdoesnotexist\\child", message.Registry.Path);
            Assert.Equal("DE-AD-BE-EF", message.Registry.Data);
        }

        [Fact, Trait("Category", "wrc-06")]
        public void EveryHandlerEmitsAtMostOnce()
        {
            var messages = new List<WintapMessage>();
            RegistrySensor sensor = Sensor(messages);

            sensor.HandleCreateKey(Timestamp, 1, @"\REGISTRY\MACHINE", "A");
            Assert.Single(messages);
            sensor.HandleDeleteKey(Timestamp, 1, KeyName);
            Assert.Equal(2, messages.Count);
            sensor.HandleDeleteValue(Timestamp, 1, KeyName, "V");
            Assert.Equal(3, messages.Count);
            sensor.HandleSetValue(Timestamp, 1, KeyName, "V", 3, Bytes("01"), 0, Array.Empty<byte>());
            Assert.Equal(4, messages.Count);
            sensor.HandleQueryValue(Timestamp, 1, KeyName, "V");
            Assert.Equal(5, messages.Count);
        }

        private static RegistrySensor Sensor(List<WintapMessage> messages)
        {
            return new RegistrySensor(messages.Add, () => true);
        }

        private static object[] TypeCase(int nativeType, WintapMessage.DataTypeEnum expected)
        {
            return new object[] { nativeType, expected };
        }

        private static object[] WriteCase(
            int type,
            string current,
            string previous,
            string expectedCurrent,
            string expectedPrevious,
            WintapMessage.DataTypeEnum expectedType)
        {
            return new object[] { type, Bytes(current), Bytes(previous), expectedCurrent, expectedPrevious, expectedType };
        }

        private static byte[] Bytes(string hex)
        {
            return Convert.FromHexString(hex.Replace("-", string.Empty));
        }

        private static void AssertNoDataContract(WintapMessage message, string valueName)
        {
            Assert.Equal(valueName, message.Registry.ValueName);
            Assert.Equal("", message.Registry.Data);
            Assert.Equal(WintapMessage.DataTypeEnum.NONE, message.Registry.DataType);
            Assert.Equal("", message.Registry.PreviousData);
            Assert.Equal(WintapMessage.DataTypeEnum.NONE, message.Registry.PreviousDataType);
        }

        private static void AssertQualified(WintapMessage message)
        {
            Assert.True(message.Registry.Path == "registry"
                || message.Registry.Path.StartsWith("registry\\", StringComparison.Ordinal));
        }
    }

    public sealed class RegistrySensorFixture : IDisposable
    {
        private readonly string dataRoot;

        public RegistrySensorFixture()
        {
            dataRoot = Path.Combine(Path.GetTempPath(), "wintap-wrc-06-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            Env.SetDataRoot(dataRoot);
            WintapLogger.Log.Init();
        }

        public void Dispose()
        {
            Env.SetDataRoot(null);
            try { Directory.Delete(dataRoot, recursive: true); } catch { }
        }
    }
}
