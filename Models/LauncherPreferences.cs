namespace TaikoDiveLauncher.Models;

public sealed class LauncherPreferences
{
    public bool CloseAfterLaunch { get; set; } = true;

    public string Theme { get; set; } = "Dark";

    public WindowPlacementPreferences? WindowPlacement { get; set; }
}

public sealed class WindowPlacementPreferences
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsMaximized { get; set; }
}

internal readonly record struct WindowBounds(int X, int Y, int Width, int Height);

internal static class WindowPlacementBounds
{
    private const int MinimumWidth = 720;
    private const int MinimumHeight = 520;

    public static WindowBounds ClampToWorkArea(
        WindowPlacementPreferences placement,
        WindowBounds workArea)
    {
        int width = Math.Min(
            Math.Clamp(placement.Width, MinimumWidth, Math.Max(MinimumWidth, workArea.Width)),
            workArea.Width);
        int height = Math.Min(
            Math.Clamp(placement.Height, MinimumHeight, Math.Max(MinimumHeight, workArea.Height)),
            workArea.Height);
        int maximumX = workArea.X + workArea.Width - width;
        int maximumY = workArea.Y + workArea.Height - height;
        int x = Math.Clamp(placement.X, workArea.X, maximumX);
        int y = Math.Clamp(placement.Y, workArea.Y, maximumY);
        return new WindowBounds(x, y, width, height);
    }
}
