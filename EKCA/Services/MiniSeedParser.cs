#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EKCA.Models;

namespace EKCA.Services
{
    /// <summary>
    /// Parses raw MiniSEED binary data (SEED format version 2.4) into
    /// <see cref="SeismogramData"/> objects.
    ///
    /// Supports the encodings used by the Canadian National Seismograph Network (CNSN):
    ///  1 — 16-bit integer (INT16)
    ///  3 — 32-bit integer (INT32)
    ///  4 — IEEE 32-bit float
    ///  5 — IEEE 64-bit float (double)
    ///  10 — Steim-1 (first-difference compression)
    ///  11 — Steim-2 (second-difference compression)
    ///
    /// All multi-byte fields in SEED format are big-endian.
    /// </summary>
    internal static class MiniSeedParser
    {
        // ---------------------------------------------------------------------------
        // Constants
        // ---------------------------------------------------------------------------

        private const int FixedHeaderSize = 48;  // Fixed section of Data Header
        private const int BlocketteHeaderSize = 4; // type(2) + nextOffset(2)
        private const int Blockette1000Size = 4;   // encodingFormat(1) + wordOrder(1) + dataRecordLen(1) + reserved(1)

        // ---------------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Parses a raw MiniSEED byte array that may contain multiple data records
        /// and returns them as an ordered sequence of <see cref="MiniSeedRecord"/>.
        /// </summary>
        public static IEnumerable<MiniSeedRecord> ParseRecords(byte[] data)
        {
            int offset = 0;
            while (offset < data.Length - FixedHeaderSize)
            {
                var record = TryParseRecord(data, offset);
                if (record == null) break;
                yield return record;
                offset += record.RecordLengthBytes;
            }
        }

        /// <summary>
        /// Assembles a collection of <see cref="MiniSeedRecord"/> objects (all from the
        /// same station / channel) into a single contiguous <see cref="SeismogramData"/>.
        /// Records that do not belong to the requested station/channel are ignored.
        /// </summary>
        public static SeismogramData ToSeismogramData(
            IEnumerable<MiniSeedRecord> records,
            string stationCode,
            string channel)
        {
            var sorted = new List<MiniSeedRecord>();
            foreach (var r in records)
            {
                if (r.Station.Equals(stationCode, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(channel) ||
                     r.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)))
                {
                    sorted.Add(r);
                }
            }

            sorted.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            if (sorted.Count == 0)
            {
                return new SeismogramData
                {
                    StationCode = stationCode,
                    Channel = channel,
                    Samples = Array.Empty<float>()
                };
            }

            // Concatenate samples from all records
            int totalSamples = 0;
            foreach (var r in sorted) totalSamples += r.Samples.Length;

            var allSamples = new float[totalSamples];
            int dest = 0;
            foreach (var r in sorted)
            {
                Array.Copy(r.Samples, 0, allSamples, dest, r.Samples.Length);
                dest += r.Samples.Length;
            }

            return new SeismogramData
            {
                StationCode = sorted[0].Station,
                Network = sorted[0].Network,
                Channel = sorted[0].Channel,
                Location = sorted[0].Location,
                StartTime = sorted[0].StartTime,
                SampleRateHz = sorted[0].SampleRateHz,
                Samples = allSamples
            };
        }

        // ---------------------------------------------------------------------------
        // Record parsing
        // ---------------------------------------------------------------------------

