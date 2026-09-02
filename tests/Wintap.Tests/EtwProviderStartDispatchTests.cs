using gov.llnl.wintap.core.collect;
using gov.llnl.wintap.platform.windows.collect.shared;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Wintap.Tests
{
    public sealed class EtwProviderStartDispatchTests
    {
        [Fact, Trait("Category", "wrc-09")]
        public void EtwProviderCollectorStartOverridesBaseSensorStart()
        {
            MethodInfo etwStart = typeof(EtwProviderCollector).GetMethod(nameof(BaseSensor.Start));
            MethodInfo baseStart = typeof(BaseSensor).GetMethod(nameof(BaseSensor.Start));

            Assert.NotNull(etwStart);
            Assert.NotNull(baseStart);
            Assert.Same(baseStart, etwStart.GetBaseDefinition());
        }

        [Fact, Trait("Category", "wrc-09")]
        public void ObservedIndependentEtwProvidersDispatchThroughEtwStartOverride()
        {
            Type collectorType = typeof(EtwProviderCollector);
            MethodInfo baseStart = typeof(BaseSensor).GetMethod(nameof(BaseSensor.Start));
            Type[] observedIndependentTypes = collectorType.Assembly.GetTypes()
                .Where(type => type.FullName == "gov.llnl.wintap.platform.windows.collect.etw.RegistrySensor" ||
                               type.FullName == "gov.llnl.wintap.platform.windows.collect.etw.CpuTriggerSensor")
                .ToArray();

            Assert.Equal(2, observedIndependentTypes.Length);

            foreach (Type providerType in observedIndependentTypes)
            {
                Assert.True(collectorType.IsAssignableFrom(providerType));
                MethodInfo start = providerType.GetMethod(nameof(BaseSensor.Start));
                Assert.NotNull(start);
                Assert.Same(typeof(EtwProviderCollector), start.DeclaringType);
                Assert.Same(baseStart, start.GetBaseDefinition());
            }
        }
    }
}
