using System;
using System.Collections.Generic;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using Xunit;

namespace Wintap.Tests
{
    public class RegistryPayloadDecoderTests
    {
        private const string InitialSz = "68-00-65-00-6C-00-6C-00-6F-00-2D-00-77-00-72-00-63-00-00-00";
        private const string InitialExpandSz = "25-00-54-00-45-00-4D-00-50-00-25-00-5C-00-77-00-72-00-63-00-00-00";
        private const string InitialDword = "78-56-34-12";
        private const string InitialQword = "88-77-66-55-44-33-22-11";
        private const string InitialBinary = "DE-AD-BE-EF";
        private const string InitialMultiSz = "61-00-6C-00-70-00-68-00-61-00-00-00-62-00-65-00-74-00-61-00-00-00-67-00-61-00-6D-00-6D-00-61-00";

        public static IEnumerable<object[]> InitialWriteFixtures
        {
            get
            {
                yield return Fixture(1, InitialSz, "hello-wrc");
                yield return Fixture(2, InitialExpandSz, @"%TEMP%\wrc");
                yield return Fixture(4, InitialDword, "0x12345678");
                yield return Fixture(11, InitialQword, "0x1122334455667788");
                yield return Fixture(3, InitialBinary, "DE-AD-BE-EF");
                yield return Fixture(7, InitialMultiSz, "alpha|beta|gamma");
            }
        }

        public static IEnumerable<object[]> OverwriteFixtures
        {
            get
            {
                yield return Fixture(1, "67-00-6F-00-6F-00-64-00-62-00-79-00-65-00-2D-00-77-00-72-00-63-00-00-00", "goodbye-wrc");
                yield return Fixture(2, "25-00-54-00-4D-00-50-00-25-00-5C-00-77-00-72-00-63-00-32-00-00-00", @"%TMP%\wrc2");
                yield return Fixture(4, "21-43-65-87", "0x87654321");
                yield return Fixture(11, "11-22-33-44-55-66-77-88", "0x8877665544332211");
                yield return Fixture(3, "CA-FE-BA-BE", "CA-FE-BA-BE");
                yield return Fixture(7, "64-00-65-00-6C-00-74-00-61-00-00-00-65-00-70-00-73-00-69-00-6C-00-6F-00-6E-00-00-00-00-00", "delta|epsilon");
            }
        }

        public static IEnumerable<object[]> PreviousDataFixtures => InitialWriteFixtures;

        public static IEnumerable<object[]> EventIdFixtures
        {
            get
            {
                yield return new object[] { 1, nameof(RegistryEventKind.CreateKey) };
                yield return new object[] { 2, nameof(RegistryEventKind.OpenKey) };
                yield return new object[] { 3, nameof(RegistryEventKind.DeleteKey) };
                yield return new object[] { 4, nameof(RegistryEventKind.QueryKey) };
                yield return new object[] { 5, nameof(RegistryEventKind.SetValueKey) };
                yield return new object[] { 6, nameof(RegistryEventKind.DeleteValueKey) };
                yield return new object[] { 7, nameof(RegistryEventKind.QueryValueKey) };
                yield return new object[] { 8, nameof(RegistryEventKind.EnumerateKey) };
                yield return new object[] { 9, nameof(RegistryEventKind.EnumerateValueKey) };
                yield return new object[] { 10, nameof(RegistryEventKind.QueryMultipleValueKey) };
                yield return new object[] { 11, nameof(RegistryEventKind.SetInformationKey) };
                yield return new object[] { 12, nameof(RegistryEventKind.FlushKey) };
                yield return new object[] { 13, nameof(RegistryEventKind.CloseKey) };
                yield return new object[] { 14, nameof(RegistryEventKind.QuerySecurityKey) };
                yield return new object[] { 15, nameof(RegistryEventKind.SetSecurityKey) };
                yield return new object[] { 0, nameof(RegistryEventKind.Unknown) };
                yield return new object[] { 16, nameof(RegistryEventKind.Unknown) };
                yield return new object[] { 999, nameof(RegistryEventKind.Unknown) };
                yield return new object[] { -1, nameof(RegistryEventKind.Unknown) };
            }
        }

