using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.Tests;

public class TelemetryViewModelTests
{
    [Fact]
    public void Update_FormatsAvailableRealMeasurements()
    {
        var viewModel = new TelemetryViewModel();
        var metrics = new TelemetryMetrics
        {
            CpuUsage = 3.25,
            IsCpuAvailable = true,
            GpuUsage = 7.75,
            IsGpuAvailable = true,
            RamUsageGb = 0.125,
            IsRamAvailable = true,
            Uptime = TimeSpan.FromSeconds(65)
        };

        viewModel.Update(metrics, true);

        Assert.Equal("3.3%", viewModel.CpuText);
        Assert.Equal("7.8%", viewModel.GpuText);
        Assert.Equal("0.13 GB", viewModel.RamText);
        Assert.Equal("00:01:05", viewModel.UptimeText);
    }

    [Fact]
    public void Update_DoesNotPresentUnavailableMeasurementsAsZero()
    {
        var viewModel = new TelemetryViewModel();

        viewModel.Update(new TelemetryMetrics(), true);

        Assert.Equal("N/A", viewModel.CpuText);
        Assert.Equal("N/A", viewModel.GpuText);
        Assert.Equal("N/A", viewModel.VramText);
        Assert.Equal("N/A", viewModel.FpsText);
    }

    [Fact]
    public void Update_HidesStaleMeasurementsWhenEngineStops()
    {
        var viewModel = new TelemetryViewModel();
        var metrics = new TelemetryMetrics
        {
            CpuUsage = 12,
            IsCpuAvailable = true,
            RamUsageGb = 0.5,
            IsRamAvailable = true
        };

        viewModel.Update(metrics, false);

        Assert.Equal("N/A", viewModel.CpuText);
        Assert.Equal("N/A", viewModel.RamText);
        Assert.Equal("00:00:00", viewModel.UptimeText);
    }
}
