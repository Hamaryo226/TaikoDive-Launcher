using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class DemoSongSettingsStore
{
    public const int MaximumSongs = 13;
    private readonly Func<bool> _isRunning;
    public DemoSongSettingsStore() : this(GameProcessService.IsRunning) { }
    internal DemoSongSettingsStore(Func<bool> isRunning) => _isRunning = isRunning;
    public static string SettingsPath(TaikoDiveInstallation installation) =>
        Path.Combine(LauncherToolkitStore.Root(installation), "demo-songs.json");

    public async Task<DemoSongSettings> LoadAsync(TaikoDiveInstallation installation)
    {
        string path = SettingsPath(installation);
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("紹介曲の設定が大きすぎます。");
        var settings = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path), LauncherJsonContext.Default.DemoSongSettings)
            ?? throw new InvalidDataException("紹介曲の設定を読み取れません。");
        settings = Upgrade(settings);
        // 消えた曲も画面に残す。ユーザーが確認するまで設定から取り除かない。
        foreach (var entry in settings.Entries)
            foreach (string relative in entry.SubSongs.Prepend(entry.MainSong))
                if (relative.Length > 0) ResolveChart(installation, relative, false);
        return settings;
    }

    public async Task SaveAsync(TaikoDiveInstallation installation, DemoSongSettings settings)
    {
        if (_isRunning()) throw new InvalidOperationException("ゲームを終了してから紹介曲を保存してください。");
        settings = Upgrade(settings);
        string? problem = SelectionProblem(settings.Entries);
        if (settings.Enabled && problem is not null) throw new InvalidDataException(problem);
        string NormalizePath(string relative, bool playable)
        {
            if (relative.Length == 0 && !playable) return "";
            string chart = ResolveChart(installation, relative, playable);
            if (playable)
            {
                LibrarySong song = SongLibraryService.ReadChart(installation.SongsDirectory, chart);
                if (song.AudioPath is null || song.Problems.Count > 0)
                    throw new InvalidDataException(song.Title + ": " + string.Join(" / ", song.Problems));
                RejectNestedLinks(installation.SongsDirectory, song.AudioPath);
            }
            return Path.GetRelativePath(installation.SongsDirectory, chart).Replace('\\', '/');
        }
        var saved = new DemoSongSettings { Enabled = settings.Enabled, Entries = settings.Entries.Select(entry => new DemoSongEntry
        {
            MainSong = NormalizePath(entry.MainSong, settings.Enabled),
            AutoSubSongs = entry.AutoSubSongs,
            SubSongs = entry.SubSongs.Select(path => NormalizePath(path, settings.Enabled && !entry.AutoSubSongs)).ToList()
        }).ToList() };
        string json = JsonSerializer.Serialize(saved, LauncherJsonContext.Default.DemoSongSettings);
        await FilePersistence.WriteTextAtomicAsync(SettingsPath(installation), json, new UTF8Encoding(false));
    }

    private static DemoSongSettings Upgrade(DemoSongSettings settings)
    {
        if (settings.Version == 1)
        {
            if (settings.Songs is null || settings.Songs.Count > MaximumSongs || settings.Songs.Any(path => path is null))
                throw new InvalidDataException("旧形式の紹介曲設定を読み取れません。");
            settings = new DemoSongSettings { Enabled = settings.Enabled,
                Entries = settings.Songs.Select(path => new DemoSongEntry { MainSong = path }).ToList() };
        }
        if (settings.Version != 2 || settings.Entries is null || settings.Entries.Count > MaximumSongs
            || settings.Entries.Any(entry => entry is null || entry.MainSong is null || entry.SubSongs is null
                || entry.SubSongs.Count > 4 || entry.SubSongs.Any(path => path is null)))
            throw new InvalidDataException("未対応の紹介曲設定です。最大13枠、サブ曲は各4曲です。");
        return settings;
    }

    internal static string? SelectionProblem(IReadOnlyList<DemoSongEntry> entries)
    {
        if (entries.Count == 0) return "紹介する枠を追加してください";
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (string.IsNullOrWhiteSpace(entry.MainSong)) return (index + 1) + "曲目のメイン曲を選んでください";
            if (!entry.AutoSubSongs && (entry.SubSongs.Count != 4 || entry.SubSongs.Any(string.IsNullOrWhiteSpace)))
                return (index + 1) + "曲目のサブ曲を4曲選んでください";
        }
        return null;
    }

    internal static string ResolveChart(TaikoDiveInstallation installation, string relative, bool requireFile)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 2048 || relative.Contains(':')
            || relative.StartsWith('/') || relative.StartsWith('\\'))
            throw new InvalidDataException("Songs内の譜面を選択してください。");
        string root = Path.GetFullPath(installation.SongsDirectory);
        string chart = Path.GetFullPath(relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar), root);
        if (!chart.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !chart.EndsWith(".tja", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Songs内のTJAを選択してください。");
        if (requireFile || File.Exists(chart)) RejectNestedLinks(root, chart);
        return chart;
    }

    private static void RejectNestedLinks(string root, string path)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        for (string? current = path; current is not null && current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Songs配下のリンクは紹介曲に使用できません。");
    }
}
