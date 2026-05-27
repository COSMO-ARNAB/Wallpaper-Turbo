using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WallpaperTurbo.UI.Controls;

public partial class TelemetryRing : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TelemetryRing), new PropertyMetadata(0.0, OnValueChanged));

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

    private double _targetProgress = 0.0;
    private double _currentProgress = 0.0;
    private bool _isRenderingHooked = false;
    private DateTime _lastTickTime = DateTime.MinValue;

    public TelemetryRing()
    {
        InitializeComponent();
        Loaded += (s, e) => HookRendering();
        Unloaded += (s, e) => UnhookRendering();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TelemetryRing ring)
        {
            ring._targetProgress = (double)e.NewValue;
            ring.HookRendering();
        }
    }

    private void HookRendering()
    {
        if (!_isRenderingHooked && IsLoaded)
        {
            _lastTickTime = DateTime.UtcNow;
            CompositionTarget.Rendering += OnCompositionTargetRendering;
            _isRenderingHooked = true;
        }
    }

    private void UnhookRendering()
    {
        if (_isRenderingHooked)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            _isRenderingHooked = false;
        }
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_lastTickTime == DateTime.MinValue)
        {
            _lastTickTime = now;
            return;
        }

        double dt = (now - _lastTickTime).TotalSeconds;
        _lastTickTime = now;

        // Cap dt to avoid massive jumps during thread pauses
        dt = Math.Min(dt, 0.1);

        double delta = _targetProgress - _currentProgress;
        if (Math.Abs(delta) > 0.05)
        {
            // Frame-rate independent exponential decay: current + (delta * (1.0 - Exp(-k * dt)))
            double k = 8.0; // Decay speed factor
            _currentProgress += delta * (1.0 - Math.Exp(-k * dt));
            Ring.Progress = _currentProgress;
        }
        else
        {
            _currentProgress = _targetProgress;
            Ring.Progress = _currentProgress;
            UnhookRendering(); // Pause rendering loop when converged to achieve absolute zero background CPU overhead!
        }
    }
}
