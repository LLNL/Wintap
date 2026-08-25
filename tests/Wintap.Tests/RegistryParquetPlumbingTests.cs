using com.espertech.esper.common.client;
using com.espertech.esper.common.client.configuration;
using com.espertech.esper.compat;
using com.espertech.esper.compiler.client;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.etl.extract;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using Xunit;

namespace Wintap.Tests
{
    public sealed class RegistryParquetPlumbingTests
    {
        private const long FirstSeen = 133700000000000000L;
        private const long LastSeen = 133700000100000000L;

        [Fact, Trait("Category", "wrc-08")]
        public void EmbeddedEplSelectsAllRegistryValueFields()
        {
            string epl = ReadEmbeddedRegistryEpl();

            Assert.Contains("registry.dataType as dataType", epl);
            Assert.Contains("registry.data as data", epl);
            Assert.Contains("registry.previousData as previousData", epl);
            Assert.Contains("registry.previousDataType as previousDataType", epl);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void EmbeddedEplGroupsByPreviousValueFields()
        {
            string groupBy = ReadEmbeddedRegistryEpl()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Single(line => line.StartsWith("group by ", StringComparison.Ordinal));

            Assert.Contains("registry.previousData", groupBy);
            Assert.Contains("registry.previousDataType", groupBy);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void EmbeddedEplCompilesAgainstWintapMessage()
        {
            var configuration = new Configuration();
            configuration.Common.EventMeta.ClassPropertyResolutionStyle = PropertyResolutionStyle.CASE_INSENSITIVE;
            configuration.Common.AddEventType(typeof(WintapMessage));
            configuration.Compiler.ByteCode.IsAllowSubscriber = true;
            configuration.Compiler.ByteCode.SetAccessModifiersPublic();
            configuration.Compiler.ByteCode.BusModifierEventType = com.espertech.esper.common.client.util.EventTypeBusModifier.BUS;

            EPCompiled compiled = EPCompilerProvider.Compiler.Compile(
                ReadEmbeddedRegistryEpl(),
                new CompilerArguments(configuration));

            Assert.NotNull(compiled);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void BuildFlatMessagePreservesEighteenColumnContractAndOrder()
        {
            IDictionary<string, object> result = BuildFlatMessage();
            string[] expected =
            {
                "AgentId", "ActivityType", "ProcessName", "Reg_Data", "Reg_DataType",
                "Reg_PreviousData", "Reg_PreviousDataType", "EventCount", "FirstSeenMs",
                "LastSeenMs", "PID", "PidHash", "HostHame", "Reg_Path", "Reg_Value",
                "Reg_Id_Hash", "MessageType", "EventTime"
            };

            Assert.Equal(expected, result.Keys);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void BuildFlatMessageRendersEnumNamesNeverOrdinals()
        {
            IDictionary<string, object> stringResult = BuildFlatMessage();
            IDictionary<string, object> qwordResult = BuildFlatMessage(fields =>
            {
                fields["dataType"] = WintapMessage.DataTypeEnum.QWORD;
                fields["previousDataType"] = WintapMessage.DataTypeEnum.NONE;
            });

            Assert.Equal("STRING", stringResult["Reg_DataType"]);
            Assert.Equal("STRING", stringResult["Reg_PreviousDataType"]);
            Assert.Equal("QWORD", qwordResult["Reg_DataType"]);
            Assert.Equal("NONE", qwordResult["Reg_PreviousDataType"]);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void BuildFlatMessageEncodesFirstWriteWithoutPreviousValue()
        {
            IDictionary<string, object> result = BuildFlatMessage(fields =>
            {
                fields["previousData"] = string.Empty;
                fields["previousDataType"] = WintapMessage.DataTypeEnum.NONE;
            });

            Assert.Equal(string.Empty, result["Reg_PreviousData"]);
            Assert.Equal("NONE", result["Reg_PreviousDataType"]);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void BuildFlatMessageNullGuardsOnlyNewValueColumns()
        {
            IDictionary<string, object> result = BuildFlatMessage(fields =>
            {
                fields["dataType"] = null;
                fields["previousData"] = null;
                fields["previousDataType"] = null;
            });

            Assert.Equal("NONE", result["Reg_DataType"]);
            Assert.Equal(string.Empty, result["Reg_PreviousData"]);
            Assert.Equal("NONE", result["Reg_PreviousDataType"]);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void BuildFlatMessagePreservesExistingColumnSemantics()
        {
            IDictionary<string, object> result = BuildFlatMessage();

            Assert.Equal(@"registry\machine\software\wintap\test", result["Reg_Path"]);
            Assert.Equal(@"registry\machine\software\wintap\test|TestValue", result["Reg_Id_Hash"]);
            Assert.Equal("PROCESS_REGISTRY", result["MessageType"]);
            Assert.Equal(DateTime.FromFileTimeUtc(FirstSeen), result["EventTime"]);
            Assert.Equal(FirstSeen, result["FirstSeenMs"]);
            Assert.Equal(LastSeen, result["LastSeenMs"]);
            Assert.Equal(3, result["EventCount"]);
            Assert.Equal("testhost", result["HostHame"]);
            Assert.Equal("agent-1", result["AgentId"]);
        }

        [Fact, Trait("Category", "wrc-08")]
        public void BuildFlatMessageKeepsCurrentAndPreviousValuesIndependent()
        {
            IDictionary<string, object> result = BuildFlatMessage();

            Assert.Equal("hello-wrc-2", result["Reg_Data"]);
            Assert.Equal("hello-wrc", result["Reg_PreviousData"]);
        }

        private static string ReadEmbeddedRegistryEpl()
        {
            using Stream stream = typeof(EventChannel).Assembly.GetManifestResourceStream(
                "gov.llnl.wintap.core.etl.esper.registry.epl");
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static IDictionary<string, object> BuildFlatMessage(
            Action<Dictionary<string, object>> modify = null)
        {
            var fields = new Dictionary<string, object>
            {
                ["activityType"] = "Write",
                ["ProcessName"] = "testproc",
                ["data"] = "hello-wrc-2",
                ["dataType"] = WintapMessage.DataTypeEnum.STRING,
                ["previousData"] = "hello-wrc",
                ["previousDataType"] = WintapMessage.DataTypeEnum.STRING,
                ["eventCount"] = 3L,
                ["firstSeen"] = FirstSeen,
                ["lastSeen"] = LastSeen,
                ["PID"] = 1234,
                ["PidHash"] = "abc123",
                ["path"] = @"REGISTRY\MACHINE\SOFTWARE\Wintap\Test",
                ["valueName"] = "TestValue"
            };
            modify?.Invoke(fields);

            ExpandoObject flatMessage = RegistrySerializer.BuildFlatMessage(
                name => fields[name],
                "agent-1",
                "testhost",
                (path, valueName) => path + "|" + valueName);

            return (IDictionary<string, object>)flatMessage;
        }
    }
}