        private static MiniSeedRecord? TryParseRecord(byte[] data, int offset)
        {
            if (offset + FixedHeaderSize > data.Length) return null;

            // -----------------------------------------------------------------------
            // Fixed section of Data Header (48 bytes)
            // -----------------------------------------------------------------------
            // 0–5   Sequence number (ASCII)
            // 6     Data header/quality indicator
            // 7     Reserved
            // 8–12  Station identifier (right-padded with spaces)
            // 13–14 Location identifier
            // 15–17 Channel identifier
            // 18–19 Network code
            // 20–29 Start time (BTIME: 10 bytes)
            // 30–31 Number of samples (int16 BE)
            // 32–33 Sample rate factor (int16 BE)
            // 34–35 Sample rate multiplier (int16 BE)
            // 36    Activity flags
            // 37    I/O and clock flags
            // 38    Data quality flags
            // 39    Number of blockettes that follow (uint8)
            // 40–43 Time correction (int32 BE) — 0.0001 s units
            // 44–45 Beginning of data (uint16 BE) — byte offset within record
            // 46–47 First blockette (uint16 BE) — byte offset within record

            string station = ReadAscii(data, offset + 8, 5).Trim();
            string location = ReadAscii(data, offset + 13, 2).Trim();
            string channel = ReadAscii(data, offset + 15, 3).Trim();
            string network = ReadAscii(data, offset + 18, 2).Trim();

            // BTIME: Year(2 BE), DayOfYear(2 BE), Hour(1), Min(1), Sec(1), Reserved(1), 0.1ms(2 BE)
            int year = ReadInt16BE(data, offset + 20);
            int day = ReadInt16BE(data, offset + 22);
            int hour = data[offset + 24];
            int min = data[offset + 25];
            int sec = data[offset + 26];
            int tenthsMs = ReadUInt16BE(data, offset + 28); // units of 100 microseconds

            DateTime startTime;
            try
            {
                startTime = new DateTime(year, 1, 1, hour, min, sec, DateTimeKind.Utc)
                    .AddDays(day - 1)
                    .AddMilliseconds(tenthsMs * 0.1);
            }
            catch
            {
                return null; // corrupt header
            }

            int nSamples = ReadInt16BE(data, offset + 30);
            if (nSamples < 0) nSamples = 0;

            short rateFactor = (short)ReadInt16BE(data, offset + 32);
            short rateMultiplier = (short)ReadInt16BE(data, offset + 34);
            double sampleRate = DecodeSampleRate(rateFactor, rateMultiplier);

            int nBlockettes = data[offset + 39];
            int dataByteOffset = ReadUInt16BE(data, offset + 44);
            int firstBlocketteOffset = ReadUInt16BE(data, offset + 46);

            // -----------------------------------------------------------------------
            // Walk blockettes to find Blockette 1000 (Data Only SEED Blockette)
            // -----------------------------------------------------------------------
            byte encodingFormat = 11; // default Steim-2
            byte wordOrder = 1;       // default big-endian
            int recordLengthExp = 9;  // default 512 bytes

            int blkOffset = offset + firstBlocketteOffset;
            for (int b = 0; b < nBlockettes && blkOffset > offset && blkOffset < offset + 256; b++)
            {
                if (blkOffset + 4 > data.Length) break;
                int blkType = ReadUInt16BE(data, blkOffset);
                int nextBlk = ReadUInt16BE(data, blkOffset + 2);

                if (blkType == 1000 && blkOffset + 8 <= data.Length)
                {
                    encodingFormat = data[blkOffset + 4];
                    wordOrder = data[blkOffset + 5];
                    recordLengthExp = data[blkOffset + 6];
                    break;
                }

                if (nextBlk == 0 || nextBlk <= (blkOffset - offset)) break;
                blkOffset = offset + nextBlk;
            }

            int recordLength = recordLengthExp > 0 ? (1 << recordLengthExp) : 512;
            if (recordLength > 65536) recordLength = 65536; // sanity cap

            // -----------------------------------------------------------------------
            // Decode samples
            // -----------------------------------------------------------------------
            float[] samples = Array.Empty<float>();
            if (nSamples > 0 && dataByteOffset > 0)
            {
                int dataStart = offset + dataByteOffset;
                int dataEnd = offset + recordLength;
                if (dataEnd > data.Length) dataEnd = data.Length;
                int dataLen = dataEnd - dataStart;

                if (dataStart < data.Length && dataLen > 0)
                {
                    samples = DecodeData(data, dataStart, dataLen, nSamples, encodingFormat);
                }
            }

            return new MiniSeedRecord
            {
                Network = network,
                Station = station,
                Location = location,
                Channel = channel,
                StartTime = startTime,
                SampleRateHz = sampleRate,
                SampleCount = nSamples,
                Samples = samples,
                RecordLengthBytes = recordLength,
                EncodingFormat = encodingFormat
            };
        }

