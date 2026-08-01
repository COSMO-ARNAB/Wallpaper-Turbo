using System.Windows.Forms;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using Xunit;

namespace WallpaperTurbo.Tests;

public class PowerManagementServiceTests
{
    [Fact]
    public void PowerManagementService_ExposesPowerLineState()
    {
        // Assert PowerManagementService methods execute safely without throwing
        bool onBattery = PowerManagementService.IsOnBatteryPower();
        bool pluggedIn = PowerManagementService.IsPluggedIn();

        Assert.Equal(onBattery, !pluggedIn);
    }
}
