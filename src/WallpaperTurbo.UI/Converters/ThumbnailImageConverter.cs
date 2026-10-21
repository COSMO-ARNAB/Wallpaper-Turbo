using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace WallpaperTurbo.UI.Converters;

/// <summary>
/// A value converter that loads a dynamic thumbnail from a string path.
/// Decodes card thumbnails at DecodePixelWidth = 360 (or 1280 for Hero banners)
/// with BitmapCacheOption.OnLoad and freezes them in a bounded cache (MaxCachedThumbnails = 64)
/// to prevent 33 MB full-4K RGBA allocations per card.
/// </summary>
public class ThumbnailImageConverter : IValueConverter
{
    public const int OptimalCardDecodePixelWidth = 360;
    public const int MaxCachedThumbnails = 64;

    private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCacheIfExceeded()
    {
        if (_cache.Count > MaxCachedThumbnails)
        {
            _cache.Clear();
        }
    }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            int decodeWidth = OptimalCardDecodePixelWidth;
            if (parameter is string paramStr && paramStr.Equals("Hero", StringComparison.OrdinalIgnoreCase))
            {
                decodeWidth = 1280; // High resolution for Hero banners to keep them crisp
            }

            string cacheKey = $"{path}:{decodeWidth}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            try
            {
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
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Freeze to make thread-safe and virtualization-friendly

                if (_cache.Count >= MaxCachedThumbnails)
                {
                    _cache.Clear();
                }
                _cache.TryAdd(cacheKey, bitmap);

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Converter Error] Failed to load thumbnail '{path}': {ex.Message}");
            }
        }

        return LoadFallback(OptimalCardDecodePixelWidth);
    }

    private static BitmapImage? LoadFallback(int decodeWidth)
    {
        string fallbackKey = $"__fallback__:{decodeWidth}";
        if (_cache.TryGetValue(fallbackKey, out var cachedFallback))
        {
            return cachedFallback;
        }

        try
        {
            var fallback = new BitmapImage();
            fallback.BeginInit();
            fallback.UriSource = new Uri("pack://application:,,,/Assets/Branding/wallpaper-turbo.ico", UriKind.Absolute);
            fallback.DecodePixelWidth = decodeWidth;
            fallback.CacheOption = BitmapCacheOption.OnLoad;
            fallback.EndInit();
            fallback.Freeze();

            _cache.TryAdd(fallbackKey, fallback);
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
