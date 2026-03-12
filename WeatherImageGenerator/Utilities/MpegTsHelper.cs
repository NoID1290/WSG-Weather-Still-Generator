using System;
using System.Collections.Generic;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Low-level MPEG Transport Stream (ISO 13818-1) packet parser and rewriter.
    /// Handles continuity counters, PID extraction, PCR/PTS/DTS reading and offsetting,
    /// keyframe detection, and PAT/PMT identification — all operating on raw 188-byte
    /// TS packets for zero-copy splice operations.
    /// </summary>
    public static class MpegTsHelper
    {
        /// <summary>MPEG-TS sync byte.</summary>
        public const byte SyncByte = 0x47;

        /// <summary>Standard MPEG-TS packet size.</summary>
        public const int PacketSize = 188;

        /// <summary>PID of the PAT (Program Association Table).</summary>
        public const int PidPat = 0x0000;

        /// <summary>PID used for null/stuffing packets.</summary>
        public const int PidNull = 0x1FFF;

        // ────────────────────────────────────────────────────────────────────
        // Packet header field accessors (byte-level, no allocation)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Returns true if the buffer at <paramref name="offset"/> starts with 0x47.</summary>
        public static bool IsValidPacket(byte[] buffer, int offset = 0)
        {
            return offset + PacketSize <= buffer.Length && buffer[offset] == SyncByte;
        }

        /// <summary>Extracts the 13-bit PID from bytes 1–2 of a TS packet.</summary>
        public static int GetPid(byte[] packet, int offset = 0)
        {
            return ((packet[offset + 1] & 0x1F) << 8) | packet[offset + 2];
        }

        /// <summary>Returns true if the Payload Unit Start Indicator bit is set.</summary>
        public static bool HasPayloadUnitStart(byte[] packet, int offset = 0)
        {
            return (packet[offset + 1] & 0x40) != 0;
        }

        /// <summary>Gets the 2-bit adaptation field control from byte 3.</summary>
        public static int GetAdaptationFieldControl(byte[] packet, int offset = 0)
        {
            return (packet[offset + 3] >> 4) & 0x03;
        }

        /// <summary>Returns true when an adaptation field is present (AFC == 2 or 3).</summary>
        public static bool HasAdaptationField(byte[] packet, int offset = 0)
        {
            int afc = GetAdaptationFieldControl(packet, offset);
            return afc == 2 || afc == 3;
        }

        /// <summary>Returns true when a payload is present (AFC == 1 or 3).</summary>
        public static bool HasPayload(byte[] packet, int offset = 0)
        {
            int afc = GetAdaptationFieldControl(packet, offset);
            return afc == 1 || afc == 3;
        }

        /// <summary>Gets the 4-bit continuity counter (0-15) from byte 3.</summary>
        public static int GetContinuityCounter(byte[] packet, int offset = 0)
        {
            return packet[offset + 3] & 0x0F;
        }

        /// <summary>Sets the 4-bit continuity counter (0-15) in byte 3.</summary>
        public static void SetContinuityCounter(byte[] packet, int offset, int cc)
        {
            packet[offset + 3] = (byte)((packet[offset + 3] & 0xF0) | (cc & 0x0F));
        }

        // ────────────────────────────────────────────────────────────────────
        // Adaptation field helpers
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the byte offset where the adaptation field starts (byte after the length byte),
        /// or -1 if no adaptation field is present.
        /// </summary>
        public static int GetAdaptationFieldOffset(byte[] packet, int packetOffset = 0)
        {
            if (!HasAdaptationField(packet, packetOffset))
                return -1;

            int afLength = packet[packetOffset + 4];
            if (afLength < 1)
                return -1;

            return packetOffset + 5; // first byte of adaptation field flags
        }

        /// <summary>Returns the length of the adaptation field (excluding the length byte itself).</summary>
        public static int GetAdaptationFieldLength(byte[] packet, int packetOffset = 0)
        {
            if (!HasAdaptationField(packet, packetOffset))
                return 0;
            return packet[packetOffset + 4];
        }

        /// <summary>Returns the byte offset where the payload starts, or -1 if none.</summary>
        public static int GetPayloadOffset(byte[] packet, int packetOffset = 0)
        {
            if (!HasPayload(packet, packetOffset))
                return -1;

            if (HasAdaptationField(packet, packetOffset))
            {
                int afLen = packet[packetOffset + 4];
                return packetOffset + 5 + afLen;
            }
            return packetOffset + 4;
        }

        // ────────────────────────────────────────────────────────────────────
        // PCR (Program Clock Reference)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Returns true if the adaptation field contains a PCR.</summary>
        public static bool HasPcr(byte[] packet, int packetOffset = 0)
        {
            int afOffset = GetAdaptationFieldOffset(packet, packetOffset);
            if (afOffset < 0) return false;
            return (packet[afOffset] & 0x10) != 0; // PCR flag
        }

        /// <summary>
        /// Reads the 42-bit PCR base (in 90 kHz units) from the adaptation field.
        /// Returns -1 if not present.
        /// </summary>
        public static long GetPcrBase(byte[] packet, int packetOffset = 0)
        {
            int afOffset = GetAdaptationFieldOffset(packet, packetOffset);
            if (afOffset < 0) return -1;
            if ((packet[afOffset] & 0x10) == 0) return -1;

            int pcrOffset = afOffset + 1; // PCR bytes start after the flags byte
            if (pcrOffset + 6 > packetOffset + PacketSize) return -1;

            long pcrBase = ((long)packet[pcrOffset] << 25)
                         | ((long)packet[pcrOffset + 1] << 17)
                         | ((long)packet[pcrOffset + 2] << 9)
                         | ((long)packet[pcrOffset + 3] << 1)
                         | ((long)(packet[pcrOffset + 4] >> 7) & 0x01);
            return pcrBase;
        }

        /// <summary>
        /// Writes a 42-bit PCR base value into the adaptation field.
        /// Does nothing if no PCR is present.
        /// </summary>
        public static void SetPcrBase(byte[] packet, int packetOffset, long pcrBase)
        {
            int afOffset = GetAdaptationFieldOffset(packet, packetOffset);
            if (afOffset < 0) return;
            if ((packet[afOffset] & 0x10) == 0) return;

            int pcrOffset = afOffset + 1;
            if (pcrOffset + 6 > packetOffset + PacketSize) return;

            packet[pcrOffset]     = (byte)((pcrBase >> 25) & 0xFF);
            packet[pcrOffset + 1] = (byte)((pcrBase >> 17) & 0xFF);
            packet[pcrOffset + 2] = (byte)((pcrBase >> 9) & 0xFF);
            packet[pcrOffset + 3] = (byte)((pcrBase >> 1) & 0xFF);
            // preserve extension bits in byte 4 (bits 0-7), only modify bit 7
            packet[pcrOffset + 4] = (byte)((packet[pcrOffset + 4] & 0x7F) | (byte)((pcrBase & 0x01) << 7));
        }

        // ────────────────────────────────────────────────────────────────────
        // PES header PTS / DTS
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to read PTS from a PES header that begins at the payload start of a
        /// packet with PUSI set. Returns the 33-bit PTS in 90 kHz ticks, or -1.
        /// </summary>
        public static long GetPts(byte[] packet, int packetOffset = 0)
        {
            int payOff = GetPayloadOffset(packet, packetOffset);
            if (payOff < 0) return -1;
            if (!HasPayloadUnitStart(packet, packetOffset)) return -1;

            // PES start code: 00 00 01
            if (payOff + 14 > packetOffset + PacketSize) return -1;
            if (packet[payOff] != 0x00 || packet[payOff + 1] != 0x00 || packet[payOff + 2] != 0x01)
                return -1;

            // byte 7: PTS_DTS_flags (bits 7-6)
            int ptsDtsFlags = (packet[payOff + 7] >> 6) & 0x03;
            if (ptsDtsFlags < 2) return -1; // no PTS

            int ptsOff = payOff + 9;
            if (ptsOff + 5 > packetOffset + PacketSize) return -1;

            return ReadTimestamp(packet, ptsOff);
        }

        /// <summary>
        /// Attempts to read DTS from a PES header. Returns -1 if not present.
        /// </summary>
        public static long GetDts(byte[] packet, int packetOffset = 0)
        {
            int payOff = GetPayloadOffset(packet, packetOffset);
            if (payOff < 0) return -1;
            if (!HasPayloadUnitStart(packet, packetOffset)) return -1;

            if (payOff + 19 > packetOffset + PacketSize) return -1;
            if (packet[payOff] != 0x00 || packet[payOff + 1] != 0x00 || packet[payOff + 2] != 0x01)
                return -1;

            int ptsDtsFlags = (packet[payOff + 7] >> 6) & 0x03;
            if (ptsDtsFlags != 3) return -1; // DTS only present when flags == 3

            int dtsOff = payOff + 14;
            if (dtsOff + 5 > packetOffset + PacketSize) return -1;

            return ReadTimestamp(packet, dtsOff);
        }

        /// <summary>
        /// Offsets PTS (and DTS if present) in a PES header by the given tick amount.
        /// Does nothing if the packet does not contain a PES header with timestamps.
        /// </summary>
        public static void OffsetTimestamps(byte[] packet, int packetOffset, long tickOffset)
        {
            int payOff = GetPayloadOffset(packet, packetOffset);
            if (payOff < 0) return;
            if (!HasPayloadUnitStart(packet, packetOffset)) return;

            if (payOff + 9 > packetOffset + PacketSize) return;
            if (packet[payOff] != 0x00 || packet[payOff + 1] != 0x00 || packet[payOff + 2] != 0x01)
                return;

            int ptsDtsFlags = (packet[payOff + 7] >> 6) & 0x03;

            if (ptsDtsFlags >= 2)
            {
                int ptsOff = payOff + 9;
                if (ptsOff + 5 <= packetOffset + PacketSize)
                {
                    long pts = ReadTimestamp(packet, ptsOff);
                    WriteTimestamp(packet, ptsOff, pts + tickOffset);
                }
            }

            if (ptsDtsFlags == 3)
            {
                int dtsOff = payOff + 14;
                if (dtsOff + 5 <= packetOffset + PacketSize)
                {
                    long dts = ReadTimestamp(packet, dtsOff);
                    WriteTimestamp(packet, dtsOff, dts + tickOffset);
                }
            }
        }

        /// <summary>Reads a 33-bit MPEG timestamp from 5 bytes at the given offset.</summary>
        private static long ReadTimestamp(byte[] buf, int off)
        {
            long ts = ((long)(buf[off] >> 1) & 0x07) << 30;
            ts |= (long)buf[off + 1] << 22;
            ts |= (long)(buf[off + 2] >> 1) << 15;
            ts |= (long)buf[off + 3] << 7;
            ts |= (long)(buf[off + 4] >> 1);
            return ts;
        }

        /// <summary>Writes a 33-bit timestamp into the 5-byte field, preserving marker bits.</summary>
        private static void WriteTimestamp(byte[] buf, int off, long ts)
        {
            // Preserve the top nibble (indicator bits) of byte 0
            int indicator = buf[off] & 0xF0;
            buf[off]     = (byte)(indicator | (int)(((ts >> 30) & 0x07) << 1) | 0x01);
            buf[off + 1] = (byte)((ts >> 22) & 0xFF);
            buf[off + 2] = (byte)((((ts >> 15) & 0x7F) << 1) | 0x01);
            buf[off + 3] = (byte)((ts >> 7) & 0xFF);
            buf[off + 4] = (byte)((((ts) & 0x7F) << 1) | 0x01);
        }

        // ────────────────────────────────────────────────────────────────────
        // Keyframe (RAP) detection
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the adaptation field's Random Access Indicator is set,
        /// signalling an IDR / keyframe boundary.
        /// </summary>
        public static bool IsRandomAccessPoint(byte[] packet, int packetOffset = 0)
        {
            int afOffset = GetAdaptationFieldOffset(packet, packetOffset);
            if (afOffset < 0) return false;
            return (packet[afOffset] & 0x40) != 0; // random_access_indicator
        }

        // ────────────────────────────────────────────────────────────────────
        // Table identification
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Returns true if the packet carries PAT data (PID 0x0000).</summary>
        public static bool IsPat(byte[] packet, int packetOffset = 0)
        {
            return GetPid(packet, packetOffset) == PidPat;
        }

        /// <summary>Returns true if the packet is a null/stuffing packet (PID 0x1FFF).</summary>
        public static bool IsNullPacket(byte[] packet, int packetOffset = 0)
        {
            return GetPid(packet, packetOffset) == PidNull;
        }

        // ────────────────────────────────────────────────────────────────────
        // Packet alignment
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the byte offset of the first sync byte (0x47) that is also 188-byte
        /// aligned (i.e. another 0x47 follows 188 bytes later, or the buffer ends).
        /// Returns -1 if no valid alignment is found.
        /// </summary>
        public static int FindSyncOffset(byte[] buffer, int start, int length)
        {
            int end = start + length - PacketSize;
            for (int i = start; i <= end; i++)
            {
                if (buffer[i] != SyncByte) continue;

                // Verify alignment: next packet should also start with 0x47
                int next = i + PacketSize;
                if (next >= start + length || buffer[next] == SyncByte)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Generates a 188-byte null stuffing packet (PID 0x1FFF).
        /// Useful for keeping the stream alive during reconnection gaps.
        /// </summary>
        public static byte[] CreateNullPacket()
        {
            var pkt = new byte[PacketSize];
            pkt[0] = SyncByte;
            pkt[1] = 0x1F;
            pkt[2] = 0xFF;
            pkt[3] = 0x10; // AFC = 01 (payload only), CC = 0
            // rest is 0x00 (valid stuffing)
            return pkt;
        }

        // ────────────────────────────────────────────────────────────────────
        // Discontinuity indicator
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the discontinuity_indicator bit in the adaptation field.
        /// If the packet has no adaptation field, modifies the AFC to include one
        /// (stealing one byte from the payload to create a minimal adaptation field).
        /// </summary>
        public static void SetDiscontinuityIndicator(byte[] packet, int offset = 0)
        {
            int afc = GetAdaptationFieldControl(packet, offset);

            if (afc == 2 || afc == 3)
            {
                // Adaptation field already present — just set the discontinuity bit
                int afLen = packet[offset + 4];
                if (afLen >= 1)
                {
                    packet[offset + 5] |= 0x80; // discontinuity_indicator is bit 7
                }
            }
            else if (afc == 1)
            {
                // Payload only — change AFC to 3 (adaptation + payload) and inject
                // a minimal 1-byte adaptation field with discontinuity set.
                packet[offset + 3] = (byte)((packet[offset + 3] & 0x0F) | 0x30); // AFC = 11
                // Shift payload right by 2 bytes to make room: af_length(1) + af_flags(1)
                // For simplicity, we just set the flags in the space where payload was.
                // This corrupts 2 payload bytes at the start, which is acceptable for a
                // splice-point transition packet.
                packet[offset + 4] = 0x01; // adaptation field length = 1
                packet[offset + 5] = 0x80; // discontinuity_indicator = 1
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // PAT / PMT parsing for PID discovery
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses a PAT packet and returns the PMT PID(s).
        /// Returns an empty list if the packet is not a valid PAT.
        /// </summary>
        public static List<int> ParsePatForPmtPids(byte[] packet, int packetOffset = 0)
        {
            var pmtPids = new List<int>();
            if (GetPid(packet, packetOffset) != PidPat) return pmtPids;
            if (!HasPayloadUnitStart(packet, packetOffset)) return pmtPids;

            int payOff = GetPayloadOffset(packet, packetOffset);
            if (payOff < 0) return pmtPids;

            // Skip pointer field
            int pointer = packet[payOff];
            int tableStart = payOff + 1 + pointer;
            if (tableStart + 8 > packetOffset + PacketSize) return pmtPids;

            // table_id should be 0x00 for PAT
            if (packet[tableStart] != 0x00) return pmtPids;

            int sectionLength = ((packet[tableStart + 1] & 0x0F) << 8) | packet[tableStart + 2];
            int dataStart = tableStart + 8; // skip header (table_id, section_length, transport_stream_id, version, section_number, last_section)
            int dataEnd = Math.Min(tableStart + 3 + sectionLength - 4, packetOffset + PacketSize); // -4 for CRC

            for (int i = dataStart; i + 3 < dataEnd; i += 4)
            {
                int programNum = (packet[i] << 8) | packet[i + 1];
                int pid = ((packet[i + 2] & 0x1F) << 8) | packet[i + 3];
                if (programNum != 0) // skip NIT (program 0)
                    pmtPids.Add(pid);
            }

            return pmtPids;
        }

        /// <summary>
        /// Parses a PMT packet and returns the elementary stream PIDs with their types.
        /// Returns (pcrPid, list of (streamType, elementaryPid)).
        /// </summary>
        public static (int PcrPid, List<(int StreamType, int Pid)> Streams) ParsePmt(byte[] packet, int packetOffset = 0)
        {
            var streams = new List<(int StreamType, int Pid)>();
            if (!HasPayloadUnitStart(packet, packetOffset))
                return (-1, streams);

            int payOff = GetPayloadOffset(packet, packetOffset);
            if (payOff < 0) return (-1, streams);

            // Skip pointer field
            int pointer = packet[payOff];
            int tableStart = payOff + 1 + pointer;
            if (tableStart + 12 > packetOffset + PacketSize) return (-1, streams);

            // table_id should be 0x02 for PMT
            if (packet[tableStart] != 0x02) return (-1, streams);

            int sectionLength = ((packet[tableStart + 1] & 0x0F) << 8) | packet[tableStart + 2];
            int pcrPid = ((packet[tableStart + 8] & 0x1F) << 8) | packet[tableStart + 9];
            int programInfoLength = ((packet[tableStart + 10] & 0x0F) << 8) | packet[tableStart + 11];

            int esStart = tableStart + 12 + programInfoLength;
            int esEnd = Math.Min(tableStart + 3 + sectionLength - 4, packetOffset + PacketSize);

            int pos = esStart;
            while (pos + 5 <= esEnd)
            {
                int streamType = packet[pos];
                int esPid = ((packet[pos + 1] & 0x1F) << 8) | packet[pos + 2];
                int esInfoLength = ((packet[pos + 3] & 0x0F) << 8) | packet[pos + 4];
                streams.Add((streamType, esPid));
                pos += 5 + esInfoLength;
            }

            return (pcrPid, streams);
        }

        /// <summary>
        /// Rewrites the 13-bit PID in bytes 1-2 of a TS packet header.
        /// </summary>
        public static void SetPid(byte[] packet, int offset, int newPid)
        {
            packet[offset + 1] = (byte)((packet[offset + 1] & 0xE0) | ((newPid >> 8) & 0x1F));
            packet[offset + 2] = (byte)(newPid & 0xFF);
        }

        /// <summary>
        /// Determines if a stream type is video (H.264, H.265, MPEG-2, etc.).
        /// </summary>
        public static bool IsVideoStreamType(int streamType)
        {
            // 0x01/0x02 = MPEG-1/2, 0x1B = H.264, 0x24 = H.265, 0x10 = MPEG-4
            return streamType == 0x01 || streamType == 0x02 || streamType == 0x1B ||
                   streamType == 0x24 || streamType == 0x10;
        }

        /// <summary>
        /// Determines if a stream type is audio (AAC, MP3, AC-3, etc.).
        /// </summary>
        public static bool IsAudioStreamType(int streamType)
        {
            // 0x03/0x04 = MPEG audio, 0x0F = AAC, 0x11 = AAC-LATM, 0x81 = AC-3, 0x06 = PES private (often audio)
            return streamType == 0x03 || streamType == 0x04 || streamType == 0x0F ||
                   streamType == 0x11 || streamType == 0x81 || streamType == 0x06;
        }

        // ────────────────────────────────────────────────────────────────────
        // H.264 keyframe (IDR) detection
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Checks if a TS packet on the specified video PID contains an H.264 IDR
        /// (Instantaneous Decoder Refresh) NAL unit, indicating a keyframe.
        /// Only inspects the first few payload bytes for the NAL start code + type.
        /// </summary>
        public static bool IsH264Keyframe(byte[] packet, int offset, int videoPid)
        {
            int pid = GetPid(packet, offset);
            if (pid != videoPid) return false;
            if (!HasPayloadUnitStart(packet, offset)) return false;

            int payOff = GetPayloadOffset(packet, offset);
            if (payOff < 0) return false;

            // PES header: 00 00 01 [stream_id] [length] [flags] [PTS/DTS...] [payload]
            // Skip PES header to reach the H.264 Access Unit
            if (payOff + 9 > offset + PacketSize) return false;
            if (packet[payOff] != 0x00 || packet[payOff + 1] != 0x00 || packet[payOff + 2] != 0x01)
                return false;

            // PES header data length is at byte 8
            int pesHeaderDataLen = packet[payOff + 8];
            int auStart = payOff + 9 + pesHeaderDataLen;

            // Scan for NAL start codes (00 00 01 or 00 00 00 01) followed by IDR NAL type
            int limit = Math.Min(auStart + 32, offset + PacketSize - 1);
            for (int i = auStart; i < limit; i++)
            {
                // Check for 3-byte start code: 00 00 01
                if (i + 3 < offset + PacketSize &&
                    packet[i] == 0x00 && packet[i + 1] == 0x00 && packet[i + 2] == 0x01)
                {
                    int nalType = packet[i + 3] & 0x1F;
                    // NAL type 5 = IDR slice (keyframe)
                    // NAL type 7 = SPS (often precedes IDR in an Access Unit)
                    if (nalType == 5) return true;
                    // If we see an SPS (7) or PPS (8), the IDR might follow — keep scanning
                    if (nalType == 7 || nalType == 8) continue;
                    // NAL type 1 = non-IDR (not a keyframe)
                    if (nalType == 1) return false;
                }
                // Check for 4-byte start code: 00 00 00 01
                if (i + 4 < offset + PacketSize &&
                    packet[i] == 0x00 && packet[i + 1] == 0x00 && packet[i + 2] == 0x00 && packet[i + 3] == 0x01)
                {
                    int nalType = packet[i + 4] & 0x1F;
                    if (nalType == 5) return true;
                    if (nalType == 7 || nalType == 8) continue;
                    if (nalType == 1) return false;
                }
            }

            return false;
        }
    }
}