        // ---------------------------------------------------------------------------
        // Sample decoders
        // ---------------------------------------------------------------------------

        private static float[] DecodeData(byte[] data, int offset, int length, int nSamples, byte encoding)
        {
            return encoding switch
            {
                1 => DecodeInt16(data, offset, length, nSamples),
                3 => DecodeInt32(data, offset, length, nSamples),
                4 => DecodeFloat32(data, offset, length, nSamples),
                5 => DecodeFloat64(data, offset, length, nSamples),
                10 => DecodeSteim1(data, offset, length, nSamples),
                11 => DecodeSteim2(data, offset, length, nSamples),
                _ => Array.Empty<float>()
            };
        }

        private static float[] DecodeInt16(byte[] data, int offset, int length, int nSamples)
        {
            int available = length / 2;
            int count = Math.Min(nSamples, available);
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * 2;
                result[i] = (short)(((data[pos] << 8) | data[pos + 1]));
            }
            return result;
        }

        private static float[] DecodeInt32(byte[] data, int offset, int length, int nSamples)
        {
            int available = length / 4;
            int count = Math.Min(nSamples, available);
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * 4;
                result[i] = ReadInt32BE(data, pos);
            }
            return result;
        }

        private static float[] DecodeFloat32(byte[] data, int offset, int length, int nSamples)
        {
            int available = length / 4;
            int count = Math.Min(nSamples, available);
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * 4;
                // Reverse bytes for big-endian → little-endian
                byte[] le = new[] { data[pos + 3], data[pos + 2], data[pos + 1], data[pos] };
                result[i] = BitConverter.ToSingle(le, 0);
            }
            return result;
        }

        private static float[] DecodeFloat64(byte[] data, int offset, int length, int nSamples)
        {
            int available = length / 8;
            int count = Math.Min(nSamples, available);
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * 8;
                byte[] le = new[]
                {
                    data[pos + 7], data[pos + 6], data[pos + 5], data[pos + 4],
                    data[pos + 3], data[pos + 2], data[pos + 1], data[pos]
                };
                result[i] = (float)BitConverter.ToDouble(le, 0);
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // Steim-1 decoder
        // Each 64-byte frame consists of 16 32-bit big-endian words.
        // Word 0 of frame 0 is the frame-set header (bitmask of decompression codes).
        // Word 1 = X0 (forward integration constant), Word 2 = Xn (reverse integration constant).
        // Remaining words encode differences using 2-bit control codes per word (in frame header words).
        // -----------------------------------------------------------------------
        private static float[] DecodeSteim1(byte[] data, int offset, int length, int nSamples)
        {
            var result = new List<float>(nSamples);
            int frameCount = length / 64;
            int x = 0; // current sample value (running integral)
            bool firstFrame = true;

            for (int f = 0; f < frameCount && result.Count < nSamples; f++)
            {
                int frameBase = offset + f * 64;
                if (frameBase + 64 > data.Length) break;

                // First word of each frame is the block header (Cn bits, 2 bits per word = 16 words * 2 bits = 32 bits)
                uint blockHeader = ReadUInt32BE(data, frameBase);

                for (int w = 0; w < 16 && result.Count < nSamples; w++)
                {
                    int wOffset = frameBase + w * 4;
                    uint dnCode = (blockHeader >> (30 - w * 2)) & 0x3;
                    int wordVal = ReadInt32BE(data, wOffset);

                    if (f == 0 && w == 0) continue; // skip frame header

                    if (f == 0 && w == 1)
                    {
                        // X0: forward integration constant
                        x = wordVal;
                        if (firstFrame) { result.Add(x); firstFrame = false; }
                        continue;
                    }
                    if (f == 0 && w == 2) continue; // Xn: not used for forward decode

                    switch (dnCode)
                    {
                        case 0: // special / no data
                            break;
                        case 1: // 4 × 8-bit differences
                            for (int b = 3; b >= 0 && result.Count < nSamples; b--)
                            {
                                int d = (sbyte)((wordVal >> (b * 8)) & 0xFF);
                                x += d;
                                result.Add(x);
                            }
                            break;
                        case 2: // 2 × 16-bit differences
                            for (int h = 1; h >= 0 && result.Count < nSamples; h--)
                            {
                                int d = (short)((wordVal >> (h * 16)) & 0xFFFF);
                                x += d;
                                result.Add(x);
                            }
                            break;
                        case 3: // 1 × 32-bit difference
                            x += wordVal;
                            result.Add(x);
                            break;
                    }
                }
            }

            return result.ToArray();
        }

        // -----------------------------------------------------------------------
        // Steim-2 decoder
        //
        // Each 64-byte frame is 16 × 32-bit BE words.
        // Word 0 of frame 0 = Cn bitmask (2 bits per word of this frame only).
        // Cn bits encode the packing type:
        //   0x0 — special (skip)
        //   0x1 — 4 × 8-bit diffs (same as Steim-1)
        //   0x2 — Steim-2 compressed block 2
        //   0x3 — Steim-2 compressed block 3
        //
        // For Cn = 0x2 or 0x3, the actual number and width of differences is encoded
        // in the top bits (dnib) of the 30-bit data field:
        //   dnib    width   count
        //    01      30       1
        //    10      15       2
        //    11      10       3
        //   000      6        5
        //   001      5        6
        //   010      4        7
        //   011    2,3      11,15 (split packing)
        // -----------------------------------------------------------------------
        private static float[] DecodeSteim2(byte[] data, int offset, int length, int nSamples)
        {
            var result = new List<float>(nSamples);
            int frameCount = length / 64;
            int x = 0;

            for (int f = 0; f < frameCount && result.Count < nSamples; f++)
            {
                int frameBase = offset + f * 64;
                if (frameBase + 64 > data.Length) break;

                uint blockHeader = ReadUInt32BE(data, frameBase);

                for (int w = 0; w < 16 && result.Count < nSamples; w++)
                {
                    int wOffset = frameBase + w * 4;
                    uint cn = (blockHeader >> (30 - w * 2)) & 0x3;
                    uint word = ReadUInt32BE(data, wOffset);

                    if (f == 0 && w == 0) continue; // block header

                    if (f == 0 && w == 1)
                    {
                        x = (int)word; // X0
                        result.Add(x);
                        continue;
                    }
                    if (f == 0 && w == 2) continue; // Xn

                    switch (cn)
                    {
                        case 0: // no data
                            break;

                        case 1: // 4 × 8-bit differences
                            for (int b = 3; b >= 0 && result.Count < nSamples; b--)
                            {
                                int d = (sbyte)((word >> (b * 8)) & 0xFF);
                                x += d;
                                result.Add(x);
                            }
                            break;

                        case 2:
                        case 3:
                            // Extract dnib from top bits
                            uint dnib;
                            if (cn == 2)
                                dnib = (word >> 30) & 0x3; // 2 bits
                            else
                                dnib = (word >> 30) & 0x3;

                            // Pack type determined by cn and dnib together
                            DecodeSteim2Word(word, cn, result, ref x, nSamples);
                            break;
                    }
                }
            }

            return result.ToArray();
        }

        private static void DecodeSteim2Word(uint word, uint cn, List<float> result, ref int x, int nSamples)
        {
            // cn=1 handled before this call
            // For cn=2 or cn=3: top 2 bits = dnib, remaining 30 bits = data
            uint dnib = (word >> 30) & 0x3;
            int data30 = (int)(word & 0x3FFFFFFF);

            if (cn == 2)
            {
                switch (dnib)
                {
                    case 1: // 1 × 30-bit difference
                    {
                        int d = SignExtend(data30, 30);
                        x += d;
                        if (result.Count < nSamples) result.Add(x);
                        break;
                    }
                    case 2: // 2 × 15-bit differences
                    {
                        int d1 = SignExtend((data30 >> 15) & 0x7FFF, 15);
                        int d2 = SignExtend(data30 & 0x7FFF, 15);
                        x += d1; if (result.Count < nSamples) result.Add(x);
                        x += d2; if (result.Count < nSamples) result.Add(x);
                        break;
                    }
                    case 3: // 3 × 10-bit differences
                    {
                        int d1 = SignExtend((data30 >> 20) & 0x3FF, 10);
                        int d2 = SignExtend((data30 >> 10) & 0x3FF, 10);
                        int d3 = SignExtend(data30 & 0x3FF, 10);
                        x += d1; if (result.Count < nSamples) result.Add(x);
                        x += d2; if (result.Count < nSamples) result.Add(x);
                        x += d3; if (result.Count < nSamples) result.Add(x);
                        break;
                    }
                }
            }
            else // cn==3
            {
                // Top 3 bits of the 30-bit data field are the real dnib (not the 2-bit word dnib)
                uint realDnib = (uint)(data30 >> 27) & 0x7;
                int data27 = data30 & 0x7FFFFFF;

                switch (realDnib)
                {
                    case 0: // 5 × 6-bit differences
                    {
                        for (int i = 4; i >= 0 && result.Count < nSamples; i--)
                        {
                            int d = SignExtend((data27 >> (i * 6)) & 0x3F, 6);
                            x += d;
                            result.Add(x);
                        }
                        break;
                    }
                    case 1: // 6 × 5-bit differences (use bits 0–29 of the 30-bit field)
                    {
                        int data6x5 = data30 & 0x3FFFFFFF;
                        for (int i = 5; i >= 0 && result.Count < nSamples; i--)
                        {
                            int d = SignExtend((data6x5 >> (i * 5)) & 0x1F, 5);
                            x += d;
                            result.Add(x);
                        }
                        break;
                    }
                    case 2: // 7 × 4-bit differences
                    {
                        // 7 × 4 = 28 bits, use bottom 28 bits
                        int data7x4 = data30 & 0xFFFFFFF;
                        for (int i = 6; i >= 0 && result.Count < nSamples; i--)
                        {
                            int d = SignExtend((data7x4 >> (i * 4)) & 0xF, 4);
                            x += d;
                            result.Add(x);
                        }
                        break;
                    }
                    case 3: // mixed 2-bit and 3-bit differences
                    {
                        // Alternating: 11 × 2-bit + 3 × 3-bit layout — approximate as 10 × 3-bit
                        for (int i = 9; i >= 0 && result.Count < nSamples; i--)
                        {
                            int d = SignExtend((data27 >> (i * 3)) & 0x7, 3);
                            x += d;
                            result.Add(x);
                        }
                        break;
                    }
                    default:
                        break;
                }
            }
        }

        // ---------------------------------------------------------------------------
        // Bit-level helpers
        // ---------------------------------------------------------------------------

        private static int SignExtend(int value, int bits)
        {
            int shift = 32 - bits;
            return (value << shift) >> shift;
        }

        private static double DecodeSampleRate(short factor, short multiplier)
        {
            if (factor == 0 && multiplier == 0) return 0;
            double rate;
            if (factor > 0 && multiplier > 0)
                rate = (double)factor * multiplier;
            else if (factor > 0 && multiplier < 0)
                rate = -1.0 * factor / multiplier;
            else if (factor < 0 && multiplier > 0)
                rate = -1.0 * multiplier / factor;
            else // both negative
                rate = 1.0 / ((double)factor * multiplier);
            return Math.Abs(rate);
        }

        private static string ReadAscii(byte[] data, int offset, int length)
        {
            var chars = new char[length];
            for (int i = 0; i < length && offset + i < data.Length; i++)
                chars[i] = (char)data[offset + i];
            return new string(chars);
        }

        private static int ReadInt16BE(byte[] data, int offset)
            => (short)((data[offset] << 8) | data[offset + 1]);

        private static int ReadUInt16BE(byte[] data, int offset)
            => (data[offset] << 8) | data[offset + 1];

        private static uint ReadUInt32BE(byte[] data, int offset)
            => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8) | data[offset + 3];

        private static int ReadInt32BE(byte[] data, int offset)
            => (int)(((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
                   | ((uint)data[offset + 2] << 8) | data[offset + 3]);
    }
}
