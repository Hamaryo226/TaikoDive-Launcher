namespace TaikoDiveLauncher.Models;

public sealed class UserProfile
{
    public int Slot { get; init; }

    public string Name { get; set; } = "どんちゃん";

    public string Title { get; set; } = "ドンだーデビュー";

    public int NamePlateType { get; set; }

    public string CharaType { get; set; } = "0";

    public bool IsConfigured { get; set; }

    public string DisplayLabel => IsConfigured ? $"{Slot}.  {Name}" : $"{Slot}.  未設定";
}

public sealed record StringOption(string Value, string Label);

public sealed record IntOption(int Value, string Label);

public sealed record ResolutionOption(int Width, string Label);

public sealed record UserStatistics(int ScoreCount, int ReplayCount, string FolderPath);

public sealed record RecentSong(string Title, DateTime PlayedAt, string ScorePath);

public sealed class SongBestResult
{
    public required string Difficulty { get; init; }
    public int Score { get; set; }
    public double Gauge { get; set; }
    public int Great { get; set; }
    public int Good { get; set; }
    public int Miss { get; set; }
    public int RollCount { get; set; }
    public int MaxCombo { get; set; }
    public string Crown { get; set; } = "NoClear";
    public string ScoreRank { get; set; } = "なし";

    public bool HasRecord => Score > 0 || Great > 0 || Good > 0 || Miss > 0 || RollCount > 0
        || Crown != "NoClear" || ScoreRank != "なし";

    public string CrownLabel => Crown switch
    {
        "Clear" => "クリア",
        "FullCombo" => "フルコンボ",
        "DondaFullCombo" => "ドンダフルコンボ",
        _ => "なし",
    };
}
