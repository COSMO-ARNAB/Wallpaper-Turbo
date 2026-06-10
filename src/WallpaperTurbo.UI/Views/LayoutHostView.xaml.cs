using System.Windows.Controls;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.Views;

public partial class LayoutHostView : UserControl
{
    public LayoutHostView()
    {
        StartupDiagnostics.StartTimer("LayoutHostView initialization");
        InitializeComponent();
        StartupDiagnostics.StopTimerWithMemory("LayoutHostView initialization");
    }
}