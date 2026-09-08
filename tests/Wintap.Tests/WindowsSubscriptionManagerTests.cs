using gov.llnl.wintap.platform.windows.infrastructure;
using gov.llnl.wintap.platform.windows.collect.etw;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Reflection;
using Xunit;

namespace Wintap.Tests
{
    public class WindowsSubscriptionManagerTests
    {
        [Fact]
        [Trait("Category", "wpc-collector-combinations")]
        public void ComputeFinalKernelFlags_ProcessOnlyUsesProcessFlag()
        {
            KernelTraceEventParser.Keywords flags = WindowsSubscriptionManager.ComputeFinalKernelFlags(null);

            Assert.Equal(KernelTraceEventParser.Keywords.Process, flags);
        }

        [Fact]
        [Trait("Category", "wpc-collector-combinations")]
        public void ComputeFinalKernelFlags_ProcessAndFileIncludesFileIoInitAndDiskFileIo()
        {
            KernelTraceEventParser.Keywords flags = WindowsSubscriptionManager.ComputeFinalKernelFlags(
                new[] { KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.DiskFileIO });

            Assert.Equal(
                KernelTraceEventParser.Keywords.Process |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.DiskFileIO,
                flags);
        }

        [Fact]
        [Trait("Category", "fio-01")]
        public void FileSensor_DeclaresFileIoInitAndDiskFileIoKernelFlags()
        {
            KernelTraceEventParser.Keywords flags = WindowsSubscriptionManager.GetKernelTraceFlags(new FileSensor());

            Assert.True(flags.HasFlag(KernelTraceEventParser.Keywords.FileIOInit));
            Assert.True(flags.HasFlag(KernelTraceEventParser.Keywords.DiskFileIO));
        }

        [Fact]
        [Trait("Category", "fio-02")]
        public void FileSensor_SubscriptionPlanSuppressesCloseAndPreservesDefaultHandlers()
        {
            FileSensor.FileIoSubscription[] plan = FileSensor.BuildSubscriptionPlan(collectFileRead: false);

            Assert.Contains(FileSensor.FileIoSubscription.Write, plan);
            Assert.Contains(FileSensor.FileIoSubscription.Delete, plan);
            Assert.Contains(FileSensor.FileIoSubscription.Name, plan);
            Assert.Contains(FileSensor.FileIoSubscription.Create, plan);
            Assert.DoesNotContain(FileSensor.FileIoSubscription.Read, plan);
            Assert.DoesNotContain(FileSensor.FileIoSubscription.Close, plan);
        }

        [Fact]
        [Trait("Category", "fio-02")]
        public void FileSensor_SubscriptionPlanKeepsReadSettingGateAndStillSuppressesClose()
        {
            FileSensor.FileIoSubscription[] plan = FileSensor.BuildSubscriptionPlan(collectFileRead: true);

            Assert.Contains(FileSensor.FileIoSubscription.Write, plan);
            Assert.Contains(FileSensor.FileIoSubscription.Delete, plan);
            Assert.Contains(FileSensor.FileIoSubscription.Name, plan);
            Assert.Contains(FileSensor.FileIoSubscription.Create, plan);
            Assert.Contains(FileSensor.FileIoSubscription.Read, plan);
            Assert.DoesNotContain(FileSensor.FileIoSubscription.Close, plan);
        }

        [Fact]
        [Trait("Category", "fio-02")]
        public void FileSensor_CloseSuppressionPreservesFileIoInitAndDiskFileIoKernelFlags()
        {
            KernelTraceEventParser.Keywords flags = WindowsSubscriptionManager.GetKernelTraceFlags(new FileSensor());

            Assert.True(flags.HasFlag(KernelTraceEventParser.Keywords.FileIOInit));
            Assert.True(flags.HasFlag(KernelTraceEventParser.Keywords.DiskFileIO));
        }

