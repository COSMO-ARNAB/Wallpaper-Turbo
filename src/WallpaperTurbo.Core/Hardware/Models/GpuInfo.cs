using System;

namespace WallpaperTurbo.Core.Hardware.Models
{
    /// <summary>
    /// Represents a GPU detected in the system.
    /// </summary>
    public enum GpuVendor
    {
        Intel,
        Nvidia,
        Amd,
        Unknown
    }

    /// <summary>
    /// Immutable information about a detected GPU and its display driver.
    /// </summary>
    public sealed record GpuInfo(
        string Name,
        ulong VramBytes,
        bool IsDedicated,
        GpuVendor Vendor,
        string DriverVersion = "",
        string DriverDate = "",
        string DeviceId = "",
        string Status = "OK"
    )
    {
        public string FormattedVram => VramBytes switch
        {
            0 => "Shared / Dynamic VRAM",
            < 1024 * 1024 * 1024 => $"{VramBytes / (1024 * 1024)} MB",
            _ => $"{VramBytes / (1024.0 * 1024 * 1024):0.#} GB"
        };

        public string TypeLabel => IsDedicated ? "Dedicated GPU (dGPU)" : "Integrated GPU (iGPU)";

        public string FormattedDriverInfo
        {
            get
            {
                bool hasVersion = !string.IsNullOrWhiteSpace(DriverVersion);
                bool hasDate = !string.IsNullOrWhiteSpace(DriverDate);

                if (hasVersion && hasDate)
                    return $"• Driver {DriverVersion} ({DriverDate})";
                if (hasVersion)
                    return $"• Driver {DriverVersion}";
                if (hasDate)
                    return $"• ({DriverDate})";
                return string.Empty;
            }
        }

        public bool HasDriverInfo => !string.IsNullOrWhiteSpace(FormattedDriverInfo);

        public string DisplaySummary => string.IsNullOrWhiteSpace(DriverVersion)
            ? $"{Name} ({TypeLabel})"
            : $"{Name} (Driver: {DriverVersion} • {TypeLabel})";
    }
}
