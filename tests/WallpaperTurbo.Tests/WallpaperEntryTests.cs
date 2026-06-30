using Xunit;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.Tests;

public class WallpaperEntryTests
{
    [Theory]
    [InlineData("30", "30 FPS")]
    [InlineData("60 FPS", "60 FPS")]
    [InlineData("60 fps", "60 FPS")]
    [InlineData("120 FPS FPS", "120 FPS")]
    [InlineData("", "30 FPS")]
    [InlineData(null, "30 FPS")]
    public void Fps_Property_CleansUpAndNormalizesSuffix(string? input, string expected)
    {
        // Arrange
        var entry = new WallpaperEntry();

        // Act
        entry.Fps = input!;

        // Assert
        Assert.Equal(expected, entry.Fps);
    }
}
