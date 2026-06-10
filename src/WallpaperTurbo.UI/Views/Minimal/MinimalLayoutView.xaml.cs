using System.Windows.Controls;
using WallpaperTurbo.UI.Services;

namespace WallpaperTurbo.UI.Views.Minimal
{
    public partial class MinimalLayoutView : UserControl
    {
        public MinimalLayoutView()
        {
            StartupDiagnostics.StartTimer("MinimalLayoutView initialization");
            InitializeComponent();
            StartupDiagnostics.StopTimerWithMemory("MinimalLayoutView initialization");
        }
    }
}