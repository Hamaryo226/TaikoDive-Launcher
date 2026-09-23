using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Text.Json;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Controls;

internal sealed record NamePlateTextBitmap(byte[] Png, int Width, int Height, double ScaleX, double ScaleY);

[SupportedOSPlatform("windows")]
internal static class NamePlateTextRenderer
{
    // TaikoDive の NamePlateFont と ApplyNamePlateFontAndScale に合わせる。
    internal static (NamePlateTextBitmap Name, NamePlateTextBitmap Title) Render(
        TaikoDiveInstallation installation, string name, string title)
    {
        string oedo = "FOT-大江戸勘亭流 Std E";
        string domCasual = "Dom Casual";
        string fallback = "Comic Sans MS";
        if (File.Exists(installation.GameSettingsPath))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(installation.GameSettingsPath));
            JsonElement root = document.RootElement;
            oedo = Read(root, "fontOedo", oedo);
            domCasual = Read(root, "fontDomCasual", domCasual);
            fallback = Read(root, "fontFallback", fallback);
        }

        bool asciiName = !string.IsNullOrEmpty(name) && name.All(c => c is >= (char)32 and <= (char)126);
        using FontFamily nameFamily = Resolve(asciiName ? domCasual : oedo, fallback);
        using FontFamily titleFamily = Resolve(oedo, fallback);
        NamePlateTextBitmap nameBitmap = RenderText(name, nameFamily, 18, false);
        NamePlateTextBitmap titleBitmap = RenderText(title, titleFamily, 16, true);
        double titleScale = Math.Min(1, 245d / titleBitmap.Width);
        return (nameBitmap with { ScaleX = Math.Min(1, 188d / nameBitmap.Width) },
                titleBitmap with { ScaleX = titleScale, ScaleY = titleScale });
    }

    private static string Read(JsonElement root, string key, string fallback) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private static FontFamily Resolve(string name, string fallback)
    {
        try { return new FontFamily(name); }
        catch { return new FontFamily(fallback); }
    }

    private static NamePlateTextBitmap RenderText(string text, FontFamily family, int fontSize, bool title)
    {
        float pixelSize = fontSize * 96f / 72f;
        using Font font = new(family, pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using Bitmap measuringBitmap = new(16, 16);
        using Graphics measuring = Graphics.FromImage(measuringBitmap);
        SizeF first = measuring.MeasureString(text, font);
        SizeF size = measuring.MeasureString(text, font, Math.Max(1, (int)first.Width), StringFormat.GenericTypographic);
        if (size.Width == 0 || size.Height == 0) size = new SizeF(16, 16);
        int width = Math.Max(1, (int)Math.Ceiling(size.Width + 6));
        int height = Math.Max(1, (int)Math.Ceiling(size.Height + 6));
        using Bitmap bitmap = new(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        if (!string.IsNullOrEmpty(text))
        {
            using StringFormat format = new(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None,
            };
            using GraphicsPath path = new();
            path.AddString(text, family, (int)FontStyle.Regular, pixelSize, title ? new Point(0, 0) : new Point(3, 3), format);
            if (!title)
            {
                using Pen edge = new(Color.Black, 6) { LineJoin = LineJoin.Round };
                graphics.DrawPath(edge, path);
            }
            using Brush fill = new SolidBrush(title ? Color.Black : Color.FromArgb(250, 250, 250));
            graphics.FillPath(fill, path);
        }
        using MemoryStream stream = new();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return new NamePlateTextBitmap(stream.ToArray(), width, height, 1, 1);
    }

    internal static (byte[] Background, byte[] Number) RenderBaseLayers(string path)
    {
        using Bitmap sheet = new(path);
        return (Crop(208), Crop(0));

        byte[] Crop(int sourceY)
        {
            using Bitmap frame = new(326, 104);
            using Graphics graphics = Graphics.FromImage(frame);
            graphics.DrawImage(sheet, new Rectangle(0, 0, 326, 104),
                new Rectangle(0, sourceY, 326, 104), GraphicsUnit.Pixel);
            using MemoryStream stream = new();
            frame.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
    }
}
