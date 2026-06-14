using System;
using System.Globalization;
using System.Windows.Data;

namespace WallpaperTurbo.UI.Converters;

/// <summary>
/// Returns the boolean inverse of the bound value.
/// Used to disable a control while an async operation is in progress
/// (e.g., disabling the GPU ComboBox while IsGpuSwitching = true).
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
