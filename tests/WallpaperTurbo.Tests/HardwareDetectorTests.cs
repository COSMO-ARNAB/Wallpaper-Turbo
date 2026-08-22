using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WallpaperTurbo.Core.Hardware;
using WallpaperTurbo.Core.Hardware.Models;
using WallpaperTurbo.Core.Updates.Interfaces;
using WallpaperTurbo.Core.Updates.Models;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;
using WallpaperTurbo.Updater;
using WallpaperTurbo.Updater.Services;
using Xunit;

namespace WallpaperTurbo.Tests
{
    public class HardwareDetectorTests
    {
        [Theory]
        [InlineData("NVIDIA GeForce RTX 4080", "", GpuVendor.Nvidia)]
        [InlineData("GeForce GTX 1080 Ti", "", GpuVendor.Nvidia)]
        [InlineData("NVIDIA RTX A5000", "", GpuVendor.Nvidia)]
        [InlineData("Intel(R) Arc(TM) A770 Graphics", "", GpuVendor.Intel)]
        [InlineData("Intel(R) UHD Graphics 770", "", GpuVendor.Intel)]
        [InlineData("Intel(R) Iris(R) Xe Graphics", "", GpuVendor.Intel)]
        [InlineData("AMD Radeon RX 7900 XTX", "", GpuVendor.Amd)]
        [InlineData("AMD Radeon(TM) Graphics", "", GpuVendor.Amd)]
        [InlineData("Generic Video Device", "PCI\\VEN_10DE&DEV_2704", GpuVendor.Nvidia)]
        [InlineData("Generic Video Device", "PCI\\VEN_8086&DEV_56A0", GpuVendor.Intel)]
        [InlineData("Generic Video Device", "PCI\\VEN_1002&DEV_744C", GpuVendor.Amd)]
        [InlineData("Unknown Display Controller", "", GpuVendor.Unknown)]
        public void ParseVendor_DetectsCorrectVendor(string name, string deviceId, GpuVendor expected)
        {
            var vendor = WindowsHardwareDetector.ParseVendor(name, deviceId);
            Assert.Equal(expected, vendor);
        }

