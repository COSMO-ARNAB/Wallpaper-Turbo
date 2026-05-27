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
        _ = LoadLibraryAsync();
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
        FilteredWallpapers.Clear();

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

        foreach (var wp in query)
        {
            FilteredWallpapers.Add(wp);
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
    private async Task ImportWallpaperAsync()
    {
        var mainVm = App.GetService<MainViewModel>();
        await mainVm.ImportWallpaperCommand.ExecuteAsync(null);
    }
}
