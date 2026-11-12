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

public sealed class IntegrationSettings
{
    public int Version { get; set; } = 1;
    public bool DiscordEnabled { get; set; }
    public bool DiscordShowSong { get; set; }
    public bool OverlayEnabled { get; set; }
    public bool RecordHistory { get; set; } = true;
    public string Goal { get; set; } = "";
}

public sealed record LibrarySong(string Title, string ChartPath, string? AudioPath, double DemoStart,
    string Hash, IReadOnlyList<string> Problems)
{
    public string Label => $"{Title}  ·  {System.IO.Path.GetFileName(ChartPath)}";
    public override string ToString() => Label;
}

public sealed record ReplayEntry(string FilePath, string Title, string SongPath, int Course,
    string PlayerName, int UserSlot, DateTime RecordedAtUtc, int Score, string ChartHash, string? Problem)
{
    public string Label => $"{Title} / {new[] { "かんたん", "ふつう", "むずかしい", "おに", "裏" }[Math.Clamp(Course, 0, 4)]} / {PlayerName} / {Score:N0} / {RecordedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    public override string ToString() => Label + (Problem is null ? "" : $"（{Problem}）");
}

public sealed class PlayHistoryEntry
{
    public string Id { get; set; } = "";
    public DateTime PlayedAtUtc { get; set; }
    public string PlayerName { get; set; } = "";
    public string SongTitle { get; set; } = "";
    public int Course { get; set; }
    public int Score { get; set; }
}

public sealed record UpdateFileChange(string Path, string Kind, long Size);
public sealed class UpdatePreview
{
    public string Version { get; set; } = "";
    public string Revision { get; set; } = "";
    public long RequiredBytes { get; set; }
    public List<UpdateFileChange> Changes { get; set; } = [];
}

public sealed class UpdateHistoryEntry
{
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

public sealed record StorageUsage(string Name, string Path, long Bytes, int FileCount, bool CanClean)
{
    public override string ToString() => $"{Name} — {Bytes / 1048576d:N1} MB / {FileCount:N0}ファイル";
}
