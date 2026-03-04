#nullable enable
using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;

namespace Grib2.Compute.Kernels
{
    /// <summary>
    /// ILGPU kernels for grid interpolation.
    /// Bilinear interpolation on GPU for extracting point forecasts from gridded GRIB2 data
    /// and regridding between model resolutions.
    /// </summary>
    public static class InterpolationKernels
    {
        /// <summary>
        /// Bilinear interpolation kernel — for each target point, compute interpolated value
        /// from the source grid.
        /// 
        /// Parameters:
        ///   index:      1D index over target points
        ///   source:     Source grid values (row-major, Ni×Nj)
        ///   output:     Output interpolated values (length = number of target points)
        ///   srcNi:      Number of columns in source grid
        ///   srcNj:      Number of rows in source grid
        ///   targetLats: Latitude of each target point (degrees)
        ///   targetLons: Longitude of each target point (degrees)
        ///   lat0:       Latitude of first grid point (degrees)
        ///   lon0:       Longitude of first grid point (degrees)
        ///   dLat:       Latitude increment (degrees, positive if j-direction is south→north)
        ///   dLon:       Longitude increment (degrees, positive if i-direction is west→east)
        /// </summary>
        public static void BilinearKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> output,
            int srcNi,
            int srcNj,
            ArrayView<float> targetLats,
            ArrayView<float> targetLons,
            float lat0,
            float lon0,
            float dLat,
            float dLon)
        {
            float tLat = targetLats[index];
            float tLon = targetLons[index];

            // Compute fractional grid position
            float fracCol = (tLon - lon0) / dLon;
            float fracRow = (tLat - lat0) / dLat;

            // Check bounds
            if (fracCol < 0 || fracCol >= srcNi - 1 || fracRow < 0 || fracRow >= srcNj - 1)
            {
                output[index] = float.NaN;
                return;
            }

            int col0 = (int)fracCol;
            int row0 = (int)fracRow;
            int col1 = col0 + 1;
            int row1 = row0 + 1;

            float dx = fracCol - col0;
            float dy = fracRow - row0;

            // Four corner values (row-major indexing)
            float v00 = source[row0 * srcNi + col0];
            float v10 = source[row0 * srcNi + col1];
            float v01 = source[row1 * srcNi + col0];
            float v11 = source[row1 * srcNi + col1];

            // Check for NaN
            if (float.IsNaN(v00) || float.IsNaN(v10) || float.IsNaN(v01) || float.IsNaN(v11))
            {
                output[index] = float.NaN;
                return;
            }

            // Bilinear interpolation
            output[index] = v00 * (1 - dx) * (1 - dy) +
                            v10 * dx * (1 - dy) +
                            v01 * (1 - dx) * dy +
                            v11 * dx * dy;
        }

        /// <summary>
        /// Nearest-neighbor interpolation kernel — for each target point, find the
        /// closest grid point value.
        /// </summary>
        public static void NearestNeighborKernel(
            Index1D index,
            ArrayView<float> source,
            ArrayView<float> output,
            int srcNi,
            int srcNj,
            ArrayView<float> targetLats,
            ArrayView<float> targetLons,
            float lat0,
            float lon0,
            float dLat,
            float dLon)
        {
            float tLat = targetLats[index];
            float tLon = targetLons[index];

            int col = (int)XMath.Round((tLon - lon0) / dLon);
            int row = (int)XMath.Round((tLat - lat0) / dLat);

            if (col < 0 || col >= srcNi || row < 0 || row >= srcNj)
            {
                output[index] = float.NaN;
                return;
            }

            output[index] = source[row * srcNi + col];
        }
    }
}
