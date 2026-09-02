using gov.llnl.wintap.platform.windows.infrastructure;
using Microsoft.Diagnostics.Tracing.Parsers;
using Xunit;

namespace Wintap.Tests
{
    public class WindowsSubscriptionManagerTests
    {
        [Fact]
        [Trait("Category", "wpc-10a")]
        public void DecideFinalKernelEnable_UnchangedSuccessfulMaskSkipsSecondEnable()
        {
            KernelEnableDecision decision = WindowsSubscriptionManager.DecideFinalKernelEnable(
                true,
                KernelTraceEventParser.Keywords.Process,
                KernelTraceEventParser.Keywords.Process);

            Assert.Equal(KernelEnableDecision.FinalFlagsAlreadyEnabled, decision);
        }

        [Fact]
        [Trait("Category", "wpc-10a")]
        public void DecideFinalKernelEnable_EarlyFailureRetriesFinalEnable()
        {
            KernelEnableDecision decision = WindowsSubscriptionManager.DecideFinalKernelEnable(
                false,
                KernelTraceEventParser.Keywords.None,
                KernelTraceEventParser.Keywords.Process);

            Assert.Equal(KernelEnableDecision.EnableFinalFlags, decision);
        }

        [Fact]
        [Trait("Category", "wpc-10a")]
        public void DecideFinalKernelEnable_LargerFinalMaskFailsClosed()
        {
            KernelEnableDecision decision = WindowsSubscriptionManager.DecideFinalKernelEnable(
                true,
                KernelTraceEventParser.Keywords.Process,
                KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad);

            Assert.Equal(KernelEnableDecision.FailMissingFlags, decision);
        }
    }
}
