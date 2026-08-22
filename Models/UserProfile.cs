namespace TaikoDiveLauncher.Models;

public sealed class UserProfile
{
    public int Slot { get; init; }

    public string Name { get; set; } = "どんちゃん";

    public string Title { get; set; } = "ドンだーデビュー";

    public int NamePlateType { get; set; }

    public string CharaType { get; set; } = "0";

    public bool IsConfigured { get; set; }

    public string DisplayLabel => IsConfigured ? $"{Slot}P  {Name}" : $"{Slot}P  未設定";
}

public sealed record StringOption(string Value, string Label);

public sealed record IntOption(int Value, string Label);

public sealed record ResolutionOption(int Width, string Label);

public sealed record UserStatistics(int ScoreCount, int ReplayCount, string FolderPath);
