using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware.Models;

namespace WallpaperTurbo.Core.Hardware
{
    /// <summary>
    /// Abstraction for hardware/OS-specific detection routines.
    /// </summary>
    public interface IHardwareDetector
    {
        /// <summary>
        /// Returns the GPUs currently present in the system.
        /// </summary>
        Task<IEnumerable<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Windows implementation of <see cref="IHardwareDetector"/> using WMI.
    /// </summary>
    public sealed class WindowsHardwareDetector : IHardwareDetector
    {
        /// <inheritdoc />
        public Task<IEnumerable<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var results = new List<GpuInfo>();

                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, AdapterRAM, VideoProcessor FROM Win32_VideoController"
                );

                foreach (ManagementObject mo in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = (mo["Name"]?.ToString() ?? string.Empty).Trim();

                    // AdapterRAM can be null or a numeric type. Convert defensively to ulong.
                    ulong vram = 0;
                    try
                    {
                        var adapterRam = mo["AdapterRAM"];
                        if (adapterRam != null)
                        {
                            vram = Convert.ToUInt64(adapterRam);
                        }
                    }
                    catch
                    {
                        vram = 0;
                    }

                    var vendor = ParseVendor(name);

                    // Heuristic: Intel is typically integrated, others dedicated in most systems.
                    var isDedicated = vendor != GpuVendor.Intel;

                    results.Add(new GpuInfo(name == string.Empty ? "Unknown" : name, vram, isDedicated, vendor));
                }

                return (IEnumerable<GpuInfo>)results;
            }, cancellationToken);
        }

        private static GpuVendor ParseVendor(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return GpuVendor.Unknown;

            var n = displayName.ToLowerInvariant();
            if (n.Contains("nvidia"))
                return GpuVendor.Nvidia;
            if (n.Contains("intel"))
                return GpuVendor.Intel;
            if (n.Contains("amd") || n.Contains("radeon") || n.Contains("ati"))
                return GpuVendor.Amd;

            return GpuVendor.Unknown;
        }
    }
}
