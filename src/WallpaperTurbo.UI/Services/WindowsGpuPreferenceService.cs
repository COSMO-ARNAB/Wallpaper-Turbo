using System;
using Microsoft.Win32;
using WallpaperTurbo.Core.Hardware;

namespace WallpaperTurbo.UI.Services;

/// <summary>
/// Windows-specific implementation of GPU routing preferences targeting the DirectX UserGpuPreferences registry key.
/// </summary>
public class WindowsGpuPreferenceService : IGpuPreferenceService
{
    private const string RegistryKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public void SetGpuPreference(string exePath, GpuPreference mode)
    {
        if (string.IsNullOrEmpty(exePath)) return;
        SetGpuPreferences(new[] { exePath }, mode);
    }

    public void SetGpuPreferences(IEnumerable<string> exePaths, GpuPreference mode)
    {
        if (exePaths == null) return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            if (key == null) return;

            foreach (var exePath in exePaths)
            {
                if (string.IsNullOrWhiteSpace(exePath)) continue;

                switch (mode)
                {
                    case GpuPreference.Integrated:
                        key.SetValue(exePath, "GpuPreference=1;");
                        break;
                    case GpuPreference.Dedicated:
                        key.SetValue(exePath, "GpuPreference=2;");
                        break;
                    case GpuPreference.Auto:
                    default:
                        key.DeleteValue(exePath, throwOnMissingValue: false);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsGpuPreferenceService] Failed to set GPU preference registry values: {ex.Message}");
        }
    }

    public GpuPreference GetGpuPreference(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return GpuPreference.Auto;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key == null) return GpuPreference.Auto;

            var value = key.GetValue(exePath) as string;
            if (string.IsNullOrEmpty(value)) return GpuPreference.Auto;

            if (value.Contains("GpuPreference=1"))
                return GpuPreference.Integrated;
            if (value.Contains("GpuPreference=2"))
                return GpuPreference.Dedicated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsGpuPreferenceService] Failed to read GPU preference registry value: {ex.Message}");
        }

        return GpuPreference.Auto;
    }
}
