using WallpaperTurbo.Core.Hardware;

namespace WallpaperTurbo.UI.Services;

/// <summary>
/// Service interface to manage system/OS-level GPU preferences for executables.
/// </summary>
public interface IGpuPreferenceService
{
    /// <summary>
    /// Sets the GPU preference for a given executable path.
    /// </summary>
    void SetGpuPreference(string exePath, GpuPreference mode);

    /// <summary>
    /// Retrieves the current GPU preference for a given executable path.
    /// </summary>
    GpuPreference GetGpuPreference(string exePath);
}
