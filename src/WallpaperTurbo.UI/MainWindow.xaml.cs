using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow(MainViewModel viewModel)
    {
        // Inject and set resolved Viewmodel context
        DataContext = viewModel;

        InitializeComponent();

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            
            // Apply Mica backdrop (DWMWA_SYSTEMBACKDROP_TYPE = 38, Value = 2 for Mica)
            int backdropType = 2;
            DwmSetWindowAttribute(hwnd, 38, ref backdropType, sizeof(int));
            
            // Enable Immersive Dark Mode for Win11 titlebar (DWMWA_USE_IMMERSIVE_DARK_MODE = 20, Value = 1)
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply Mica backdrop: {ex.Message}");
        }
    }
}