        [Fact]
        [Trait("Category", "fio-03")]
        public void FileSensor_TraceEventOnlyPathSelectionUsesProvidedFileName()
        {
            string path = FileSensor.SelectTraceEventFileNameOnly(@"c:\temp\traceevent-path.txt");

            Assert.Equal(@"c:\temp\traceevent-path.txt", path);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [Trait("Category", "fio-03")]
        public void FileSensor_TraceEventOnlyPathSelectionDoesNotReplaceMissingFileName(string traceEventFileName)
        {
            string path = FileSensor.SelectTraceEventFileNameOnly(traceEventFileName);

            Assert.Equal(traceEventFileName, path);
        }

        [Fact]
        [Trait("Category", "fio-03")]
        public void FileSensor_DiagnosticFallbackDisablePreservesCloseSuppressionAndKernelFlags()
        {
            FileSensor.FileIoSubscription[] plan = FileSensor.BuildSubscriptionPlan(collectFileRead: true);
            KernelTraceEventParser.Keywords flags = WindowsSubscriptionManager.GetKernelTraceFlags(new FileSensor());

            Assert.DoesNotContain(FileSensor.FileIoSubscription.Close, plan);
            Assert.True(flags.HasFlag(KernelTraceEventParser.Keywords.FileIOInit));
            Assert.True(flags.HasFlag(KernelTraceEventParser.Keywords.DiskFileIO));
        }

        [Fact]
        [Trait("Category", "wpc-collector-combinations")]
        public void CreateStartupPlan_MultipleSharedKernelSensorsComputesSingleFinalMask()
        {
            CollectorStartupPlan plan = WindowsSubscriptionManager.CreateStartupPlan(new[]
            {
                WindowsSubscriptionManager.CreateCollectorEntry("File", new FileSensor()),
                WindowsSubscriptionManager.CreateCollectorEntry("Tcp", new TcpSensor()),
                WindowsSubscriptionManager.CreateCollectorEntry("Udp", new UdpSensor()),
                WindowsSubscriptionManager.CreateCollectorEntry("ImageLoad", new ImageLoadSensor())
            });

            KernelTraceEventParser.Keywords expected =
                KernelTraceEventParser.Keywords.Process |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.DiskFileIO |
                KernelTraceEventParser.Keywords.NetworkTCPIP |
                KernelTraceEventParser.Keywords.ImageLoad;

            Assert.Equal(expected, plan.FinalKernelFlags);
            Assert.Equal(4, plan.SharedKernelSensors.Count);
            Assert.Empty(plan.IndependentSensors);
        }

        [Fact]
        [Trait("Category", "wpc-collector-combinations")]
        public void CreateStartupPlan_RegistryAndCpuTriggerAreIndependent()
        {
            CollectorStartupPlan plan = WindowsSubscriptionManager.CreateStartupPlan(new[]
            {
                WindowsSubscriptionManager.CreateCollectorEntry("Registry", new RegistrySensor()),
                WindowsSubscriptionManager.CreateCollectorEntry("CpuTrigger", new CpuTriggerSensor())
            });

            Assert.Equal(KernelTraceEventParser.Keywords.Process, plan.FinalKernelFlags);
            Assert.Empty(plan.SharedKernelSensors);
            Assert.Equal(2, plan.IndependentSensors.Count);
        }

        [Fact]
        [Trait("Category", "wpc-collector-combinations")]
        public void StartupPlan_EnablesKernelBeforeSharedAndIndependentSensors()
        {
            CollectorStartupPlan plan = WindowsSubscriptionManager.CreateStartupPlan(new[]
            {
                WindowsSubscriptionManager.CreateCollectorEntry("File", new FileSensor()),
                WindowsSubscriptionManager.CreateCollectorEntry("Registry", new RegistrySensor())
            });

            AssertOrder(plan, "EnableKernelProvider", "StartSharedKernelSensors");
            AssertOrder(plan, "StartSharedKernelSensors", "StartKernelConsumer");
            AssertOrder(plan, "StartKernelConsumer", "StartIndependentSensors");
        }

        [Fact]
        [Trait("Category", "wpc-collector-combinations")]
        public void FileSensor_StartOverridesBaseForSharedKernelSubscriptionPath()
        {
            MethodInfo start = typeof(FileSensor).GetMethod("Start", BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(start);
            Assert.Equal(typeof(FileSensor), start.DeclaringType);
            Assert.True(WindowsSubscriptionManager.IsSharedKernelSensor(new FileSensor()));
        }

        private static void AssertOrder(CollectorStartupPlan plan, string earlier, string later)
        {
            int earlierIndex = IndexOf(plan.StartupPhases, earlier);
            int laterIndex = IndexOf(plan.StartupPhases, later);
            Assert.True(earlierIndex >= 0, $"Missing startup phase {earlier}");
            Assert.True(laterIndex >= 0, $"Missing startup phase {later}");
            Assert.True(earlierIndex < laterIndex, $"Expected {earlier} before {later}");
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
