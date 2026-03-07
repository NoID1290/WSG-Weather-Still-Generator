#nullable enable
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Grib2.Decoder
{
    /// <summary>
    /// Low-level bit-stream reader for unpacking arbitrary-width fields from GRIB2 binary data.
    /// GRIB2 frequently uses non-byte-aligned integer fields (e.g., 12-bit packed values).
    /// All reads advance the internal bit position sequentially.
    /// </summary>
    public ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bitOffset;

        /// <summary>Current bit offset within the data span.</summary>
        public int BitOffset => _bitOffset;

        /// <summary>Total number of bits available.</summary>
        public int TotalBits => _data.Length * 8;

        /// <summary>Number of bits remaining to read.</summary>
        public int RemainingBits => TotalBits - _bitOffset;

        /// <summary>
        /// Create a new BitReader over a byte span starting at bit offset 0.
        /// </summary>
        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bitOffset = 0;
        }

        /// <summary>
        /// Create a new BitReader starting at a specific bit offset.
        /// </summary>
        public BitReader(ReadOnlySpan<byte> data, int startBitOffset)
        {
            _data = data;
            _bitOffset = startBitOffset;
        }

        /// <summary>
        /// Read an unsigned integer of the specified bit width (1..32).
        /// Returns 0 for 0-bit reads.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt(int bits)
        {
            if (bits == 0) return 0;
            if (bits < 0 || bits > 32)
                throw new ArgumentOutOfRangeException(nameof(bits), "Bit count must be 0..32");
            if (_bitOffset + bits > TotalBits)
                throw new InvalidOperationException($"Not enough bits: need {bits}, have {RemainingBits}");

            uint result = 0;
            int bitsRemaining = bits;

            while (bitsRemaining > 0)
            {
                int byteIndex = _bitOffset >> 3;        // _bitOffset / 8
                int bitInByte = _bitOffset & 7;          // _bitOffset % 8
                int bitsAvailableInByte = 8 - bitInByte;
                int bitsToRead = Math.Min(bitsAvailableInByte, bitsRemaining);

                // Extract bits from the current byte (MSB first, GRIB2 convention)
                int shift = bitsAvailableInByte - bitsToRead;
                uint mask = (uint)((1 << bitsToRead) - 1);
                uint extracted = ((uint)_data[byteIndex] >> shift) & mask;

                result = (result << bitsToRead) | extracted;
                _bitOffset += bitsToRead;
                bitsRemaining -= bitsToRead;
            }

            return result;
        }

        /// <summary>
        /// Read a signed integer of the specified bit width using two's complement.
        /// GRIB2 uses two's complement for signed values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt(int bits)
        {
            if (bits == 0) return 0;
            if (bits < 1 || bits > 32)
                throw new ArgumentOutOfRangeException(nameof(bits), "Bit count must be 1..32");

            uint raw = ReadUInt(bits);

            // Sign extend: if MSB is set, the value is negative
            if (bits < 32 && (raw & (1u << (bits - 1))) != 0)
            {
                // Fill upper bits with 1s for sign extension
                raw |= ~((1u << bits) - 1);
            }

            return (int)raw;
        }

        /// <summary>
        /// Read a signed integer using GRIB2's sign-magnitude representation.
        /// In sign-magnitude, the MSB is the sign bit (1 = negative), remaining bits are the magnitude.
        /// Used by some GRIB2 fields (e.g., scale factors).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadSignMagnitude(int bits)
        {
            if (bits == 0) return 0;
            if (bits < 2 || bits > 32)
                throw new ArgumentOutOfRangeException(nameof(bits), "Bit count must be 2..32 for sign-magnitude");

            uint raw = ReadUInt(bits);
            bool negative = (raw & (1u << (bits - 1))) != 0;
            int magnitude = (int)(raw & ((1u << (bits - 1)) - 1));

            return negative ? -magnitude : magnitude;
        }

        /// <summary>
        /// Read a single byte (8 bits) as an unsigned value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte() => (byte)ReadUInt(8);

        /// <summary>
        /// Read a 16-bit unsigned integer (big-endian, GRIB2 convention).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16() => (ushort)ReadUInt(16);

        /// <summary>
        /// Read a 32-bit unsigned integer (big-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32() => ReadUInt(32);

        /// <summary>
        /// Read a single bit as a boolean (1 = true, 0 = false).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ReadUInt(1) != 0;

        /// <summary>
        /// Read an IEEE 754 single-precision float (32 bits, big-endian).
        /// Standard IEEE 754, NOT the old GRIB1 IBM float format.
        /// GRIB2 Section 5 reference values use standard IEEE 754.
        /// </summary>
        public float ReadFloat32()
        {
            uint raw = ReadUInt(32);
            return BitConverter.Int32BitsToSingle((int)raw);
        }

        /// <summary>
        /// Read an IBM 370 floating-point value (used in GRIB1, sometimes referenced in GRIB2 docs).
        /// Format: 1-bit sign, 7-bit exponent (excess-64, base-16), 24-bit mantissa.
        /// value = (-1)^sign × (mantissa / 2^24) × 16^(exponent - 64)
        /// </summary>
        public float ReadIbmFloat()
        {
            uint raw = ReadUInt(32);

            if (raw == 0) return 0.0f;

            int sign = (int)(raw >> 31);
            int exponent = (int)((raw >> 24) & 0x7F);
            int mantissa = (int)(raw & 0x00FFFFFF);

            float value = mantissa / 16777216.0f; // mantissa / 2^24
            value *= MathF.Pow(16.0f, exponent - 64);

            return sign != 0 ? -value : value;
        }

        /// <summary>
        /// Skip the specified number of bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Skip(int bits)
        {
            if (_bitOffset + bits > TotalBits && bits > 0)
                throw new InvalidOperationException($"Cannot skip {bits} bits: only {RemainingBits} remaining");
            _bitOffset += bits;
        }

        /// <summary>
        /// Seek to an absolute bit offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Seek(int bitOffset)
        {
            if (bitOffset < 0 || bitOffset > TotalBits)
                throw new ArgumentOutOfRangeException(nameof(bitOffset));
            _bitOffset = bitOffset;
        }

        /// <summary>
        /// Check if all bits have been consumed.
        /// </summary>
        public bool IsAtEnd => _bitOffset >= TotalBits;
    }

    /// <summary>
    /// Extension methods for reading multi-byte big-endian integers from byte spans.
    /// GRIB2 uses big-endian (network byte order) for all multi-byte fields.
    /// </summary>
    public static class Grib2BinaryExtensions
    {
        /// <summary>Read a big-endian 16-bit unsigned integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUInt16BE(this ReadOnlySpan<byte> data, int offset)
            => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

        /// <summary>Read a big-endian 32-bit unsigned integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32BE(this ReadOnlySpan<byte> data, int offset)
            => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

        /// <summary>Read a big-endian 32-bit signed integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32BE(this ReadOnlySpan<byte> data, int offset)
            => BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));

        /// <summary>Read a big-endian 16-bit signed integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ReadInt16BE(this ReadOnlySpan<byte> data, int offset)
            => BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));

        /// <summary>Read a big-endian 64-bit unsigned integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64BE(this ReadOnlySpan<byte> data, int offset)
            => BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8));

        /// <summary>
        /// Read a 24-bit (3-byte) big-endian unsigned integer.
        /// Common in GRIB2 for section lengths.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt24BE(this ReadOnlySpan<byte> data, int offset)
            => ((uint)data[offset] << 16) | ((uint)data[offset + 1] << 8) | data[offset + 2];

        /// <summary>
        /// Read an IEEE 754 single-precision float from big-endian bytes.
        /// GRIB2 Section 5 reference value uses this format.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadFloat32BE(this ReadOnlySpan<byte> data, int offset)
        {
            int raw = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
            return BitConverter.Int32BitsToSingle(raw);
        }

        /// <summary>
        /// Read a signed 16-bit value using GRIB2 sign-magnitude representation.
        /// MSB = sign (1 = negative), remaining 15 bits = magnitude.
        /// Used for binary/decimal scale factors in Section 5.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ReadSignedMagnitude16BE(this ReadOnlySpan<byte> data, int offset)
        {
            ushort raw = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            bool negative = (raw & 0x8000u) != 0;
            short magnitude = (short)(raw & 0x7FFF);
            return negative ? (short)-magnitude : magnitude;
        }

        /// <summary>
        /// Read a signed 32-bit value using GRIB2 sign-magnitude representation.
        /// MSB = sign (1 = negative), remaining 31 bits = magnitude.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadSignedMagnitude32BE(this ReadOnlySpan<byte> data, int offset)
        {
            uint raw = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            bool negative = (raw & 0x80000000u) != 0;
            int magnitude = (int)(raw & 0x7FFFFFFFu);
            return negative ? -magnitude : magnitude;
        }
    }
}
