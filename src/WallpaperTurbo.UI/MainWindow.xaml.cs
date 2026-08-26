using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.Services.Theme;
using WallpaperTurbo.UI.ViewModels;

namespace WallpaperTurbo.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly IWallpaperPreviewService _previewService;
    private readonly DiagnosticsService _diagnostics;
    private readonly PresentationManager _presentation;
    private readonly IDwmApplier _dwmApplier;

    public MainWindow(MainViewModel viewModel, IWallpaperPreviewService previewService, DiagnosticsService diagnostics, PresentationManager presentation)
        : this(viewModel, previewService, diagnostics, presentation, new DwmBackdropApplier())
    {
    }

    public MainWindow(MainViewModel viewModel, IWallpaperPreviewService previewService, DiagnosticsService diagnostics, PresentationManager presentation, IDwmApplier dwmApplier)
    {
        Services.StartupDiagnostics.Log("MainWindow constructor ENTRY");
#if DEBUG
        Console.WriteLine("DEBUG: MainWindow constructor ENTRY");
#endif
        _previewService = previewService;
        _diagnostics = diagnostics;
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _dwmApplier = dwmApplier ?? throw new ArgumentNullException(nameof(dwmApplier));
        _presentation.PropertyChanged += OnPresentationPropertyChanged;

        // Inject and set resolved Viewmodel context
        DataContext = viewModel;

        Services.StartupDiagnostics.StartTimer("MainWindow InitializeComponent");
#if DEBUG
        Console.WriteLine("DEBUG: MainWindow calling InitializeComponent");
#endif
        InitializeComponent();
#if DEBUG
        Console.WriteLine("DEBUG: MainWindow InitializeComponent finished");
#endif
        Services.StartupDiagnostics.StopTimerWithMemory("MainWindow InitializeComponent");

        // Hard safety guard: force-cancel any active preview when window loses focus or minimizes.
        // Prevents ghost preview sessions accumulating while the user is in another app.
        Deactivated  += OnWindowDeactivated;
        Activated    += OnWindowActivated;
        StateChanged += OnWindowStateChanged;
        SourceInitialized += OnSourceInitialized;

        Loaded += OnWindowLoaded;
        ContentRendered += OnWindowContentRendered;

        // H2 cold-start: offload icon decode + CopyPixels + alpha scan off UI thread.
        // Previously 50-250ms synchronous work before Loaded delayed first frame.
        // Now fire-and-forget on thread-pool and marshal Icon assignment back at Background priority.
        _ = Task.Run(TryLoadIconInBackground);

        Services.StartupDiagnostics.LogWithMemory("MainWindow constructor EXIT");
    }

    /// <summary>
    /// H2 cold-start helper: runs fully on thread-pool (invoked via Task.Run in ctor).
    /// Decodes largest ICO frame, converts to BGRA32, copies pixels and scans alpha
    /// to compute minimal opaque bounding box (+2% padding), then freezes the
    /// resulting ImageSource and marshals Icon assignment back at Background priority.
    /// Keeps original try/catch and visual result (cropped icon vs fallback frame).
    /// </summary>
    private void TryLoadIconInBackground()
    {
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
            if (largestFrame == null) return;

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

            System.Windows.Media.ImageSource? iconSource;
            if (minX <= maxX && minY <= maxY)
            {
                int padding = (int)(width * 0.02);
                minX = Math.Max(0, minX - padding);
                minY = Math.Max(0, minY - padding);
                maxX = Math.Min(width - 1, maxX + padding);
                maxY = Math.Min(height - 1, maxY + padding);
                var rect = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
                var cropped = new System.Windows.Media.Imaging.CroppedBitmap(converted, rect);
                cropped.Freeze();
                iconSource = cropped;
            }
            else
            {
                var clone = largestFrame.Clone();
                clone.Freeze();
                iconSource = clone;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { Icon = iconSource; } catch { }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load high-res icon: {ex.Message}");
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Force-stop preview when window loses focus (user switched apps, Alt+Tab, etc.)
        _ = _previewService.StopPreviewAsync();
        System.Diagnostics.Debug.WriteLine("[MainWindow] Window deactivated → preview force-stopped.");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // HWND is guaranteed valid here — apply backdrop as early as possible.
        // This covers the case where PresentationManager fired before Loaded.
        ApplyBackdropAttributes();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // Re-apply after activation: DWM can drop/reset the backdrop after
        // minimize/restore, monitor/DPI changes, or theme transitions.
        // Without this, the window can stay stuck on a grey/fallback frame.
        ApplyBackdropAttributes();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // Force-stop preview when minimized to release decoder and compositor resources
            _ = _previewService.StopPreviewAsync();
            System.Diagnostics.Debug.WriteLine("[MainWindow] Window minimized → preview force-stopped.");
        }
        else
        {
            // Restored from minimized/maximized toggle — DWM may have reset
            // DWMWA_SYSTEMBACKDROP_TYPE to Auto. Re-apply without changing colors.
            ApplyBackdropAttributes();
        }
    }

    private void OnWindowContentRendered(object? sender, EventArgs e)
    {
        Services.StartupDiagnostics.LogWithMemory("MainWindow.ContentRendered event");
    }

    private void OnPresentationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PresentationManager.BackdropMode))
        {
            UpdateSystemBackdrop();
        }
    }

    private void UpdateSystemBackdrop()
    {
        // Thin wrapper kept for the PresentationManager.PropertyChanged path.
        // Delegates to the single authoritative helper so ordering/HRESULT handling is consistent.
        ApplyBackdropAttributes();
    }

    /// <summary>
    /// Delegates to IDwmApplier but retains hwnd==0 Dispatcher deferral in MainWindow.
    /// The applier itself is dumb (pure P/Invoke, early-returns on 0) so deferral must live here
    /// to cover the case where PresentationManager fires before SourceInitialized.
    /// Keeps SourceInitialized/Activated/StateChanged re-apply hooks for DWM reset recovery.
    /// </summary>
    private void ApplyBackdropAttributes()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                // Handle not yet created (e.g., PropertyChanged fired between
                // MainWindow construction and SourceInitialized). Defer exactly once.
                Dispatcher.BeginInvoke(new Action(() => ApplyBackdropAttributes()),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            _dwmApplier.Apply(hwnd, _presentation.BackdropMode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply backdrop attributes: {ex.Message}");
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Services.StartupDiagnostics.LogWithMemory("MainWindow.Loaded event");
        Services.StartupDiagnostics.StartHeartbeat(Dispatcher);
        ApplyBackdropAttributes();
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

    private bool _isShuttingDown = false;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        e.Cancel = true; // Block initial closing to allow background tasks to complete safely
        _isShuttingDown = true;
        this.Hide();     // Instantly hide the window to maintain fluid responsive visual UX

        if (DataContext is MainViewModel viewModel)
        {
            _ = viewModel.ShutdownAsync().ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during safe shutdown: {t.Exception.GetBaseException().Message}");
                }

                Dispatcher.Invoke(() => this.Close());
            }, TaskScheduler.Default);
            return;
        }

        Dispatcher.Invoke(() => this.Close());
    }
}
