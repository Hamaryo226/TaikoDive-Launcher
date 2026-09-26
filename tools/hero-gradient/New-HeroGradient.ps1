Add-Type -AssemblyName System.Drawing.Common

$source = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class HeroGradientGenerator
{
    public static void Save(string path, double phase, string palette)
    {
        const int width = 1400;
        const int height = 560;
        using Bitmap bitmap = new(width, height, PixelFormat.Format24bppRgb);
        Rectangle bounds = new(0, 0, width, height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        byte[] pixels = new byte[data.Stride * height];
        double lowerX = palette == "Sunset" ? 0.34 : palette == "Violet" ? 0.18 : 0.10;
        double lowerY = palette == "Violet" ? 1.04 : 1.13;
        double upperX = palette == "Sunset" ? 1.03 : palette == "Violet" ? 0.70 : 0.88;
        double upperY = palette == "Sunset" ? 0.03 : -0.12;
        double middleX = palette == "Violet" ? 0.44 : 0.62;
        double middleY = palette == "Violet" ? 0.56 : 0.88;

        for (int y = 0; y < height; y++)
        {
            double v = (double)y / height;
            for (int x = 0; x < width; x++)
            {
                double u = (double)x / width;
                double lower = Glow(u, v, lowerX + phase * 0.08, lowerY,
                    palette == "Sunset" ? 0.62 : 0.38, 0.66);
                double upper = Glow(u, v, upperX - phase * 0.09, upperY,
                    palette == "Violet" ? 0.28 : 0.34, 0.58);
                double middle = Glow(u, v, middleX + phase * 0.04, middleY,
                    palette == "Sunset" ? 0.38 : 0.24, palette == "Violet" ? 0.70 : 0.54);
                double wave = 0.86 + 0.14 * Math.Sin(u * 15.0 + v * 9.0 + phase * 2.0);
                double cyan = Math.Min(1.0, (lower * 0.92 + upper * 0.93 + middle * 0.32) * wave);
                double blue = Math.Min(1.0, lower * 0.51 + upper * 0.68 + middle * 0.62);
                double grain = (Hash(x, y) - 0.5) * 9.0;
                int offset = y * data.Stride + x * 3;
                if (palette == "Violet")
                {
                    pixels[offset] = Channel(38 + 115 * blue + 89 * cyan + grain);
                    pixels[offset + 1] = Channel(11 + 46 * blue + 48 * cyan + grain);
                    pixels[offset + 2] = Channel(14 + 96 * blue + 93 * cyan + grain);
                }
                else if (palette == "Sunset")
                {
                    pixels[offset] = Channel(22 + 58 * blue + 38 * cyan + grain);
                    pixels[offset + 1] = Channel(13 + 70 * blue + 62 * cyan + grain);
                    pixels[offset + 2] = Channel(25 + 121 * blue + 119 * cyan + grain);
                }
                else
                {
                    pixels[offset] = Channel(29 + 138 * blue + 62 * cyan + grain);
                    pixels[offset + 1] = Channel(15 + 82 * blue + 91 * cyan + grain);
                    pixels[offset + 2] = Channel(5 + 9 * blue + 23 * cyan + grain);
                }
            }
        }

        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
        ImageCodecInfo jpeg = Array.Find(ImageCodecInfo.GetImageEncoders(), codec => codec.MimeType == "image/jpeg");
        using EncoderParameters options = new(1);
        options.Param[0] = new EncoderParameter(Encoder.Quality, 87L);
        bitmap.Save(path, jpeg, options);
    }

    private static double Glow(double x, double y, double centerX, double centerY, double radiusX, double radiusY)
    {
        double dx = (x - centerX) / radiusX;
        double dy = (y - centerY) / radiusY;
        return Math.Exp(-1.6 * (dx * dx + dy * dy));
    }

    private static double Hash(int x, int y)
    {
        uint value = (uint)(x * 73856093) ^ (uint)(y * 19349663);
        value ^= value >> 13;
        value *= 1274126177;
        return (value & 65535) / 65535.0;
    }

    private static byte Channel(double value) => (byte)Math.Clamp((int)value, 0, 255);
}
'@

Add-Type -TypeDefinition $source -ReferencedAssemblies @(
    [System.Drawing.Bitmap].Assembly.Location,
    [System.Drawing.Size].Assembly.Location
)

$assets = Join-Path $PSScriptRoot '..\..\Assets'
foreach ($style in @('Ocean', 'Violet', 'Sunset')) {
    $name = if ($style -eq 'Ocean') { 'HeroGradient' } else { "HeroGradient$style" }
    [HeroGradientGenerator]::Save((Join-Path $assets "$($name)A.jpg"), 0.0, $style)
    [HeroGradientGenerator]::Save((Join-Path $assets "$($name)B.jpg"), 1.0, $style)
}
