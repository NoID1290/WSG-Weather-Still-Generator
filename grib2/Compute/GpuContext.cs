#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

namespace Grib2.Compute
{
    /// <summary>
    /// ILGPU device management — singleton GPU context for the Grib2 library.
    /// Selects the best available accelerator: CUDA → OpenCL → CPU fallback.
    /// All kernel compilation and memory allocation flows through this context.
    /// Implements IDisposable for clean GPU resource teardown.
    /// </summary>
    public sealed class GpuContext : IDisposable
    {
        private static readonly Lazy<GpuContext> _instance = new(() => new GpuContext());
        private bool _disposed;

        /// <summary>Shared singleton instance. All consumers share one GPU context.</summary>
        public static GpuContext Instance => _instance.Value;

        /// <summary>The ILGPU context (manages device compilation).</summary>
        public Context Context { get; }

        /// <summary>The selected accelerator device.</summary>
        public Accelerator Accelerator { get; }

        /// <summary>Name of the selected device (e.g., "NVIDIA GeForce RTX 3080" or "CPU Accelerator").</summary>
        public string DeviceName { get; }

        /// <summary>Type of accelerator selected.</summary>
        public AcceleratorType AcceleratorType { get; }

        /// <summary>True if a GPU accelerator was selected (CUDA or OpenCL).</summary>
        public bool IsGpuAvailable => AcceleratorType != AcceleratorType.CPU;

        /// <summary>
        /// Create a new GpuContext — selects the best available device.
        /// Prefer CUDA → OpenCL → CPU in order.
        /// </summary>
        private GpuContext()
        {
            Context = Context.Create(builder =>
            {
                builder.Default();
                builder.EnableAlgorithms();
            });

            Accelerator = SelectBestAccelerator(Context);
            DeviceName = Accelerator.Name;
            AcceleratorType = Accelerator.AcceleratorType;

            Debug.WriteLine($"[Grib2.GpuContext] Selected device: {DeviceName} ({AcceleratorType})");
            Debug.WriteLine($"[Grib2.GpuContext] Max threads/group: {Accelerator.MaxNumThreadsPerGroup}");
            Debug.WriteLine($"[Grib2.GpuContext] Max shared memory: {Accelerator.MaxSharedMemoryPerGroup} bytes");
        }

        /// <summary>
        /// Create a GpuContext with a specific accelerator (for testing).
        /// </summary>
        public GpuContext(Accelerator accelerator)
        {
            Context = accelerator.Context;
            Accelerator = accelerator;
            DeviceName = accelerator.Name;
            AcceleratorType = accelerator.AcceleratorType;
        }

        /// <summary>
        /// Select the best available accelerator.
        /// Priority: CUDA → OpenCL → CPU.
        /// </summary>
        private static Accelerator SelectBestAccelerator(Context context)
        {
            // Try CUDA first
            var cudaDevices = context.GetCudaDevices();
            if (cudaDevices.Count > 0)
            {
                try
                {
                    Device? best = null;
                    int maxThreads = 0;
                    foreach (var d in cudaDevices)
                    {
                        if (d.MaxNumThreadsPerGroup > maxThreads)
                        {
                            maxThreads = d.MaxNumThreadsPerGroup;
                            best = d;
                        }
                    }
                    if (best != null)
                        return best.CreateAccelerator(context);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Grib2.GpuContext] CUDA init failed: {ex.Message}");
                }
            }

            // Try OpenCL
            var clDevices = context.GetCLDevices();
            if (clDevices.Count > 0)
            {
                try
                {
                    // Prefer GPU-type CL devices over CPU-type
                    CLDevice? gpuDevice = null;
                    CLDevice? firstDevice = null;
                    foreach (var d in clDevices)
                    {
                        firstDevice ??= d;
                        if (d.DeviceType == CLDeviceType.CL_DEVICE_TYPE_GPU)
                        {
                            gpuDevice = d;
                            break;
                        }
                    }
                    var selectedDevice = gpuDevice ?? firstDevice!;
                    return selectedDevice.CreateAccelerator(context);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Grib2.GpuContext] OpenCL init failed: {ex.Message}");
                }
            }

            // Fall back to CPU
            Debug.WriteLine("[Grib2.GpuContext] Falling back to CPU accelerator");
            var cpuDevice = context.GetCPUDevice(0);
            return cpuDevice.CreateAccelerator(context);
        }

        /// <summary>
        /// Allocate a 1D GPU buffer and upload data.
        /// </summary>
        public MemoryBuffer1D<T, Stride1D.Dense> AllocateAndUpload<T>(T[] data) where T : unmanaged
        {
            var buffer = Accelerator.Allocate1D<T>(data.Length);
            buffer.CopyFromCPU(data);
            return buffer;
        }

        /// <summary>
        /// Allocate an empty 1D GPU buffer.
        /// </summary>
        public MemoryBuffer1D<T, Stride1D.Dense> Allocate<T>(int length) where T : unmanaged
        {
            return Accelerator.Allocate1D<T>(length);
        }

        /// <summary>
        /// Download data from a GPU buffer to a CPU array.
        /// </summary>
        public T[] Download<T>(MemoryBuffer1D<T, Stride1D.Dense> buffer) where T : unmanaged
        {
            var result = new T[buffer.Length];
            buffer.CopyToCPU(result);
            return result;
        }

        /// <summary>
        /// Synchronize the accelerator — wait for all pending operations to complete.
        /// </summary>
        public void Synchronize() => Accelerator.Synchronize();

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Accelerator?.Dispose();
                Context?.Dispose();
            }
        }
    }
}