        [Theory]
        [InlineData("NVIDIA GeForce RTX 4090", GpuVendor.Nvidia, 24UL * 1024 * 1024 * 1024, true)]
        [InlineData("Intel(R) Arc(TM) A770 Graphics", GpuVendor.Intel, 16UL * 1024 * 1024 * 1024, true)]
        [InlineData("Intel Arc A580", GpuVendor.Intel, 8UL * 1024 * 1024 * 1024, true)]
        [InlineData("Intel Arc B580", GpuVendor.Intel, 12UL * 1024 * 1024 * 1024, true)]
        [InlineData("Intel(R) Arc(TM) Graphics", GpuVendor.Intel, 1UL * 1024 * 1024 * 1024, false)] // Meteor / Lunar Lake Core Ultra iGPU
        [InlineData("Intel(R) UHD Graphics 770", GpuVendor.Intel, 1UL * 1024 * 1024 * 1024, false)]
        [InlineData("Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel, 1UL * 1024 * 1024 * 1024, false)]
        [InlineData("AMD Radeon RX 6800 XT", GpuVendor.Amd, 16UL * 1024 * 1024 * 1024, true)]
        [InlineData("AMD Radeon(TM) Graphics", GpuVendor.Amd, 512UL * 1024 * 1024, false)]
        [InlineData("AMD Radeon 780M Graphics", GpuVendor.Amd, 2UL * 1024 * 1024 * 1024, false)]
        [InlineData("AMD Radeon 890M", GpuVendor.Amd, 2UL * 1024 * 1024 * 1024, false)]
        public void DetermineIfDedicated_CorrectlyClassifiesGpuType(string name, GpuVendor vendor, ulong vram, bool expectedDedicated)
        {
            var isDedicated = WindowsHardwareDetector.DetermineIfDedicated(name, vendor, vram);
            Assert.Equal(expectedDedicated, isDedicated);
        }

        [Theory]
        [InlineData("20250819000000.000000-000", "2025-08-19")]
        [InlineData("20240508000000.000000-000", "2024-05-08")]
        [InlineData("", "")]
        public void FormatDriverDate_ParsesWmiDates(string raw, string expected)
        {
            var formatted = WindowsHardwareDetector.FormatDriverDate(raw);
            Assert.Equal(expected, formatted);
        }

        [Fact]
        public void GpuInfo_FormattingProperties_ProduceCorrectStrings()
        {
            var gpu = new GpuInfo(
                Name: "NVIDIA GeForce RTX 4080",
                VramBytes: 16UL * 1024 * 1024 * 1024,
                IsDedicated: true,
                Vendor: GpuVendor.Nvidia,
                DriverVersion: "555.85",
                DriverDate: "2024-05-15"
            );

            Assert.Equal("16 GB", gpu.FormattedVram);
            Assert.Equal("Dedicated GPU (dGPU)", gpu.TypeLabel);
            Assert.Equal("• Driver 555.85 (2024-05-15)", gpu.FormattedDriverInfo);
            Assert.True(gpu.HasDriverInfo);
            Assert.Equal("NVIDIA GeForce RTX 4080 (Driver: 555.85 • Dedicated GPU (dGPU))", gpu.DisplaySummary);

            var minimalGpu = new GpuInfo(
                Name: "Basic Display Adapter",
                VramBytes: 0,
                IsDedicated: false,
                Vendor: GpuVendor.Unknown
            );

            Assert.Equal("Shared / Dynamic VRAM", minimalGpu.FormattedVram);
            Assert.Equal(string.Empty, minimalGpu.FormattedDriverInfo);
            Assert.False(minimalGpu.HasDriverInfo);
        }

        private sealed class FakeHardwareDetector : IHardwareDetector
        {
            private readonly List<GpuInfo> _gpus;
            public FakeHardwareDetector(IEnumerable<GpuInfo> gpus) => _gpus = gpus.ToList();
            public Task<IEnumerable<GpuInfo>> GetGpusAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IEnumerable<GpuInfo>>(_gpus);
        }

        private sealed class FakeSettingsStore : ISettingsStore
        {
            public event EventHandler<AppSettings>? SettingsChanged;
            public AppSettings Load() => new();
            public void Save(AppSettings settings) => SettingsChanged?.Invoke(this, settings);
        }

        private sealed class FakeGpuPreferenceService : IGpuPreferenceService
        {
            public void SetGpuPreference(string exePath, GpuPreference mode) { }
            public GpuPreference GetGpuPreference(string exePath) => GpuPreference.Auto;
        }

        private sealed class FakeLibraryService : IWallpaperLibraryService
        {
#pragma warning disable CS0067
            public event EventHandler<WallpaperEntry>? MetadataChanged;
#pragma warning restore CS0067
            public Task<IReadOnlyList<WallpaperEntry>> GetWallpapersAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<WallpaperEntry>>(new List<WallpaperEntry>());
            public Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default, IProgress<ImportProgress>? progress = null)
                => Task.FromResult(new WallpaperEntry { Id = "1", Title = "Test" });
            public Task<bool> UpdateWallpaperMetadataAsync(string guid, string? title, string? author, CancellationToken cancellationToken = default)
                => Task.FromResult(true);
            public Task ShutdownAsync() => Task.CompletedTask;
            public Task<bool> DeleteWallpaperAsync(string id, CancellationToken cancellationToken = default)
                => Task.FromResult(true);
        }

        private sealed class FakeUpdaterSettingsStore : IUpdaterSettingsStore
        {
            private UpdaterSettings _settings = new();
            public UpdaterSettings Load() => _settings.Clone();
            public void Save(UpdaterSettings settings) => _settings = settings.Clone();
        }

        private sealed class FakeUpdateService : IUpdateService
        {
            public Task<(bool IsAvailable, UpdateManifest? Manifest)> CheckForUpdatesAsync(ReleaseChannel channel, CancellationToken cancellationToken = default)
                => Task.FromResult<(bool IsAvailable, UpdateManifest? Manifest)>((false, null));
        }

        private sealed class NoOpDownloadManager : IDownloadManager
        {
            public Task<string> DownloadUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
                => Task.FromResult(destinationPath);
        }

        private sealed class AlwaysValidSignatureValidator : ISignatureValidator
        {
            public bool IsValidSignature(string filePath) => true;
        }

        private sealed class NoOpUpdateApplier : IUpdateApplier
        {
            public void ApplyUpdate(string installerFilePath) { }
        }

        private sealed class NoOpProcessManager : IProcessManager
        {
            public Task<bool> ShutdownOtherProcessesGracefullyAsync(int timeoutMilliseconds) => Task.FromResult(true);
            public void ShutdownCurrentProcessGracefully() { }
        }

        [Fact]
        public async Task SettingsViewModel_DualGpuDetection_IdentifiesHybridSystemAndRecommendation()
        {
            var fakeGpus = new List<GpuInfo>
            {
                new("Intel(R) UHD Graphics 770", 1024 * 1024 * 1024, false, GpuVendor.Intel, "31.0.101.5333", "2024-03-01"),
                new("NVIDIA GeForce RTX 4080", 16UL * 1024 * 1024 * 1024, true, GpuVendor.Nvidia, "555.85", "2024-05-15")
            };

            var fakeDetector = new FakeHardwareDetector(fakeGpus);
            var settingsStore = new FakeSettingsStore();
            var gpuPrefService = new FakeGpuPreferenceService();
            var libService = new FakeLibraryService();
            var wallpaperService = new WallpaperService(libService, settingsStore, gpuPrefService);

            var coordinator = new UpdateCoordinator(
                new FakeUpdateService(),
                new NoOpDownloadManager(),
                new AlwaysValidSignatureValidator(),
                new NoOpUpdateApplier(),
                new NoOpProcessManager()
            );

            var updaterVm = new UpdaterViewModel(coordinator, new FakeUpdaterSettingsStore());
            var layoutHostVm = new LayoutHostViewModel(new SettingsStoreLayoutPreferenceStore(settingsStore));

            var vm = new SettingsViewModel(wallpaperService, updaterVm, layoutHostVm, settingsStore, fakeDetector);
            await vm.DetectGpusAsync();

            Assert.True(vm.HasDetectedGpus);
            Assert.Equal(2, vm.DetectedGpus.Count);
            Assert.True(vm.IsDualGpuSystem);
            Assert.Contains("Hybrid GPU system detected", vm.DualGpuRecommendation);
        }

        [Fact]
        public async Task WindowsHardwareDetector_LiveSystem_DetectsGpusAndDrivers()
        {
            var detector = new WindowsHardwareDetector();
            var gpus = (await detector.GetGpusAsync()).ToList();

            Assert.NotEmpty(gpus);
            foreach (var gpu in gpus)
            {
                Assert.False(string.IsNullOrWhiteSpace(gpu.Name));
                Assert.False(string.IsNullOrWhiteSpace(gpu.Status));
                Assert.True(gpu.Vendor == GpuVendor.Nvidia || gpu.Vendor == GpuVendor.Intel || gpu.Vendor == GpuVendor.Amd || gpu.Vendor == GpuVendor.Unknown);
            }

            // On local dual-GPU machine, verify dGPU + iGPU pairing
            var dgpu = gpus.FirstOrDefault(g => g.IsDedicated);
            var igpu = gpus.FirstOrDefault(g => !g.IsDedicated);

            if (dgpu != null)
            {
                Assert.Equal("Dedicated GPU (dGPU)", dgpu.TypeLabel);
                Assert.False(string.IsNullOrWhiteSpace(dgpu.DriverVersion));
            }

            if (igpu != null)
            {
                Assert.Equal("Integrated GPU (iGPU)", igpu.TypeLabel);
            }
        }
    }
}
