using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TaikoDiveLauncher.Models;

public sealed class DemoSongSettings
{
    public int Version { get; set; } = 2;
    public bool Enabled { get; set; }
    public List<DemoSongEntry> Entries { get; set; } = [];
    // 旧ランチャーの設定は読み込み時にEntriesへ移行する。
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Songs { get; set; }
}

public sealed class DemoSongEntry
{
    public string MainSong { get; set; } = "";
    public bool AutoSubSongs { get; set; } = true;
    public List<string> SubSongs { get; set; } = [];
}

public sealed partial class SelectedDemoSong(string relativePath, LibrarySong? song) : INotifyPropertyChanged
{
    public string RelativePath { get; } = relativePath;
    public LibrarySong? Song { get; } = song;
    public string Label => Song?.Title ?? "見つからない曲";
    public string Description => Song is null ? "譜面が移動・削除されています" :
        string.Join(" · ", new[] { Song.Genre, Song.Subtitle }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool CanUse => Song is { AudioPath: not null } && Song.Problems.Count == 0;
    public string Problem => Song is null ? "この譜面は見つかりません。取り除くか、元の場所に戻してください。" : string.Join(" / ", Song.Problems);
    private int _number;
    public int Number
    {
        get => _number;
        set { if (_number == value) return; _number = value; PropertyChanged?.Invoke(this, new(nameof(NumberLabel))); }
    }
    public string NumberLabel => _number.ToString("00");
    public override string ToString() => _number > 0 ? _number + "番 " + Label : Label;
    public event PropertyChangedEventHandler? PropertyChanged;
}
