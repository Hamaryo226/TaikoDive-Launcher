using System.Globalization;

namespace TaikoDiveLauncher.Services;

public sealed class Aup2Animation
{
    private Aup2Animation(int width, int height, double frameRate, int totalFrames, IReadOnlyList<Aup2Visual> visuals)
    {
        Width = width;
        Height = height;
        FrameRate = frameRate;
        TotalFrames = totalFrames;
        Visuals = visuals;
    }

    public int Width { get; }
    public int Height { get; }
    public double FrameRate { get; }
    public int TotalFrames { get; }
    public IReadOnlyList<Aup2Visual> Visuals { get; }

    public static Aup2Animation? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        Dictionary<int, Aup2Visual> objects = [];
        string section = string.Empty;
        string effect = string.Empty;
        int width = 400;
        int height = 100;
        double frameRate = 60;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                effect = string.Empty;
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator];
            string value = line[(separator + 1)..];
            if (section.StartsWith("scene.", StringComparison.OrdinalIgnoreCase))
            {
                if (key == "video.width")
                {
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
                }
                else if (key == "video.height")
                {
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
                }
                else if (key == "video.rate")
                {
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out frameRate);
                }

                continue;
            }

            int dot = section.IndexOf('.');
            string objectSection = dot < 0 ? section : section[..dot];
            if (!int.TryParse(objectSection, NumberStyles.Integer, CultureInfo.InvariantCulture, out int objectId))
            {
                continue;
            }

            if (!objects.TryGetValue(objectId, out Aup2Visual? visual))
            {
                visual = new Aup2Visual();
                objects.Add(objectId, visual);
            }

            if (dot < 0)
            {
                if (key == "layer")
                {
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out visual.Layer);
                }
                else if (key == "frame")
                {
                    string[] frames = value.Split(',');
                    if (frames.Length >= 2)
                    {
                        int.TryParse(frames[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out visual.StartFrame);
                        int.TryParse(frames[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out visual.EndFrame);
                    }
                }

                continue;
            }

            if (key == "effect.name")
            {
                effect = value.Trim();
                continue;
            }

            if (effect == "画像ファイル" && key == "ファイル")
            {
                string fileName = Path.GetFileName(value.Replace('\\', '/'));
                visual.ImagePath = Path.Combine(Path.GetDirectoryName(path)!, fileName);
            }
            else if (effect == "標準描画")
            {
                AssignStandardValue(visual, key, value);
            }
            else if (effect == "拡大率")
            {
                AssignScaleValue(visual, key, value);
            }
        }

        List<Aup2Visual> visuals = objects.Values
            .Where(item => !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
            .OrderBy(item => item.Layer)
            .ToList();
        if (visuals.Count == 0)
        {
            return null;
        }

        int totalFrames = Math.Max(1, objects.Values.Max(item => item.EndFrame + 1));
        return new Aup2Animation(
            Math.Max(1, width),
            Math.Max(1, height),
            frameRate > 0 ? frameRate : 60,
            totalFrames,
            visuals);
    }

    private static void AssignStandardValue(Aup2Visual visual, string key, string value)
    {
        switch (key)
        {
            case "X": visual.X = Aup2Value.Parse(value, 0); break;
            case "Y": visual.Y = Aup2Value.Parse(value, 0); break;
            case "拡大率": visual.Scale = Aup2Value.Parse(value, 100); break;
            case "縦横比": visual.Aspect = Aup2Value.Parse(value, 0); break;
            case "中心X": visual.CenterX = Aup2Value.Parse(value, 0); break;
            case "中心Y": visual.CenterY = Aup2Value.Parse(value, 0); break;
            case "Z軸回転": visual.Rotation = Aup2Value.Parse(value, 0); break;
            case "透明度": visual.Transparency = Aup2Value.Parse(value, 0); break;
            case "合成モード": visual.BlendMode = Aup2BlendModeExtensions.Parse(value); break;
        }
    }

    private static void AssignScaleValue(Aup2Visual visual, string key, string value)
    {
        switch (key)
        {
            case "拡大率": visual.EffectScale = Aup2Value.Parse(value, 100); break;
            case "X": visual.EffectScaleX = Aup2Value.Parse(value, 100); break;
            case "Y": visual.EffectScaleY = Aup2Value.Parse(value, 100); break;
        }
    }
}

