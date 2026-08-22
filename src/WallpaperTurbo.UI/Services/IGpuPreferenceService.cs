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
    /// Sets the GPU preference for multiple executable paths in a single batch operation.
    /// </summary>
    void SetGpuPreferences(IEnumerable<string> exePaths, GpuPreference mode)
    {
        if (exePaths == null) return;
        foreach (var path in exePaths)
        {
            SetGpuPreference(path, mode);
        }
    }

    /// <summary>
    /// Retrieves the current GPU preference for a given executable path.
    /// </summary>
    GpuPreference GetGpuPreference(string exePath);
}
