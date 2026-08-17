using System.Security.Principal;
using gov.llnl.wintap.platform.windows.collect.etw.helpers;
using Xunit;

namespace Wintap.Tests
{
    public class WindowsProcessSidExtractionTests
    {
        private const string ExpectedSidString = "S-1-5-18";

        [Theory]
        [Trait("Category", "wpc-01")]
        [InlineData(3, 4)]
        [InlineData(3, 8)]
        [InlineData(4, 4)]
        [InlineData(4, 8)]
        public void TryGetUserSidFromPayload_ExtractsSid_ForSupportedLayouts(int version, int pointerSize)
        {
            byte[] payload = CreatePayloadWithSid(version, pointerSize, out int sidStart, out int sidLength);

            SidParseStatus status = ProcessTraceDataExtensions.TryGetUserSidFromPayload(
                payload,
                pointerSize,
                version,
                out SecurityIdentifier sid,
                out int postSidOffset);

            Assert.Equal(SidParseStatus.Extracted, status);
            Assert.NotNull(sid);
            Assert.Equal(ExpectedSidString, sid.ToString());
            Assert.Equal(sidStart + sidLength, postSidOffset);
        }

        [Fact]
        [Trait("Category", "wpc-01")]
        public void TryGetUserSidFromPayload_ReturnsNoSid_WhenMarkerIsZero()
        {
            const int version = 4;
            const int pointerSize = 8;
            int sidOffset = GetSidOffset(version, pointerSize);
            byte[] payload = new byte[sidOffset + 4];

            SidParseStatus status = ProcessTraceDataExtensions.TryGetUserSidFromPayload(
                payload,
                pointerSize,
                version,
                out SecurityIdentifier sid,
                out int postSidOffset);

            Assert.Equal(SidParseStatus.NoSid, status);
            Assert.Null(sid);
            Assert.Equal(sidOffset + 4, postSidOffset);
        }

        [Fact]
        [Trait("Category", "wpc-01")]
        public void TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadShorterThanSidMarker()
        {
            const int version = 3;
            const int pointerSize = 4;
            int sidOffset = GetSidOffset(version, pointerSize);
            byte[] payload = new byte[sidOffset + 3];

            SidParseStatus status = ProcessTraceDataExtensions.TryGetUserSidFromPayload(
                payload,
                pointerSize,
                version,
                out SecurityIdentifier sid,
                out int postSidOffset);

            Assert.Equal(SidParseStatus.Malformed, status);
            Assert.Null(sid);
            Assert.Equal(-1, postSidOffset);
        }

        [Fact]
        [Trait("Category", "wpc-01")]
        public void TryGetUserSidFromPayload_ReturnsMalformed_WhenPayloadTooShortForTokenUserAndSidHeader()
        {
            const int version = 4;
            const int pointerSize = 8;
            int sidOffset = GetSidOffset(version, pointerSize);
            int sidStart = GetSidStart(version, pointerSize);
            byte[] payload = new byte[sidStart + 7];
            payload[sidOffset] = 1;

            SidParseStatus status = ProcessTraceDataExtensions.TryGetUserSidFromPayload(
                payload,
                pointerSize,
                version,
                out SecurityIdentifier sid,
                out int postSidOffset);

            Assert.Equal(SidParseStatus.Malformed, status);
            Assert.Null(sid);
            Assert.Equal(-1, postSidOffset);
        }

        [Fact]
        [Trait("Category", "wpc-01")]
        public void TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsMaximum()
        {
            const int version = 3;
            const int pointerSize = 8;
            int sidOffset = GetSidOffset(version, pointerSize);
            int sidStart = GetSidStart(version, pointerSize);
            byte[] payload = new byte[sidStart + 8];
            payload[sidOffset] = 1;
            payload[sidStart] = 1;
            payload[sidStart + 1] = 16;

            SidParseStatus status = ProcessTraceDataExtensions.TryGetUserSidFromPayload(
                payload,
                pointerSize,
                version,
                out SecurityIdentifier sid,
                out int postSidOffset);

            Assert.Equal(SidParseStatus.Malformed, status);
            Assert.Null(sid);
            Assert.Equal(-1, postSidOffset);
        }

        [Fact]
        [Trait("Category", "wpc-01")]
        public void TryGetUserSidFromPayload_ReturnsMalformed_WhenSubAuthorityCountExceedsPayloadLength()
        {
            const int version = 4;
            const int pointerSize = 4;
            int sidOffset = GetSidOffset(version, pointerSize);
            int sidStart = GetSidStart(version, pointerSize);
            byte[] payload = new byte[sidStart + 12];
            payload[sidOffset] = 1;
            payload[sidStart] = 1;
            payload[sidStart + 1] = 2;

            SidParseStatus status = ProcessTraceDataExtensions.TryGetUserSidFromPayload(
                payload,
                pointerSize,
                version,
                out SecurityIdentifier sid,
                out int postSidOffset);

            Assert.Equal(SidParseStatus.Malformed, status);
            Assert.Null(sid);
            Assert.Equal(-1, postSidOffset);
        }

        private static byte[] CreatePayloadWithSid(int version, int pointerSize, out int sidStart, out int sidLength)
        {
            var expectedSid = new SecurityIdentifier(ExpectedSidString);
            sidLength = expectedSid.BinaryLength;
            sidStart = GetSidStart(version, pointerSize);

            byte[] payload = new byte[sidStart + sidLength];
            payload[GetSidOffset(version, pointerSize)] = 1;
            expectedSid.GetBinaryForm(payload, sidStart);

            return payload;
        }

        private static int GetSidStart(int version, int pointerSize)
            => GetSidOffset(version, pointerSize) + HostOffset(8, 2, pointerSize);

        private static int GetSidOffset(int version, int pointerSize)
            => version >= 4 ? HostOffset(28, 2, pointerSize)
             : version >= 3 ? HostOffset(24, 2, pointerSize)
             : HostOffset(20, 1, pointerSize);

        private static int HostOffset(int bytes, int nPtrs, int pointerSize)
            => bytes + (pointerSize - 4) * nPtrs;
    }
}
