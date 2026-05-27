using System;
using System.Drawing;

class Program
{
    static void Main()
    {
        string path = @"c:\Users\arnab\PROJECTS\Wallpaper Turbo\src\WallpaperTurbo.UI\Assets\Branding\wallpaper-turbo.png";
        using (Bitmap bmp = new Bitmap(path))
        {
            int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A > 10)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            Console.WriteLine($"Original Size: {bmp.Width}x{bmp.Height}");
            Console.WriteLine($"Content Bounding Box: {minX},{minY} to {maxX},{maxY}");
            Console.WriteLine($"Content Size: {maxX - minX}x{maxY - minY}");
        }
    }
}
