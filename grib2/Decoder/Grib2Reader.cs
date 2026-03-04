#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Grib2.Decoder.Sections;
using Grib2.Models;
using Grib2.Templates.Packing;

namespace Grib2.Decoder
{
    /// <summary>
    /// Entry point for GRIB2 binary decoding. Parses GRIB2 files containing one or more
    /// concatenated messages into <see cref="Grib2Message"/> objects.
    /// 
    /// Usage:
    ///   var reader = new Grib2Reader(grib2Bytes);
    ///   foreach (var message in reader.ReadMessages()) { ... }
    /// </summary>
    public class Grib2Reader
    {
        private readonly ReadOnlyMemory<byte> _data;

        /// <summary>
        /// Create a reader from a byte array or ReadOnlyMemory.
        /// </summary>
        public Grib2Reader(ReadOnlyMemory<byte> data)
        {
            _data = data;
        }

        /// <summary>
        /// Create a reader from a byte array.
        /// </summary>
        public Grib2Reader(byte[] data)
        {
            _data = data;
        }

        /// <summary>
        /// Create a reader from a stream (reads entire stream into memory).
        /// </summary>
        public Grib2Reader(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _data = ms.ToArray();
        }

        /// <summary>
        /// Parse all GRIB2 messages from the data.
        /// GRIB2 files can contain multiple messages concatenated back-to-back.
        /// </summary>
        /// <returns>Enumerable of decoded messages.</returns>
        public IEnumerable<Grib2Message> ReadMessages()
        {
            var messages = new List<Grib2Message>();
            ReadOnlySpan<byte> span = _data.Span;
            int offset = 0;

            while (offset < span.Length - 4)
            {
                // Scan for "GRIB" magic bytes
                long magicOffset = IndicatorSection.FindMagic(span, offset);
                if (magicOffset < 0)
                    break; // No more messages

                offset = (int)magicOffset;

                // Ensure we have enough bytes for the indicator section
                if (offset + IndicatorSection.SectionLength > span.Length)
                    break;

                Grib2Message? message = null;
                try
                {
                    message = ParseMessage(span, offset);
                }
                catch (Exception ex)
                {
                    // Log and skip malformed messages
                    System.Diagnostics.Debug.WriteLine($"GRIB2 parse error at offset {offset}: {ex.Message}");
                }

                if (message != null)
                {
                    messages.Add(message);
                    // Advance past this message
                    offset += (int)message.TotalLength;
                }
                else
                {
                    // Skip past the magic bytes to find the next message
                    offset += 4;
                }
            }

            return messages;
        }

        /// <summary>
        /// Parse all messages and return as a list (eager evaluation).
        /// </summary>
        public List<Grib2Message> ReadAllMessages()
        {
            var messages = new List<Grib2Message>();
            foreach (var msg in ReadMessages())
                messages.Add(msg);
            return messages;
        }

