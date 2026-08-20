/*
 * Copyright (c) 2026, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

[assembly: InternalsVisibleTo("Wintap.Tests")]

namespace gov.llnl.wintap.platform.windows.collect.etw.helpers
{
    public enum SidParseStatus
    {
        /// <summary>A well-formed SID was extracted from the event payload.</summary>
        Extracted,

        /// <summary>The event carried the 4-byte null-SID marker.</summary>
        NoSid,

        /// <summary>The payload was too short or inconsistent with the expected layout.</summary>
        Malformed
    }

    /// <summary>
    /// Extracts the UserSID field from kernel Process events. The field is present in the
    /// MOF payload, but TraceEvent's ProcessTraceData deliberately skips it, so the offset
    /// is re-derived from EventData(), Version, and PointerSize.
    ///
    /// Must be called inside the event callback because TraceEvent recycles event instances.
    /// PointerSize is read per-event so this stays correct when replaying .etl files
    /// captured on a different architecture.
    /// </summary>
    public static class ProcessTraceDataExtensions
    {
        public static SidParseStatus TryGetUserSid(this ProcessTraceData data, out SecurityIdentifier? sid)
            => TryGetUserSid(data, out sid, out _);

        /// <summary>
        /// Extracts UserSID and exposes the offset one byte past the SID field.
        /// </summary>
        public static SidParseStatus TryGetUserSid(this ProcessTraceData data, out SecurityIdentifier? sid, out int postSidOffset)
        {
            byte[] payload = data.EventData();
            int ptrSize = data.PointerSize;
            int version = data.Version;

            return TryGetUserSidFromPayload(payload, ptrSize, version, out sid, out postSidOffset);
        }

        internal static SidParseStatus TryGetUserSidFromPayload(
            byte[] payload,
            int pointerSize,
            int version,
            out SecurityIdentifier? sid,
            out int postSidOffset)
        {
            sid = null;
            postSidOffset = -1;

            int HostOffset(int bytes, int nPtrs) => bytes + (pointerSize - 4) * nPtrs;

            int sidOffset = version >= 4 ? HostOffset(28, 2)
                          : version >= 3 ? HostOffset(24, 2)
                          : HostOffset(20, 1);

            if (payload.Length < sidOffset + 4)
            {
                return SidParseStatus.Malformed;
            }

            if (BitConverter.ToInt32(payload, sidOffset) == 0)
            {
                postSidOffset = sidOffset + 4;
                return SidParseStatus.NoSid;
            }

            int sidStart = sidOffset + HostOffset(8, 2);
            if (payload.Length < sidStart + 8)
            {
                return SidParseStatus.Malformed;
            }

            byte subAuthorityCount = payload[sidStart + 1];
            int sidLength = 8 + 4 * subAuthorityCount;
            if (subAuthorityCount > 15 || payload.Length < sidStart + sidLength)
            {
                return SidParseStatus.Malformed;
            }

            try
            {
                sid = new SecurityIdentifier(payload, sidStart);
            }
            catch (ArgumentException)
            {
                return SidParseStatus.Malformed;
            }

            postSidOffset = sidStart + sidLength;
            return SidParseStatus.Extracted;
        }
    }
}
