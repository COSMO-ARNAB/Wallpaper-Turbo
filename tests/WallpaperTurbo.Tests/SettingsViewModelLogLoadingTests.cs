using System;
using System.IO;
using System.Linq;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.Tests;

public class SettingsViewModelLogLoadingTests
{
    [Fact]
    public void ReadEngineLogsText_ReturnsFallbackMessage_WhenLogFileIsMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WallpaperTurbo.Tests", Guid.NewGuid().ToString("N"));
        var result = SettingsViewModel.ReadEngineLogsText(tempDir);

        Assert.Equal(
            "AppRunner engine log file (wallpaper.log) not generated yet. Start wallpaper to dump logs.",
            result);
    }

    [Fact]
    public void ReadEngineLogsText_ReturnsLastFifteenLines_WhenLogFileExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WallpaperTurbo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logPath = Path.Combine(tempDir, "wallpaper.log");
            var lines = Enumerable.Range(1, 20).Select(i => $"line-{i}").ToArray();
            File.WriteAllLines(logPath, lines);

            var result = SettingsViewModel.ReadEngineLogsText(tempDir);

            Assert.Equal(string.Join(Environment.NewLine, lines.Skip(5)), result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
