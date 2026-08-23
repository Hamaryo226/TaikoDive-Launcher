namespace TaikoDiveLauncher.Models;

public enum GameUpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Verifying,
    Applying,
    Completed,
    Failed,
}

public sealed class GameUpdateManifest
{
    public string Version { get; set; } = string.Empty;

    public string Revision { get; set; } = string.Empty;

    public string PackageFileName { get; set; } = string.Empty;

    public string PackageUrl { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset PublishedAt { get; set; }

    public GameUpdateArchiveInfo Archive { get; set; } = new();
}

public sealed class GameUpdateArchiveInfo
{
    public string Format { get; set; } = string.Empty;

    public string Encryption { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;
}

public sealed class GamePackageManifest
{
    public string Version { get; set; } = string.Empty;

    public string Revision { get; set; } = string.Empty;

    public List<GamePackageFile> Files { get; set; } = [];
}

public sealed class GamePackageFile
{
    public string Path { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long Size { get; set; }
}

internal sealed class InstalledGameUpdate
{
    public string BuildDirectory { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Revision { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }
}
