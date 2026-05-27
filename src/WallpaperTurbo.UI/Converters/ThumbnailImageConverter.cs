using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace WallpaperTurbo.UI.Converters;

/// <summary>
/// A value converter that loads a dynamic thumbnail from a string path.
/// Decodes at a limited pixel width (320 for cards, 1280 for hero banners) 
/// and freezes the bitmap to make it thread-safe and virtualization-friendly.
/// 
/// IMPORTANT: Uses BitmapCacheOption.OnLoad (NOT OnDemand) because Freeze() 
/// forces synchronous decode anyway — OnDemand + Freeze is a contradiction that
/// causes random stalls when WPF tries to finalize the freeze. OnLoad is explicit
/// and predictable.
/// </summary>
public class ThumbnailImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                int decodeWidth = 320;
                if (parameter is string paramStr && paramStr.Equals("Hero", StringComparison.OrdinalIgnoreCase))
                {
                    decodeWidth = 1280; // High resolution for Hero banners to keep them crisp
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                
                // Map pack URIs or absolute filesystem paths correctly
                if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                {
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                }
                else
                {
                    // Validate file exists before attempting to load to avoid exceptions on deleted thumbnails
                    string fullPath = Path.GetFullPath(path);
                    if (!File.Exists(fullPath))
                    {
                        return LoadFallback(decodeWidth);
                    }
                    bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                }
                
                bitmap.DecodePixelWidth = decodeWidth;
                
                // Use OnLoad (NOT OnDemand): Freeze() forces a synchronous decode regardless of the cache option,
                // so OnDemand + Freeze is contradictory and causes unpredictable stalls. OnLoad is explicit.
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                
                bitmap.EndInit();
                bitmap.Freeze(); // Freeze to make thread-safe and virtualization-friendly
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Converter Error] Failed to load thumbnail '{path}': {ex.Message}");
            }
        }

        return LoadFallback(320);
    }

    private static BitmapImage? LoadFallback(int decodeWidth)
    {
        try
        {
            var fallback = new BitmapImage();
            fallback.BeginInit();
            fallback.UriSource = new Uri("pack://application:,,,/Assets/Branding/wallpaper-turbo.ico", UriKind.Absolute);
            fallback.DecodePixelWidth = decodeWidth;
            fallback.CacheOption = BitmapCacheOption.OnLoad;
            fallback.EndInit();
            fallback.Freeze();
            return fallback;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
