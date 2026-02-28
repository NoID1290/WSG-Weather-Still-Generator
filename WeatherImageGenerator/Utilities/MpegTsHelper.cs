using System;

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
    }
}