        /// <summary>
        /// Parse a single GRIB2 message starting at the given offset.
        /// </summary>
        private Grib2Message ParseMessage(ReadOnlySpan<byte> span, int startOffset)
        {
            var message = new Grib2Message { FileOffset = startOffset };
            int offset = startOffset;

            // --- Section 0: Indicator ---
            IndicatorSection.Parse(span.Slice(offset), out byte discipline, out byte edition, out long totalLength);
            message.TotalLength = totalLength;
            message.Metadata.Discipline = discipline;
            message.Metadata.Edition = edition;
            message.Metadata.TotalLength = totalLength;
            offset += IndicatorSection.SectionLength;

            int messageEnd = startOffset + (int)totalLength;
            if (messageEnd > span.Length)
                messageEnd = span.Length; // Truncated file — parse what we can

            // --- Section 1: Identification ---
            int sec1Len = IdentificationSection.Parse(span.Slice(offset), message.Metadata);
            offset += sec1Len;

            // --- Section 2: Local Use (optional) ---
            if (offset < messageEnd && LocalUseSection.IsLocalUseSection(span.Slice(offset)))
            {
                int sec2Len = LocalUseSection.Parse(span.Slice(offset), out var localData);
                message.LocalUseData = localData;
                offset += sec2Len;
            }

            // --- Sections 3–7 may repeat (for multiple fields in one message) ---
            // We parse the first occurrence; repeated sections would create additional messages.
            bool[]? lastBitmap = null;

            while (offset < messageEnd - 4)
            {
                // Check for end section "7777"
                if (EndSection.Validate(span.Slice(offset)))
                {
                    offset += EndSection.SectionLength;
                    break;
                }

                // Read section number from octet 5 (index 4)
                if (offset + 5 > span.Length)
                    break;

                byte sectionNumber = span[offset + 4];

                switch (sectionNumber)
                {
                    case 3: // Grid Definition
                        int sec3Len = GridDefinitionSection.Parse(span.Slice(offset), message.Grid);
                        offset += sec3Len;
                        break;

                    case 4: // Product Definition
                        int sec4Len = ProductDefinitionSection.Parse(span.Slice(offset), message.Field, discipline);
                        offset += sec4Len;
                        break;

                    case 5: // Data Representation
                        int sec5Len = DataRepresentationSection.Parse(span.Slice(offset), message.Field, out _);
                        // Store the full section 5 template data for packing templates
                        _lastSection5Data = span.Slice(offset, sec5Len).ToArray();
                        offset += sec5Len;
                        break;

                    case 6: // Bitmap
                        int sec6Len = BitmapSection.Parse(span.Slice(offset), message.Grid.NumberOfDataPoints,
                            out bool hasBitmap, out bool[]? bitmap);
                        message.Field.HasBitmap = hasBitmap;
                        if (bitmap != null)
                        {
                            message.Field.Bitmap = bitmap;
                            lastBitmap = bitmap;
                        }
                        else if (hasBitmap && lastBitmap != null)
                        {
                            // Indicator 254: reuse previous bitmap
                            message.Field.Bitmap = lastBitmap;
                        }
                        offset += sec6Len;
                        break;

                    case 7: // Data
                        int sec7Len = DataSection.Parse(span.Slice(offset), out var packedData);
                        // Unpack the data using the appropriate packing template
                        UnpackData(packedData, message.Field, message.Grid.NumberOfDataPoints);
                        offset += sec7Len;
                        break;

                    default:
                        // Unknown section — skip it using the section length
                        if (offset + 4 <= span.Length)
                        {
                            int unknownLen = (int)span.ReadUInt32BE(offset);
                            if (unknownLen < 5) unknownLen = 5; // Safety
                            offset += unknownLen;
                        }
                        else
                        {
                            offset = messageEnd; // Can't read length, bail
                        }
                        break;
                }
            }

            return message;
        }

        /// <summary>Temporary storage for Section 5 data needed during Section 7 unpacking.</summary>
        private byte[]? _lastSection5Data;

        /// <summary>
        /// Unpack Section 7 data bytes into floating-point values using the packing template
        /// identified in Section 5.
        /// </summary>
        private void UnpackData(ReadOnlySpan<byte> packedData, Grib2Field field, int numberOfDataPoints)
        {
            try
            {
                // Get the Section 5 template data for complex unpacking
                ReadOnlySpan<byte> sec5Template = _lastSection5Data != null
                    ? DataRepresentationSection.GetFullTemplateData(_lastSection5Data)
                    : ReadOnlySpan<byte>.Empty;

                float[] values = field.PackingTemplateNumber switch
                {
                    0 => SimplePackingTemplate.Unpack(packedData, field, numberOfDataPoints),
                    2 => ComplexPackingTemplate.Unpack(packedData, sec5Template, field, numberOfDataPoints),
                    3 => ComplexSpatialPackingTemplate.Unpack(packedData, sec5Template, field, numberOfDataPoints),
                    40 => PngPackingTemplate.Unpack(packedData, field, numberOfDataPoints),
                    41 => Jpeg2000PackingTemplate.Unpack(packedData, field, numberOfDataPoints),
                    _ => CreateMissingValues(numberOfDataPoints)
                };

                // Apply bitmap: set missing points to NaN
                if (field.HasBitmap && field.Bitmap != null)
                {
                    values = ApplyBitmap(values, field.Bitmap, numberOfDataPoints);
                }

                field.Values = values;
            }
            catch (NotSupportedException)
            {
                // Template not implemented (e.g., JPEG2000)
                field.Values = CreateMissingValues(numberOfDataPoints);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unpack error (template {field.PackingTemplateNumber}): {ex.Message}");
                field.Values = CreateMissingValues(numberOfDataPoints);
            }
        }

        /// <summary>
        /// Apply a bitmap to expand packed values into a full grid array.
        /// Values where bitmap[i] = false are set to NaN.
        /// </summary>
        private static float[] ApplyBitmap(float[] packedValues, bool[] bitmap, int numberOfDataPoints)
        {
            var result = new float[numberOfDataPoints];
            int packedIndex = 0;

            for (int i = 0; i < numberOfDataPoints; i++)
            {
                if (i < bitmap.Length && bitmap[i])
                {
                    result[i] = packedIndex < packedValues.Length ? packedValues[packedIndex++] : float.NaN;
                }
                else
                {
                    result[i] = float.NaN;
                }
            }

            return result;
        }

        /// <summary>
        /// Create an array filled with NaN (missing) values.
        /// </summary>
        private static float[] CreateMissingValues(int count)
        {
            var values = new float[count];
            Array.Fill(values, float.NaN);
            return values;
        }
    }
}
