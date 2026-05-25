using System.Windows;
using System.Windows.Controls;

namespace WallpaperTurbo.UI.Controls;

public partial class TelemetryRing : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TelemetryRing), new PropertyMetadata(0.0));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TelemetryRing), new PropertyMetadata(string.Empty));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public TelemetryRing()
    {
        InitializeComponent();
    }
}