public sealed class Aup2Visual
{
    public int Layer;
    public int StartFrame;
    public int EndFrame;
    public string ImagePath = string.Empty;
    public Aup2Value X = Aup2Value.Constant(0);
    public Aup2Value Y = Aup2Value.Constant(0);
    public Aup2Value Scale = Aup2Value.Constant(100);
    public Aup2Value Aspect = Aup2Value.Constant(0);
    public Aup2Value CenterX = Aup2Value.Constant(0);
    public Aup2Value CenterY = Aup2Value.Constant(0);
    public Aup2Value Rotation = Aup2Value.Constant(0);
    public Aup2Value Transparency = Aup2Value.Constant(0);
    public Aup2Value EffectScale = Aup2Value.Constant(100);
    public Aup2Value EffectScaleX = Aup2Value.Constant(100);
    public Aup2Value EffectScaleY = Aup2Value.Constant(100);
    public Aup2BlendMode BlendMode;

    public double Progress(double frame)
    {
        int duration = EndFrame - StartFrame;
        return duration <= 0 ? 0 : Math.Clamp((frame - StartFrame) / duration, 0, 1);
    }
}

public sealed class Aup2Value
{
    private readonly double[] _values;
    private readonly Aup2Interpolation _interpolation;

    private Aup2Value(double[] values, Aup2Interpolation interpolation)
    {
        _values = values;
        _interpolation = interpolation;
    }

    public static Aup2Value Constant(double value) => new([value], Aup2Interpolation.Linear);

    public static Aup2Value Parse(string rawValue, double fallback)
    {
        List<double> values = [];
        foreach (string part in rawValue.Split('|')[0].Split(','))
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                break;
            }

            values.Add(value);
        }

        Aup2Interpolation interpolation = rawValue.Contains("瞬間移動", StringComparison.Ordinal)
            ? Aup2Interpolation.Step
            : rawValue.Contains("加減速移動", StringComparison.Ordinal)
                ? Aup2Interpolation.Ease
                : Aup2Interpolation.Linear;
        return new Aup2Value(values.Count == 0 ? [fallback] : values.ToArray(), interpolation);
    }

    public double At(double progress)
    {
        if (_values.Length == 1)
        {
            return _values[0];
        }

        double position = Math.Clamp(progress, 0, 1) * (_values.Length - 1);
        int index = Math.Min((int)position, _values.Length - 2);
        double amount = position - index;
        if (_interpolation == Aup2Interpolation.Step)
        {
            amount = 0;
        }
        else if (_interpolation == Aup2Interpolation.Ease)
        {
            amount = amount * amount * (3 - 2 * amount);
        }

        return _values[index] + ((_values[index + 1] - _values[index]) * amount);
    }
}

public enum Aup2Interpolation
{
    Linear,
    Step,
    Ease,
}

public enum Aup2BlendMode
{
    Normal,
    Additive,
    Subtractive,
    Multiply,
}

internal static class Aup2BlendModeExtensions
{
    public static Aup2BlendMode Parse(string value) => value.Trim() switch
    {
        "加算" => Aup2BlendMode.Additive,
        "減算" => Aup2BlendMode.Subtractive,
        "乗算" => Aup2BlendMode.Multiply,
        _ => Aup2BlendMode.Normal,
    };
}

public static class Aup2ImageProcessor
{
    public static void ConvertToPremultipliedBgra(byte[] pixels, bool removeBlackBackground)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length % 4 != 0)
        {
            throw new ArgumentException("BGRA pixel data must contain four bytes per pixel.", nameof(pixels));
        }

        for (int index = 0; index < pixels.Length; index += 4)
        {
            int blue = pixels[index];
            int green = pixels[index + 1];
            int red = pixels[index + 2];
            int sourceAlpha = pixels[index + 3];

            pixels[index] = (byte)((blue * sourceAlpha + 127) / 255);
            pixels[index + 1] = (byte)((green * sourceAlpha + 127) / 255);
            pixels[index + 2] = (byte)((red * sourceAlpha + 127) / 255);
            pixels[index + 3] = removeBlackBackground
                ? (byte)((Math.Max(red, Math.Max(green, blue)) * sourceAlpha + 127) / 255)
                : (byte)sourceAlpha;
        }
    }
}
