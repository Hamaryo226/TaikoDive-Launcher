namespace TaikoDiveLauncher.Models;

public sealed class LauncherPreferences
{
    public string GameDirectory { get; set; } = string.Empty;

    public bool CloseAfterLaunch { get; set; } = true;
}
