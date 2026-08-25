using gov.llnl.wintap.core.shared;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.platform.windows.collect.shared;
using gov.llnl.wintap.platform.windows.infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Wintap.Tests
{
    [CollectionDefinition("shc-03-drive-map", DisableParallelization = true)]
    public sealed class DriveMapCollection : ICollectionFixture<DriveMapFixture>
    {
    }

    [Collection("shc-03-drive-map")]
    public sealed class WindowsStateManagerDriveMapTests
    {
        private readonly BaseWindowsSensor sensor = new BaseWindowsSensor();

        [Theory, Trait("Category", "shc-03")]
        [InlineData(@"\Device\HarddiskVolume1", 1)]
        [InlineData(@"\Device\HarddiskVolume3", 3)]
        [InlineData(@"\device\harddiskvolume12", 12)]
        [InlineData(@"\DEVICE\HARDDISKVOLUME7", 7)]
        public void ParseAcceptsExactHarddiskVolumeNames(string deviceName, int expected)
        {
            Assert.True(WindowsStateManager.TryParseHarddiskVolumeNumber(deviceName, out int actual));
            Assert.Equal(expected, actual);
        }

        [Theory, Trait("Category", "shc-03")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(@"\Device\HarddiskVolume")]
        [InlineData(@"\Device\HarddiskVolume3x")]
        [InlineData(@"\Device\HarddiskVolume 3")]
        [InlineData(@"\Device\CdRom0")]
        [InlineData(@"\Device\LanmanRedirector\;X:000000000001d3f\server\share")]
        [InlineData(@"\??\C:\subst\target")]
        [InlineData(@"\Device\Mup\server\share")]
        [InlineData(@"C:\")]
        [InlineData(@"\Device\HarddiskVolume-1")]
        public void ParseRejectsOtherDeviceNames(string deviceName)
        {
            Assert.False(WindowsStateManager.TryParseHarddiskVolumeNumber(deviceName, out _));
        }

        [Fact, Trait("Category", "shc-03")]
        public void BuildDriveMapKeepsOnlyFixedLocalVolumes()
        {
            var targets = new Dictionary<char, string>
            {
                ['C'] = @"\Device\HarddiskVolume3",
                ['D'] = @"\Device\HarddiskVolume4",
                ['E'] = @"\Device\CdRom0",
                ['N'] = @"\Device\LanmanRedirector\;N:0000000000001\server\share",
                ['S'] = @"\??\C:\tmp"
            };

            List<DiskVolume> result = WindowsStateManager.BuildDriveMap(letter => targets.TryGetValue(letter, out string target) ? target : null);

            Assert.Collection(result,
                volume => { Assert.Equal(3, volume.VolumeNumber); Assert.Equal('c', volume.VolumeLetter); },
                volume => { Assert.Equal(4, volume.VolumeNumber); Assert.Equal('d', volume.VolumeLetter); });
        }

        [Fact, Trait("Category", "shc-03")]
        public void BuildDriveMapHandlesAllUnassignedLetters()
        {
            Assert.Empty(WindowsStateManager.BuildDriveMap(_ => null));
        }

        [Fact, Trait("Category", "shc-03")]
        public void BuildDriveMapIsolatesPerLetterFailures()
        {
            List<DiskVolume> result = WindowsStateManager.BuildDriveMap(letter => letter switch
            {
                'C' => throw new InvalidOperationException("query failed"),
                'D' => @"\Device\HarddiskVolume5",
                _ => null
            });

            DiskVolume volume = Assert.Single(result);
            Assert.Equal(5, volume.VolumeNumber);
            Assert.Equal('d', volume.VolumeLetter);
        }

        [Fact, Trait("Category", "shc-03")]
        public void BuildDriveMapQueriesUppercaseLettersInOrder()
        {
            var queried = new List<char>();
            WindowsStateManager.BuildDriveMap(letter => { queried.Add(letter); return null; });
            Assert.Equal(Enumerable.Range('A', 26).Select(value => (char)value), queried);
        }

        [Fact, Trait("Category", "shc-03")]
        public void LiveQueryDosDeviceSmokeDoesNotRequireElevation()
        {
            // An exotic host may legitimately have no HarddiskVolume mappings,
            // so this smoke test validates only returned entries, not non-emptiness.
            List<DiskVolume> result = WindowsStateManager.RefreshDriveMap();
            Assert.All(result, volume =>
            {
                Assert.True(volume.VolumeNumber >= 1);
                Assert.InRange(volume.VolumeLetter, 'a', 'z');
            });
            Assert.Equal(result.Count, result.Select(volume => volume.VolumeLetter).Distinct().Count());
        }

        [Fact, Trait("Category", "shc-03")]
        public void FromNativeUsesVolumeNumberBeyondDriveCount()
        {
            Assert.Equal(@"c:\windows\system32\cmd.exe", sensor.fromNative(
                @"\device\harddiskvolume3\windows\system32\cmd.exe",
                Map((3, 'c'))));
        }

        [Fact, Trait("Category", "shc-03")]
        public void FromNativeSelectsCorrectDriveOnMultiDriveMap()
        {
            Assert.Equal(@"d:\data\report.xlsx", sensor.fromNative(
                @"\device\harddiskvolume4\data\report.xlsx",
                Map((3, 'c'), (4, 'd'))));
        }

        [Theory, Trait("Category", "shc-03")]
        [InlineData(7, 'c', @"\device\harddiskvolume7\x\y.exe", @"c:\x\y.exe")]
        [InlineData(2, 'c', @"\device\harddiskvolume2\a.exe", @"c:\a.exe")]
        public void FromNativePreservesLegacyFallback(int mappedNumber, char mappedLetter, string path, string expected)
        {
            List<DiskVolume> map = mappedNumber == 2 ? new List<DiskVolume>() : Map((3, mappedLetter));
            Assert.Equal(expected, sensor.fromNative(path, map));
        }

        [Fact, Trait("Category", "shc-03")]
        public void FromNativePreservesRemainderAndReplacesMultiDigitNumber()
        {
            Assert.Equal(@"e:\dir\sub\app name.exe", sensor.fromNative(
                @"\device\harddiskvolume12\dir\sub\app name.exe",
                Map((12, 'e'))));
        }

        [Fact, Trait("Category", "shc-03")]
        public void FromNativeRemovesQuotesBeforeTranslation()
        {
            Assert.Equal(@"c:\quoted\app.exe", sensor.fromNative(
                "\"\\device\\harddiskvolume3\\quoted\\app.exe\"",
                Map((3, 'c'))));
        }

        private static List<DiskVolume> Map(params (int Number, char Letter)[] entries)
        {
            return entries.Select(entry => new DiskVolume { VolumeNumber = entry.Number, VolumeLetter = entry.Letter }).ToList();
        }
    }

    public sealed class DriveMapFixture : IDisposable
    {
        private readonly string dataRoot;

        public DriveMapFixture()
        {
            dataRoot = Path.Combine(Path.GetTempPath(), "wintap-shc-03-" + Guid.NewGuid().ToString("N"));
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
