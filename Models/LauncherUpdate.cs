namespace TaikoDiveLauncher.Models;

public enum LauncherUpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    StartingInstaller,
    Failed,
}

public sealed class LauncherUpdateManifest
{
    public string Revision { get; set; } = string.Empty;

    public string ReleaseNotes { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
}