        [Theory]
        [Trait("Category", "wrc-03")]
        [MemberData(nameof(InitialWriteFixtures))]
        public void DecodeRegValue_DecodesInitialWriteFixtures(int regType, byte[] data, string expected)
        {
            Assert.Equal(expected, RegistryPayloadDecoder.DecodeRegValue(regType, data));
        }

        [Theory]
        [Trait("Category", "wrc-03")]
        [MemberData(nameof(OverwriteFixtures))]
        public void DecodeRegValue_DecodesOverwriteFixtures(int regType, byte[] data, string expected)
        {
            Assert.Equal(expected, RegistryPayloadDecoder.DecodeRegValue(regType, data));
        }

        [Theory]
        [Trait("Category", "wrc-03")]
        [MemberData(nameof(PreviousDataFixtures))]
        public void DecodeRegValue_DecodesPreviousDataFixtures(int regType, byte[] data, string expected)
        {
            Assert.Equal(expected, RegistryPayloadDecoder.DecodeRegValue(regType, data));
        }

        [Theory]
        [Trait("Category", "wrc-03")]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(11)]
        [InlineData(0)]
        [InlineData(99)]
        public void DecodeRegValue_ReturnsEmpty_ForNullOrEmptyData(int regType)
        {
            Assert.Equal(string.Empty, RegistryPayloadDecoder.DecodeRegValue(regType, null));
            Assert.Equal(string.Empty, RegistryPayloadDecoder.DecodeRegValue(regType, Array.Empty<byte>()));
        }

        [Theory]
        [Trait("Category", "wrc-03")]
        [InlineData(4, "78-56", "(type 4) 78-56")]
        [InlineData(11, "88-77-66-55", "(type 11) 88-77-66-55")]
        public void DecodeRegValue_UsesFallback_ForTruncatedNumericData(int regType, string bytes, string expected)
        {
            Assert.Equal(expected, RegistryPayloadDecoder.DecodeRegValue(regType, ParseBytes(bytes)));
        }

        [Theory]
        [Trait("Category", "wrc-03")]
        [InlineData(5, "DE-AD", "(type 5) DE-AD")]
        [InlineData(0, "01", "(type 0) 01")]
        public void DecodeRegValue_UsesFallback_ForUnknownTypes(int regType, string bytes, string expected)
        {
            Assert.Equal(expected, RegistryPayloadDecoder.DecodeRegValue(regType, ParseBytes(bytes)));
        }

        [Fact]
        [Trait("Category", "wrc-03")]
        public void DecodeRegValue_DoesNotExpandExpandString()
        {
            string result = RegistryPayloadDecoder.DecodeRegValue(2, ParseBytes(InitialExpandSz));

            Assert.Contains("%TEMP%", result);
            Assert.Equal(@"%TEMP%\wrc", result);
        }

        [Fact]
        [Trait("Category", "wrc-03")]
        public void DecodeRegValue_DecodesSzWithoutTerminatingNull()
        {
            Assert.Equal("hi", RegistryPayloadDecoder.DecodeRegValue(1, ParseBytes("68-00-69-00")));
        }

        [Fact]
        [Trait("Category", "wrc-03")]
        public void DecodeRegValue_DecodesSingleMultiSzValue()
        {
            Assert.Equal("a", RegistryPayloadDecoder.DecodeRegValue(7, ParseBytes("61-00-00-00-00-00")));
        }

        [Theory]
        [Trait("Category", "wrc-03")]
        [MemberData(nameof(EventIdFixtures))]
        public void KindFromEventId_MapsKnownIdsAndRejectsUnknownIds(int eventId, string expected)
        {
            Assert.Equal(expected, RegistryPayloadDecoder.KindFromEventId(eventId).ToString());
        }

        private static object[] Fixture(int regType, string bytes, string expected)
        {
            return new object[] { regType, ParseBytes(bytes), expected };
        }

        private static byte[] ParseBytes(string bytes)
        {
            return Convert.FromHexString(bytes.Replace("-", string.Empty));
        }
    }
}
