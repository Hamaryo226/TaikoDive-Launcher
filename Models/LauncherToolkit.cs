namespace TaikoDiveLauncher.Models;

public sealed class OutfitPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "コーデ";
    public Character3DSettings Settings { get; set; } = new();
    public override string ToString() => Name;
}

public sealed class OutfitCollection
{
    public int Version { get; set; } = 1;
    public List<OutfitPreset> Presets { get; set; } = [];
}

public sealed record LibrarySong(string Title, string ChartPath, string? AudioPath, double DemoStart,
    string Hash, IReadOnlyList<string> Problems)
{
    public string Label => $"{Title}  ·  {System.IO.Path.GetFileName(ChartPath)}";
    public override string ToString() => Label;
}

public sealed record UpdateFileChange(string Path, string Kind, long Size, string BackupSha256 = "");
public sealed class UpdatePreview
{
    public string Version { get; set; } = "";
    public string Revision { get; set; } = "";
    public long RequiredBytes { get; set; }
    public List<UpdateFileChange> Changes { get; set; } = [];
}

public sealed class UpdateHistoryEntry
{
    public string Revision { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BuildDirectory { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime InstalledAtUtc { get; set; }
    public bool Reverted { get; set; }
    public string BackupDirectory { get; set; } = "";
    public string? PreviousInstalledState { get; set; }
    public List<UpdateFileChange> Changes { get; set; } = [];
    public override string ToString() => $"{InstalledAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}  v{Version}" + (Reverted ? "（復帰済み）" : "");
}

public sealed class UpdateHistory
{
    public List<UpdateHistoryEntry> Entries { get; set; } = [];
}

public sealed class LauncherRollbackEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TargetPath { get; set; } = "";
    public string PreviousVersion { get; set; } = "";
    public string PreviousSha256 { get; set; } = "";
    public string InstalledSha256 { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public override string ToString() => $"{CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} → {PreviousVersion}へ復帰";
}
