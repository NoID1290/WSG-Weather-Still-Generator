#nullable enable
using System;
using ILGPU;
using ILGPU.Runtime;

namespace Grib2.Compute.Kernels
{
    /// <summary>
    /// ILGPU kernels for spatial aggregation (min, max, mean) over grid regions.
    /// Used for computing statistics over rectangular sub-grids or entire fields.
    /// </summary>
    public static class AggregationKernels
    {
        /// <summary>
        /// Compute the minimum value in a rectangular sub-grid region.
        /// Each work item processes one target region.
        /// 
        /// Parameters:
        ///   index:     Target region index
        ///   source:    Source grid values (row-major)
        ///   output:    Output minimum for each region
        ///   srcNi:     Number of columns in source grid
        ///   regionStartRow, regionStartCol: Top-left corner of each region
        ///   regionRows, regionCols: Size of each region
        /// </summary>
        public static void RegionMinKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> output,
            int srcNi,
            ArrayView<int> regionStartRow,
            ArrayView<int> regionStartCol,
            int regionRows,
            int regionCols)
        {
            int startR = regionStartRow[index];
            int startC = regionStartCol[index];

            float minVal = float.MaxValue;

            for (int r = startR; r < startR + regionRows; r++)
            {
                for (int c = startC; c < startC + regionCols; c++)
                {
                    float val = source[r * srcNi + c];
                    if (!float.IsNaN(val) && val < minVal)
                        minVal = val;
                }
            }

            output[index] = minVal == float.MaxValue ? float.NaN : minVal;
        }

        /// <summary>
        /// Compute the maximum value in a rectangular sub-grid region.
        /// </summary>
        public static void RegionMaxKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> output,
            int srcNi,
            ArrayView<int> regionStartRow,
            ArrayView<int> regionStartCol,
            int regionRows,
            int regionCols)
        {
            int startR = regionStartRow[index];
            int startC = regionStartCol[index];

            float maxVal = float.MinValue;

            for (int r = startR; r < startR + regionRows; r++)
            {
                for (int c = startC; c < startC + regionCols; c++)
                {
                    float val = source[r * srcNi + c];
                    if (!float.IsNaN(val) && val > maxVal)
                        maxVal = val;
                }
            }

            output[index] = maxVal == float.MinValue ? float.NaN : maxVal;
        }

        /// <summary>
        /// Compute the mean value in a rectangular sub-grid region.
        /// </summary>
        public static void RegionMeanKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> output,
            int srcNi,
            ArrayView<int> regionStartRow,
            ArrayView<int> regionStartCol,
            int regionRows,
            int regionCols)
        {
            int startR = regionStartRow[index];
            int startC = regionStartCol[index];

            float sum = 0f;
            int count = 0;

            for (int r = startR; r < startR + regionRows; r++)
            {
                for (int c = startC; c < startC + regionCols; c++)
                {
                    float val = source[r * srcNi + c];
                    if (!float.IsNaN(val))
                    {
                        sum += val;
                        count++;
                    }
                }
            }

            output[index] = count > 0 ? sum / count : float.NaN;
        }

        /// <summary>
        /// Global reduction: compute min of entire array.
        /// Uses a simple sequential kernel — for large arrays, prefer chunked approach.
        /// Each thread handles a tile of the input.
        /// </summary>
        public static void TiledMinKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> partialResults,
            int totalLength,
            int tileSize)
        {
            int start = index * tileSize;
            int end = start + tileSize;
            if (end > totalLength) end = totalLength;

            float minVal = float.MaxValue;
            for (int i = start; i < end; i++)
            {
                float val = source[i];
                if (!float.IsNaN(val) && val < minVal)
                    minVal = val;
            }

            partialResults[index] = minVal;
        }

        /// <summary>
        /// Global reduction: compute max of entire array (tiled).
        /// </summary>
        public static void TiledMaxKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> partialResults,
            int totalLength,
            int tileSize)
        {
            int start = index * tileSize;
            int end = start + tileSize;
            if (end > totalLength) end = totalLength;

            float maxVal = float.MinValue;
            for (int i = start; i < end; i++)
            {
                float val = source[i];
                if (!float.IsNaN(val) && val > maxVal)
                    maxVal = val;
            }

            partialResults[index] = maxVal;
        }

        /// <summary>
        /// Global reduction: compute sum and count for mean (tiled).
        /// partialSums[index] = sum of tile, partialCounts[index] = count of valid values.
        /// </summary>
        public static void TiledSumCountKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> partialSums,
            ArrayView<int> partialCounts,
            int totalLength,
            int tileSize)
        {
            int start = index * tileSize;
            int end = start + tileSize;
            if (end > totalLength) end = totalLength;

            float sum = 0f;
            int count = 0;
            for (int i = start; i < end; i++)
            {
                float val = source[i];
                if (!float.IsNaN(val))
                {
                    sum += val;
                    count++;
                }
            }

            partialSums[index] = sum;
            partialCounts[index] = count;
        }
    }
}
