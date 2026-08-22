using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// Returns the GPUs and display drivers currently present in the system.
        /// </summary>
        Task<IEnumerable<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Windows implementation of <see cref="IHardwareDetector"/> using WMI with advanced GPU and driver detection.
    /// </summary>
    public sealed class WindowsHardwareDetector : IHardwareDetector
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private IEnumerable<GpuInfo>? _cachedGpus;

        /// <inheritdoc />
        public async Task<IEnumerable<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default)
        {
            if (_cachedGpus != null)
                return _cachedGpus;

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cachedGpus != null)
                    return _cachedGpus;

                var gpus = await Task.Run(() => QueryGpusFromWmi(cancellationToken), cancellationToken).ConfigureAwait(false);
                _cachedGpus = gpus;
                return gpus;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static List<GpuInfo> QueryGpusFromWmi(CancellationToken cancellationToken)
        {
            var results = new List<GpuInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, AdapterRAM, VideoProcessor, DriverVersion, DriverDate, PNPDeviceID, Status FROM Win32_VideoController"
                );

                using var collection = searcher.Get();
                foreach (ManagementObject mo in collection)
                {
                    using (mo)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var name = (mo["Name"]?.ToString() ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(name))
                            continue;

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

                        string driverVersion = (mo["DriverVersion"]?.ToString() ?? string.Empty).Trim();
                        string rawDriverDate = (mo["DriverDate"]?.ToString() ?? string.Empty).Trim();
                        string driverDate = FormatDriverDate(rawDriverDate);
                        string deviceId = (mo["PNPDeviceID"]?.ToString() ?? string.Empty).Trim();
                        string status = (mo["Status"]?.ToString() ?? "OK").Trim();

                        var vendor = ParseVendor(name, deviceId);
                        var isDedicated = DetermineIfDedicated(name, vendor, vram);

                        results.Add(new GpuInfo(
                            Name: name,
                            VramBytes: vram,
                            IsDedicated: isDedicated,
                            Vendor: vendor,
                            DriverVersion: driverVersion,
                            DriverDate: driverDate,
                            DeviceId: deviceId,
                            Status: status
                        ));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsHardwareDetector] WMI GPU query failed: {ex.Message}");
            }

            // If real hardware GPUs were detected, filter out purely virtual / software adapters
            if (results.Count > 1 && results.Any(g => g.Vendor != GpuVendor.Unknown))
            {
                results = results.Where(g => !IsSoftwareOrVirtualAdapter(g.Name)).ToList();
            }

            // Fallback if WMI returned nothing
            if (results.Count == 0)
            {
                results.Add(new GpuInfo("Default Graphics Adapter", 0, false, GpuVendor.Unknown, "Unknown", "", "", "OK"));
            }

            return results;
        }

        public static GpuVendor ParseVendor(string displayName, string deviceId = "")
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var d = deviceId.ToUpperInvariant();
                if (d.Contains("VEN_10DE")) return GpuVendor.Nvidia;
                if (d.Contains("VEN_8086")) return GpuVendor.Intel;
                if (d.Contains("VEN_1002") || d.Contains("VEN_1022")) return GpuVendor.Amd;
            }

            if (string.IsNullOrWhiteSpace(displayName))
                return GpuVendor.Unknown;

            var n = displayName.ToLowerInvariant();
            if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("quadro") || n.Contains("rtx") || n.Contains("gtx"))
                return GpuVendor.Nvidia;
            if (n.Contains("intel") || n.Contains("arc") || n.Contains("iris") || n.Contains("uhd graphics") || n.Contains("hd graphics"))
                return GpuVendor.Intel;
            if (n.Contains("amd") || n.Contains("radeon") || n.Contains("ati") || n.Contains("firepro"))
                return GpuVendor.Amd;

            return GpuVendor.Unknown;
        }

        public static bool DetermineIfDedicated(string name, GpuVendor vendor, ulong vramBytes)
        {
            var lower = name.ToLowerInvariant();

            // NVIDIA discrete GPUs
            if (vendor == GpuVendor.Nvidia)
                return true;

            // Intel discrete vs integrated
            if (vendor == GpuVendor.Intel)
            {
                if (lower.Contains("iris xe max") || lower.Contains("data center") || lower.Contains("flex") || vramBytes >= 3UL * 1024 * 1024 * 1024)
                    return true;

                // Dedicated Intel Arc model lines (A-series, B-series, Pro)
                if (lower.Contains(" a7") || lower.Contains(" a5") || lower.Contains(" a3") ||
                    lower.Contains(" b5") || lower.Contains(" b7") || lower.Contains("pro a") ||
                    lower.Contains("pro b") || lower.Contains("arc(tm) a") || lower.Contains("arc(tm) b") ||
                    lower.Contains("arc a") || lower.Contains("arc b"))
                {
                    return true;
                }

                // Intel UHD, Iris Xe, Core Ultra "Intel(R) Arc(TM) Graphics" (Meteor/Lunar Lake) are Integrated
                return false;
            }

            // AMD discrete vs integrated
            if (vendor == GpuVendor.Amd)
            {
                // Integrated APU graphics
                if (lower.Contains("radeon(tm) graphics") || lower.Contains("radeon graphics") || lower.Contains("vega") ||
                    lower.Contains("680m") || lower.Contains("780m") || lower.Contains("890m") || lower.Contains("760m") ||
                    lower.Contains("660m") || lower.Contains("610m") || lower.Contains("740m") || lower.Contains("880m"))
                {
                    return false;
                }

                // Dedicated AMD lines
                if (lower.Contains("rx ") || lower.Contains("rx-") || lower.Contains("radeon rx") ||
                    lower.Contains("radeon pro") || lower.Contains("firepro") || lower.Contains("radeon vii") ||
                    lower.Contains("r9") || lower.Contains("r7"))
                {
                    return true;
                }

                // VRAM heuristic
                return vramBytes >= 3UL * 1024 * 1024 * 1024;
            }

            return vramBytes >= 3UL * 1024 * 1024 * 1024;
        }

        public static string FormatDriverDate(string rawDate)
        {
            if (string.IsNullOrWhiteSpace(rawDate))
                return string.Empty;

            // WMI CIM datetime format: "20250819000000.000000-000"
            if (rawDate.Length >= 8 && char.IsDigit(rawDate[0]) && char.IsDigit(rawDate[1]) && char.IsDigit(rawDate[2]) && char.IsDigit(rawDate[3]))
            {
                string year = rawDate.Substring(0, 4);
                string month = rawDate.Substring(4, 2);
                string day = rawDate.Substring(6, 2);
                return $"{year}-{month}-{day}";
            }

            // If it's already a parseable date format (e.g. "19-08-2025 05:30:00")
            if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(rawDate, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            {
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return rawDate;
        }

        private static bool IsSoftwareOrVirtualAdapter(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("basic display") ||
                   lower.Contains("remote desktop") ||
                   lower.Contains("rdpdd") ||
                   lower.Contains("vnc") ||
                   lower.Contains("citrix") ||
                   lower.Contains("parsec") ||
                   lower.Contains("iddsample");
        }
    }
}
