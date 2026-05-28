using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly IWallpaperPreviewService _previewService;
    private readonly DiagnosticsService _diagnostics;

    public MainWindow(MainViewModel viewModel, IWallpaperPreviewService previewService, DiagnosticsService diagnostics)
    {
        _previewService = previewService;
        _diagnostics = diagnostics;

        // Inject and set resolved Viewmodel context
        DataContext = viewModel;

        InitializeComponent();

        // Hard safety guard: force-cancel any active preview when window loses focus or minimizes.
        // Prevents ghost preview sessions accumulating while the user is in another app.
        Deactivated  += OnWindowDeactivated;
        StateChanged += OnWindowStateChanged;

        // Load the highest quality frame from the .ico file for the Taskbar Icon and crop any transparent padding
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/Branding/wallpaper-turbo.ico", UriKind.Absolute);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(uri, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.Default);
            System.Windows.Media.Imaging.BitmapFrame? largestFrame = null;
            int maxArea = 0;
            foreach (var frame in decoder.Frames)
            {
                int area = frame.PixelWidth * frame.PixelHeight;
                if (area > maxArea)
                {
                    maxArea = area;
                    largestFrame = frame;
                }
            }
            if (largestFrame != null)
            {
                // Ensure the frame is 32-bit BGRA so we can safely read the alpha channel
                var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(largestFrame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                converted.CopyPixels(pixels, stride, 0);

                int minX = width, minY = height, maxX = 0, maxY = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte alpha = pixels[(y * stride) + (x * 4) + 3]; // BGRA
                        if (alpha > 10)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }

                if (minX <= maxX && minY <= maxY)
                {
                    // Add a small 2% padding around the cropped bounds so it doesn't hit the absolute edges
                    int padding = (int)(width * 0.02);
                    minX = Math.Max(0, minX - padding);
                    minY = Math.Max(0, minY - padding);
                    maxX = Math.Min(width - 1, maxX + padding);
                    maxY = Math.Min(height - 1, maxY + padding);

                    var rect = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
                    var cropped = new System.Windows.Media.Imaging.CroppedBitmap(converted, rect);
                    this.Icon = cropped;
                }
                else
                {
                    this.Icon = largestFrame;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load high-res icon: {ex.Message}");
        }

        Loaded += OnWindowLoaded;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Force-stop preview when window loses focus (user switched apps, Alt+Tab, etc.)
        _ = _previewService.StopPreviewAsync();
        System.Diagnostics.Debug.WriteLine("[MainWindow] Window deactivated → preview force-stopped.");
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // Force-stop preview when minimized to release decoder and compositor resources
            _ = _previewService.StopPreviewAsync();
            System.Diagnostics.Debug.WriteLine("[MainWindow] Window minimized → preview force-stopped.");
        }
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

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                this.DragMove();
            }
        }
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true; // Block initial closing to allow background tasks to complete safely
        this.Hide();     // Instantly hide the window to maintain fluid responsive visual UX

        try
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.ShutdownAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during safe shutdown: {ex.Message}");
        }

        Application.Current.Shutdown(); // Clean process exit
    }
}