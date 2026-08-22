using Microsoft.UI.Windowing;
using TaikoDiveLauncher.Models;
using Windows.Graphics;

namespace TaikoDiveLauncher.Services;

internal static class WindowPlacementService
{
    private const int DefaultWidth = 1180;
    private const int DefaultHeight = 760;

    public static RectInt32 GetInitialBounds(WindowPlacementPreferences? savedPlacement)
    {
        if (!IsUsable(savedPlacement))
        {
            return new RectInt32(0, 0, DefaultWidth, DefaultHeight);
        }

        PointInt32 center = new(
            savedPlacement!.X + (savedPlacement.Width / 2),
            savedPlacement.Y + (savedPlacement.Height / 2));
        DisplayArea displayArea = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
        return ClampToWorkArea(savedPlacement, displayArea.WorkArea);
    }

    internal static RectInt32 ClampToWorkArea(
        WindowPlacementPreferences placement,
        RectInt32 workArea)
    {
        WindowBounds bounds = WindowPlacementBounds.ClampToWorkArea(
            placement,
            new WindowBounds(workArea.X, workArea.Y, workArea.Width, workArea.Height));
        return new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool IsUsable(WindowPlacementPreferences? placement)
    {
        return placement is
        {
            Width: >= 720 and <= 16384,
            Height: >= 520 and <= 16384,
        };
    }
}
