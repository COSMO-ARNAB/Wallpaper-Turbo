using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.Tests;

public class DashboardViewModelPercentageTests
{
    [Fact]
    public void CalculatePercentage_ReturnsZero_WhenTotalIsZero()
    {
        var result = DashboardViewModel.CalculatePercentage(2, 0);

        Assert.Equal(0.0, result);
    }

    [Fact]
    public void CalculatePercentage_ReturnsExpectedValue_WhenTotalIsPositive()
    {
        var result = DashboardViewModel.CalculatePercentage(2, 8);

        Assert.Equal(25.0, result);
    }

    [Fact]
    public void CalculatePercentage_ReturnsZero_WhenTotalIsNotFinite()
    {
        var result = DashboardViewModel.CalculatePercentage(2, double.PositiveInfinity);

        Assert.Equal(0.0, result);
    }
}
