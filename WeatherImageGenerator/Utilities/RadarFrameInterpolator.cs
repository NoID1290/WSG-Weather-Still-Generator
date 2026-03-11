using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// CPU-side radar frame interpolator that generates smooth synthetic in-between
    /// frames using block-matching optical flow and bidirectional pixel warping.
    ///
    /// The algorithm:
    ///   1. Decode each PNG pair to locked ARGB pixel arrays.
    ///   2. Build 1/4-scale grayscale images for fast motion estimation.
    ///   3. Block-matching (16×16 SAD, ±16 pixel search) produces a sparse
    ///      vector grid on the downscaled image.
    ///   4. Bilinear upscale of the motion grid to full-resolution per-pixel field.
    ///   5. For each intermediate time t: bidirectional reverse-warp from both frames
    ///      and blend with weight (1-t) / t.
    ///   6. Encode output as PNG byte[].
    ///
    /// Output is consumed by IMapRenderer.SetImageBytes which routes to the active
    /// GPU backend (OpenGL, DirectX, or Vulkan) without any renderer-specific changes,
    /// because all three backends share the same byte[] PNG ingestion interface.
    /// </summary>
    internal static class RadarFrameInterpolator
    {
        // ──────────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Inserts (interpolationFactor - 1) synthetic frames between every consecutive
        /// pair of real radar frames.
        /// </summary>
        /// <param name="realFrames">Source PNG byte arrays loaded from ECCC WMS.</param>
        /// <param name="interpolationFactor">
        ///   2 = 1 inserted frame per pair (2× smoother),
        ///   4 = 3 inserted frames per pair (4× smoother),
        ///   8 = 7 inserted frames per pair (8× smoother).
        /// </param>
        /// <param name="ct">Cancellation — triggered when factor changes or frames reload.</param>
        /// <param name="progress">Optional progress string callback for HUD updates.</param>
        /// <returns>
        ///   frames: real + synthetic interleaved list;
        ///   realIndices: set of indices in that list that are the original real frames.
        /// </returns>
        public static async Task<(List<byte[]> frames, HashSet<int> realIndices)> InterpolateAsync(
            List<byte[]> realFrames,
            int interpolationFactor,
            CancellationToken ct = default,
            IProgress<string>? progress = null)
        {
            if (realFrames.Count < 2 || interpolationFactor <= 1)
            {
                var indices = new HashSet<int>(Enumerable.Range(0, realFrames.Count));
                return (new List<byte[]>(realFrames), indices);
            }

            int insertedBetween = interpolationFactor - 1;

            return await Task.Run(() =>
            {
                int capacity = realFrames.Count + (realFrames.Count - 1) * insertedBetween;
                var result   = new List<byte[]>(capacity);
                var realIdx  = new HashSet<int>();

                for (int i = 0; i < realFrames.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    realIdx.Add(result.Count);
                    result.Add(realFrames[i]);

                    if (i < realFrames.Count - 1)
                    {
                        progress?.Report($"Generating frames {i + 1}/{realFrames.Count - 1}…");
                        var synthetics = GenerateIntermediateFrames(
                            realFrames[i], realFrames[i + 1], insertedBetween, ct);
                        result.AddRange(synthetics);
                    }
                }

                return (result, realIdx);
            }, ct);
        }

        // ──────────────────────────────────────────────────────────────────
        // Per pair: optical flow + warp
        // ──────────────────────────────────────────────────────────────────

        private static List<byte[]> GenerateIntermediateFrames(
            byte[] frameAData, byte[] frameBData, int count, CancellationToken ct)
        {
            var (pixA, w, h, strideA) = GetPixels(frameAData);
            var (pixB, _,  _, strideB) = GetPixels(frameBData);

            // 1/4-scale grayscale for motion estimation
            int sw = Math.Max(1, w / 4);
            int sh = Math.Max(1, h / 4);
            float[] grayA = BuildGrayscaleDownscaled(pixA, w, h, strideA, sw, sh);
            float[] grayB = BuildGrayscaleDownscaled(pixB, w, h, strideB, sw, sh);

            // Block matching → sparse motion grid (vectors already in full-res pixel units)
            const int BlockSize   = 16;
            const int SearchRange = 16;
            int bx = (sw + BlockSize - 1) / BlockSize;
            int by = (sh + BlockSize - 1) / BlockSize;
            float[] mvX = new float[bx * by];
            float[] mvY = new float[bx * by];
            ComputeBlockMotion(grayA, grayB, sw, sh, BlockSize, SearchRange, mvX, mvY, bx, by);

            if (ct.IsCancellationRequested) return new List<byte[]>();

            // Upscale sparse vectors to per-pixel field
            float[] fullMvX = UpscaleMotionField(mvX, bx, by, w, h);
            float[] fullMvY = UpscaleMotionField(mvY, bx, by, w, h);

            var results = new List<byte[]>(count);
            for (int k = 1; k <= count; k++)
            {
                if (ct.IsCancellationRequested) return results;
                float t = (float)k / (count + 1);
                results.Add(WarpBlendFrame(pixA, pixB, w, h, strideA, strideB, fullMvX, fullMvY, t));
            }

            return results;
        }

        // ──────────────────────────────────────────────────────────────────
        // PNG decode → locked ARGB byte[]
        // ──────────────────────────────────────────────────────────────────

        private static (byte[] pixels, int w, int h, int stride) GetPixels(byte[] pngData)
        {
            using var ms  = new MemoryStream(pngData);
            using var src = new Bitmap(ms);

            // Normalize to 32bppArgb so pixel layout is always BGRA
            using var bmp = src.PixelFormat == PixelFormat.Format32bppArgb
                ? (Bitmap)src.Clone()
                : src.Clone(new Rectangle(0, 0, src.Width, src.Height),
                            PixelFormat.Format32bppArgb);

            int w = bmp.Width, h = bmp.Height;
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                                  ImageLockMode.ReadOnly,
                                  PixelFormat.Format32bppArgb);
            int stride   = bd.Stride;
            byte[] pixels = new byte[stride * h];
            Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(bd);
            return (pixels, w, h, stride);
        }

        // ──────────────────────────────────────────────────────────────────
        // Grayscale downscale for motion estimation
        // ──────────────────────────────────────────────────────────────────

        private static float[] BuildGrayscaleDownscaled(
            byte[] pixels, int srcW, int srcH, int stride, int dstW, int dstH)
        {
            float[] gray = new float[dstW * dstH];
            float sx = (float)srcW / dstW;
            float sy = (float)srcH / dstH;

            for (int y = 0; y < dstH; y++)
            for (int x = 0; x < dstW; x++)
            {
                int px  = Math.Clamp((int)(x * sx), 0, srcW - 1);
                int py  = Math.Clamp((int)(y * sy), 0, srcH - 1);
                int idx = py * stride + px * 4;

                float a = pixels[idx + 3] / 255f;
                if (a < 0.05f)
                {
                    gray[y * dstW + x] = 0f;   // transparent = no data
                    continue;
                }

                float r = pixels[idx + 2] / 255f;
                float g = pixels[idx + 1] / 255f;
                float b = pixels[idx + 0] / 255f;
                // Premultiplied luminance — transparent areas have near-zero weight
                gray[y * dstW + x] = (0.299f * r + 0.587f * g + 0.114f * b) * a;
            }

            return gray;
        }

        // ──────────────────────────────────────────────────────────────────
        // Block-matching optical flow (A→B direction, SAD metric)
        // ──────────────────────────────────────────────────────────────────

        private static void ComputeBlockMotion(
            float[] gA, float[] gB, int sw, int sh,
            int blockSize, int searchRange,
            float[] mvX, float[] mvY, int bx, int by)
        {
            int half = blockSize / 2;

            for (int by_idx = 0; by_idx < by; by_idx++)
            for (int bx_idx = 0; bx_idx < bx; bx_idx++)
            {
                int cx = bx_idx * blockSize + half;
                int cy = by_idx * blockSize + half;

                float bestSad = float.MaxValue;
                int   bestDx  = 0, bestDy = 0;

                for (int dy = -searchRange; dy <= searchRange; dy++)
                for (int dx = -searchRange; dx <= searchRange; dx++)
                {
                    float sad   = 0f;
                    int   count = 0;

                    for (int py = -half; py < half; py++)
                    for (int px = -half; px < half; px++)
                    {
                        int ax  = Math.Clamp(cx + px,      0, sw - 1);
                        int ay  = Math.Clamp(cy + py,      0, sh - 1);
                        int bxP = Math.Clamp(cx + px + dx, 0, sw - 1);
                        int byP = Math.Clamp(cy + py + dy, 0, sh - 1);

                        sad += Math.Abs(gA[ay * sw + ax] - gB[byP * sw + bxP]);
                        count++;
                    }

                    if (count > 0) sad /= count;

                    if (sad < bestSad)
                    {
                        bestSad = sad;
                        bestDx  = dx;
                        bestDy  = dy;
                    }
                }

                int idx = by_idx * bx + bx_idx;
                // Scale back to full-resolution pixel units (×4 because we downscaled ÷4)
                mvX[idx] = bestDx * 4f;
                mvY[idx] = bestDy * 4f;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Bilinear upscale of block-grid motion field → per-pixel field
        // ──────────────────────────────────────────────────────────────────

        private static float[] UpscaleMotionField(
            float[] field, int bx, int by, int fullW, int fullH)
        {
            float[] full = new float[fullW * fullH];

            if (bx == 1 && by == 1)
            {
                Array.Fill(full, field[0]);
                return full;
            }

            for (int y = 0; y < fullH; y++)
            for (int x = 0; x < fullW; x++)
            {
                float gx  = (float)x / (fullW - 1) * (bx - 1);
                float gy  = (float)y / (fullH - 1) * (by - 1);
                int   gx0 = (int)gx,              gy0 = (int)gy;
                int   gx1 = Math.Min(gx0 + 1, bx - 1);
                int   gy1 = Math.Min(gy0 + 1, by - 1);
                float fx  = gx - gx0,             fy  = gy - gy0;

                float v00 = field[gy0 * bx + gx0];
                float v10 = field[gy0 * bx + gx1];
                float v01 = field[gy1 * bx + gx0];
                float v11 = field[gy1 * bx + gx1];

                full[y * fullW + x] =
                    v00 * (1f - fx) * (1f - fy) +
                    v10 * fx        * (1f - fy) +
                    v01 * (1f - fx) * fy        +
                    v11 * fx        * fy;
            }

            return full;
        }

        // ──────────────────────────────────────────────────────────────────
        // Bidirectional warp blend → PNG byte[]
        // ──────────────────────────────────────────────────────────────────

        private static byte[] WarpBlendFrame(
            byte[] pixA, byte[] pixB, int w, int h, int strideA, int strideB,
            float[] mvX, float[] mvY, float t)
        {
            int outStride = w * 4;              // tightly packed row bytes
            byte[] outPix = new byte[outStride * h];

            // Parallel over rows — safe because each row is independent
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float mvx = mvX[y * w + x];
                    float mvy = mvY[y * w + x];

                    // Reverse warp from A:  where in A did this output pixel come from?
                    float sAx = x - t       * mvx;
                    float sAy = y - t       * mvy;

                    // Reverse warp from B:  where in B did this output pixel come from?
                    float sBx = x + (1f - t) * mvx;
                    float sBy = y + (1f - t) * mvy;

                    BilinearSample(pixA, w, h, strideA, sAx, sAy,
                        out float rA, out float gA, out float bA, out float aA);
                    BilinearSample(pixB, w, h, strideB, sBx, sBy,
                        out float rB, out float gB, out float bB, out float aB);

                    float r = rA * (1f - t) + rB * t;
                    float g = gA * (1f - t) + gB * t;
                    float b = bA * (1f - t) + bB * t;
                    float a = aA * (1f - t) + aB * t;

                    int idx        = y * outStride + x * 4;
                    outPix[idx + 0] = (byte)Math.Clamp(b * 255f, 0f, 255f);
                    outPix[idx + 1] = (byte)Math.Clamp(g * 255f, 0f, 255f);
                    outPix[idx + 2] = (byte)Math.Clamp(r * 255f, 0f, 255f);
                    outPix[idx + 3] = (byte)Math.Clamp(a * 255f, 0f, 255f);
                }
            });

            // Write to Bitmap and encode as PNG
            using var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bd = result.LockBits(new Rectangle(0, 0, w, h),
                                     ImageLockMode.WriteOnly,
                                     PixelFormat.Format32bppArgb);

            if (bd.Stride == outStride)
            {
                Marshal.Copy(outPix, 0, bd.Scan0, outPix.Length);
            }
            else
            {
                // Handle stride padding (e.g. width not a multiple of 4)
                for (int row = 0; row < h; row++)
                    Marshal.Copy(outPix, row * outStride, bd.Scan0 + row * bd.Stride, outStride);
            }

            result.UnlockBits(bd);

            using var ms = new MemoryStream();
            result.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        // ──────────────────────────────────────────────────────────────────
        // Bilinear sampler: BGRA byte[] → float RGBA (clamped at edges)
        // ──────────────────────────────────────────────────────────────────

        private static void BilinearSample(
            byte[] pixels, int w, int h, int stride,
            float sx, float sy,
            out float r, out float g, out float b, out float a)
        {
            sx = Math.Clamp(sx, 0f, w - 1f);
            sy = Math.Clamp(sy, 0f, h - 1f);

            int   x0 = (int)sx,             y0 = (int)sy;
            int   x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fx = sx - x0,             fy = sy - y0;

            ReadBgra(pixels, y0 * stride + x0 * 4, out float b00, out float g00, out float r00, out float a00);
            ReadBgra(pixels, y0 * stride + x1 * 4, out float b10, out float g10, out float r10, out float a10);
            ReadBgra(pixels, y1 * stride + x0 * 4, out float b01, out float g01, out float r01, out float a01);
            ReadBgra(pixels, y1 * stride + x1 * 4, out float b11, out float g11, out float r11, out float a11);

            float w00 = (1f - fx) * (1f - fy);
            float w10 = fx        * (1f - fy);
            float w01 = (1f - fx) * fy;
            float w11 = fx        * fy;

            r = r00 * w00 + r10 * w10 + r01 * w01 + r11 * w11;
            g = g00 * w00 + g10 * w10 + g01 * w01 + g11 * w11;
            b = b00 * w00 + b10 * w10 + b01 * w01 + b11 * w11;
            a = a00 * w00 + a10 * w10 + a01 * w01 + a11 * w11;
        }

        private static void ReadBgra(byte[] pixels, int idx,
            out float b, out float g, out float r, out float a)
        {
            b = pixels[idx + 0] / 255f;
            g = pixels[idx + 1] / 255f;
            r = pixels[idx + 2] / 255f;
            a = pixels[idx + 3] / 255f;
        }
    }
}
