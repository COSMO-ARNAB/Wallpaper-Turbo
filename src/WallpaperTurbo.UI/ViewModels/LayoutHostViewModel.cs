using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperTurbo.UI.Models;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.ViewModels;

public partial class LayoutHostViewModel : ObservableObject
{
    private readonly ILayoutPreferenceStore _layoutPreferenceStore;

    [ObservableProperty]
    private LayoutMode _currentLayout;

    public IReadOnlyList<LayoutMode> Layouts { get; } =
        new[] { LayoutMode.Minimal, LayoutMode.Techie };

    public LayoutHostViewModel(ILayoutPreferenceStore layoutPreferenceStore)
    {
        _layoutPreferenceStore = layoutPreferenceStore ?? throw new ArgumentNullException(nameof(layoutPreferenceStore));
        CurrentLayout = _layoutPreferenceStore.GetSavedLayout();
    }

    [RelayCommand]
    public void SwitchLayout(LayoutMode layoutMode)
    {
        if (CurrentLayout == layoutMode)
            return;

        CurrentLayout = layoutMode;
        _layoutPreferenceStore.SaveLayout(layoutMode);
    }
}