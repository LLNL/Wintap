using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using gov.llnl.wintap.core.shared;
using Xunit;

namespace Wintap.Tests
{
    public class RuntimeDataRootTests
    {
        [Fact]
        [Trait("Category", "wpc-09")]
        public void ResolveDataRoot_ProgrammaticOverrideWins()
        {
            string result = Env.ResolveDataRoot(
                @"D:\programmatic",
                @"D:\configured",
                OSPlatform.Windows,
                @"C:\ProgramData");

            Assert.Equal(@"D:\programmatic", result);
        }

        [Fact]
        [Trait("Category", "wpc-09")]
        public void ResolveDataRoot_ConfiguredValueWinsPlatformDefault()
        {
            string result = Env.ResolveDataRoot(
                null,
                @"D:\configured",
                OSPlatform.Windows,
                @"C:\ProgramData");

            Assert.Equal(@"D:\configured", result);
        }

        [Theory]
        [Trait("Category", "wpc-09")]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", "\t")]
        public void ResolveDataRoot_EmptyValuesUsePlatformDefault(string overridden, string configured)
        {
            string result = Env.ResolveDataRoot(
                overridden,
                configured,
                OSPlatform.Windows,
                @"C:\ProgramData");

            Assert.Equal(Path.Combine(@"C:\ProgramData", "Wintap"), result);
        }

        [Theory]
        [Trait("Category", "wpc-09")]
        [InlineData("WINDOWS", @"C:\ProgramData\Wintap")]
        [InlineData("OSX", "/Library/Application Support/Mactap")]
        [InlineData("LINUX", "/var/lib/lintap")]
        public void ResolveDataRoot_UsesExpectedPlatformDefault(string platformName, string expected)
        {
            OSPlatform platform = platformName == "WINDOWS"
                ? OSPlatform.Windows
                : platformName == "OSX"
                    ? OSPlatform.OSX
                    : OSPlatform.Linux;

            string result = Env.ResolveDataRoot(null, null, platform, @"C:\ProgramData");

            Assert.Equal(expected, result);
        }

        [Fact]
        [Trait("Category", "wpc-09")]
        public void ConfigRoot_HasNoDataRootDefault()
        {
            Assert.True(string.IsNullOrEmpty(new ConfigRoot().DataRoot));
        }

        [Fact]
        [Trait("Category", "wpc-09")]
        public void ShippedEtlConfig_HasNoDataRootOverride()
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "ETLConfig.json");
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));

            Assert.False(config.RootElement.TryGetProperty("DataRoot", out _));
        }
    }
}
