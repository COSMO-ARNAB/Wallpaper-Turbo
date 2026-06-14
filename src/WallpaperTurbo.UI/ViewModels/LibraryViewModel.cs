using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly WallpaperService _wallpaperService;
    private List<WallpaperEntry> _allWallpapers = new();

    public ObservableCollection<WallpaperEntry> FilteredWallpapers { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    public List<string> Categories { get; } = new()
    {
        "All", "4K", "Ultrawide", "Dual Monitor", "Abstract", "Nature", "Sci-Fi", "Ambient"
    };

    public LibraryViewModel(WallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService;
        _ = LoadLibraryAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryViewModel] LoadLibraryAsync failed: {t.Exception?.GetBaseException().Message}");
            }
        }, TaskScheduler.Default);
    }

    public async Task LoadLibraryAsync()
    {
        _allWallpapers = await _wallpaperService.GetWallpapersAsync();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
    }

    private void ApplyFilter()
    {
        var query = _allWallpapers.AsEnumerable();

        // 1. Search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string searchLower = SearchText.ToLowerInvariant();
            query = query.Where(w => w.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                                     w.Author.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                                     w.Tags.Any(t => t.Contains(searchLower, StringComparison.OrdinalIgnoreCase)));
        }

        // 2. Category filter
        if (!string.Equals(SelectedCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            string categoryLower = SelectedCategory.ToLowerInvariant();
            if (categoryLower == "4k")
            {
                query = query.Where(w => w.Resolution.Contains("3840"));
            }
            else if (categoryLower == "ultrawide")
            {
                query = query.Where(w => w.Resolution.Contains("3440"));
            }
            else
            {
                query = query.Where(w => w.Tags.Any(t => t.Equals(categoryLower, StringComparison.OrdinalIgnoreCase)));
            }
        }

        var targetList = query.ToList();

        // 3. Incremental synchronization to avoid list-level resetting
        // Remove items no longer present
        for (int i = FilteredWallpapers.Count - 1; i >= 0; i--)
        {
            if (!targetList.Contains(FilteredWallpapers[i]))
            {
                FilteredWallpapers.RemoveAt(i);
            }
        }

        // Add or move items to match targetList exactly in order
        for (int i = 0; i < targetList.Count; i++)
        {
            var targetItem = targetList[i];
            int currentIndex = FilteredWallpapers.IndexOf(targetItem);
            if (currentIndex == -1)
            {
                FilteredWallpapers.Insert(i, targetItem);
            }
            else if (currentIndex != i)
            {
                FilteredWallpapers.Move(currentIndex, i);
            }
        }
    }

    [RelayCommand]
    private async Task PlayWallpaperAsync(WallpaperEntry? wp)
    {
        if (wp == null) return;
        int index = _allWallpapers.IndexOf(wp) + 1;
        if (index > 0)
        {
            // Launch via background detached model
            await _wallpaperService.LaunchWallpaperAsync(index);

            // Update main status details
            var mainVm = App.GetService<MainViewModel>();
            mainVm.UpdateEngineStatus();
            mainVm.SetActiveWallpaperInfo(wp.Title, $"{wp.Resolution} • {wp.Fps}");
        }
    }

    [RelayCommand]
    private async Task TripleClickPlayWallpaperAsync(WallpaperEntry? wp)
    {
        if (wp == null) return;
        int index = _allWallpapers.IndexOf(wp) + 1;
        if (index > 0)
        {
            // 1. Force close any previous wallpaper completely first
            var mainVm = App.GetService<MainViewModel>();
            if (mainVm.IsEngineRunning)
            {
                await _wallpaperService.StopPlaybackAsync();
                await Task.Delay(500); // Give it a short delay for OS process cleanup
            }

            // 2. Play the new wallpaper fresh from scratch (forcing fresh process)
            await _wallpaperService.LaunchWallpaperAsync(index, forceFreshLaunch: true);

            // 3. Update main status details
            mainVm.UpdateEngineStatus();
            mainVm.SetActiveWallpaperInfo(wp.Title, $"{wp.Resolution} • {wp.Fps}");
        }
    }

    [RelayCommand]
    private async Task ImportWallpaperAsync()
    {
        var mainVm = App.GetService<MainViewModel>();
        await mainVm.ImportWallpaperCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task DeleteWallpaperAsync(WallpaperEntry? wp)
    {
        if (wp == null) return;

        var mainVm = App.GetService<MainViewModel>();
        mainVm.DialogTitle = "Confirm Delete";
        mainVm.DialogMessage = $"Are you sure you want to permanently delete the wallpaper '{wp.Title}'?\nThis will remove it from your library and delete its local copy on disk.";
        mainVm.IsDialogCancelVisible = true;
        
        mainVm.DialogConfirmCommand = new AsyncRelayCommand(async () =>
        {
            mainVm.IsDialogVisible = false;
            bool success = await _wallpaperService.DeleteWallpaperAsync(wp);
            if (success)
            {
                // Refresh list cache
                _allWallpapers.Remove(wp);
                ApplyFilter();

                // If deleted wallpaper was active, reset active wallpaper details in MainViewModel
                mainVm.UpdateEngineStatus();
                if (mainVm.ActiveWallpaperTitle == wp.Title)
                {
                    mainVm.SetActiveWallpaperInfo("No Active Wallpaper", "None");
                }
            }
            else
            {
                mainVm.DialogTitle = "Delete Failed";
                mainVm.DialogMessage = "Failed to delete the wallpaper. It might be locked or already deleted.";
                mainVm.IsDialogCancelVisible = false;
                mainVm.DialogConfirmCommand = new RelayCommand(() => mainVm.IsDialogVisible = false);
                mainVm.IsDialogVisible = true;
            }
        });
        
        mainVm.DialogCancelCommand = new RelayCommand(() => mainVm.IsDialogVisible = false);
        mainVm.IsDialogVisible = true;
        await Task.CompletedTask;
    }
}
