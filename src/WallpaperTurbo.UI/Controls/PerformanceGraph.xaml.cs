using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WallpaperTurbo.UI.Controls;

public partial class PerformanceGraph : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(PerformanceGraph), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(PerformanceGraph), new PropertyMetadata(Brushes.Cyan));

    public static readonly DependencyProperty GlowColorProperty = DependencyProperty.Register(
        nameof(GlowColor), typeof(Color), typeof(PerformanceGraph), new PropertyMetadata(Colors.Cyan));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    private readonly List<double> _history = new();
    private readonly List<double> _displayHistory = new();
    private const int MaxPoints = 25;
    private bool _isRenderingHooked = false;

    public PerformanceGraph()
    {
        InitializeComponent();

        // Populate initial flat line so the graph displays nicely on startup
        for (int i = 0; i < MaxPoints; i++)
        {
            _history.Add(0.0);
            _displayHistory.Add(0.0);
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (s, e) => Redraw();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartInterpolationLoop();
        Redraw();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopInterpolationLoop();
    }

    private void StartInterpolationLoop()
    {
        if (!_isRenderingHooked)
        {
            CompositionTarget.Rendering += OnCompositionTargetRendering;
            _isRenderingHooked = true;
        }
    }

    private void StopInterpolationLoop()
    {
        if (_isRenderingHooked)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            _isRenderingHooked = false;
        }
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        // Smoothly interpolate display points toward target history points
        bool needsRedraw = false;
        for (int i = 0; i < MaxPoints; i++)
        {
            double target = _history[i];
            double current = _displayHistory[i];
            double delta = target - current;

            if (Math.Abs(delta) > 0.01)
            {
                // Exponential moving average for fluid organic ease-out motion (10% step per frame)
                _displayHistory[i] = current + (delta * 0.1);
                needsRedraw = true;
            }
            else
            {
                _displayHistory[i] = target;
            }
        }

        if (needsRedraw)
        {
            Redraw();
        }
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PerformanceGraph graph)
        {
            graph.AddValue((double)e.NewValue);
        }
    }

    private void AddValue(double val)
    {
        _history.Add(val);
        if (_history.Count > MaxPoints)
        {
            _history.RemoveAt(0);
        }

        // Maintain display history sizing matching actual history
        while (_displayHistory.Count < _history.Count)
        {
            _displayHistory.Add(val);
        }
        if (_displayHistory.Count > MaxPoints)
        {
            _displayHistory.RemoveAt(0);
        }
        
        // Ensure interpolation loop runs when values are actively updated
        StartInterpolationLoop();
    }

    private void Redraw()
    {
        if (WaveLine == null || ActualWidth <= 0 || ActualHeight <= 0) return;

        var points = new PointCollection();
        double w = ActualWidth;
        double h = ActualHeight;

        double stepX = w / (MaxPoints - 1);

        for (int i = 0; i < _displayHistory.Count; i++)
        {
            double x = i * stepX;
            // Map 0-100% to h-0 height bounds safely
            double valueNormalized = Math.Clamp(_displayHistory[i] / 100.0, 0.0, 1.0);
            double y = h - (valueNormalized * (h - 4.0)) - 2.0; // pad slightly to avoid edge cuts

            points.Add(new Point(x, y));
        }

        WaveLine.Points = points;
    }
}
