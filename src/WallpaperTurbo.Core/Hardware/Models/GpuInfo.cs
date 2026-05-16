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
    /// Immutable information about a GPU.
    /// </summary>
    public sealed record GpuInfo(
        string Name,
        ulong VramBytes,
        bool IsDedicated,
        GpuVendor Vendor
    );
}
