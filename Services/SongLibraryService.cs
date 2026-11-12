using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public static class SongLibraryService
{
    public static Task<IReadOnlyList<LibrarySong>> ScanAsync(string root, CancellationToken token = default) => Task.Run<IReadOnlyList<LibrarySong>>(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var songs = new List<LibrarySong>();
        foreach (string path in LauncherToolkitStore.SafeFiles(root).Where(p => p.EndsWith(".tja", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            try { songs.Add(ReadChart(root, path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            { songs.Add(new(Path.GetFileNameWithoutExtension(path), path, null, 0, "", [ex.Message])); }
        }
        return songs.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, token);

    internal static LibrarySong ReadChart(string root, string path)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) return new(Path.GetFileNameWithoutExtension(path), path, null, 0, "", ["譜面が8MBを超えています"]);
        byte[] bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(932).GetString(bytes); }
        string title = Path.GetFileNameWithoutExtension(path), wave = "";
        double demo = 0;
        bool inChart = false;
        int starts = 0, ends = 0;
        var issues = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split(new[] { "//" }, 2, StringSplitOptions.None)[0].Trim();
            if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase)) { starts++; inChart = true; }
            if (line.Equals("#END", StringComparison.OrdinalIgnoreCase)) { ends++; inChart = false; }
            if (inChart) continue;
            int colon = line.IndexOf(':'); if (colon < 0) continue;
            string key = line[..colon].Trim().ToUpperInvariant(), value = line[(colon + 1)..].Trim();
            if (key == "TITLE" && value.Length > 0) title = value;
            if (key == "WAVE") wave = value.Trim('"');
            if (key == "DEMOSTART" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d)) demo = Math.Max(0, d);
        }
        if (starts == 0 || starts != ends) issues.Add("#START / #END の対応を確認してください");
        string? audio = null;
        if (wave.Length == 0) issues.Add("WAVEが指定されていません");
        else
        {
            string candidate = Path.GetFullPath(wave.Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(path)!);
            string relative = Path.GetRelativePath(Path.GetFullPath(root), candidate);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) issues.Add("音源がSongsの外を参照しています");
            else if (!File.Exists(candidate)) issues.Add("音源が見つかりません: " + wave);
            else audio = candidate;
        }
        return new(title, path, audio, demo, Convert.ToHexString(SHA256.HashData(bytes)), issues);
    }

    public static IReadOnlyList<IReadOnlyList<LibrarySong>> FindDuplicates(IEnumerable<LibrarySong> songs) => songs
        .Where(s => s.Hash.Length > 0).GroupBy(s => s.Hash, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1).Select(g => (IReadOnlyList<LibrarySong>)g.ToArray()).ToArray();

    public static async Task<IReadOnlyList<ReplayEntry>> ReadReplaysAsync(TaikoDiveInstallation installation, CancellationToken token = default)
    {
        var profiles = await new UserProfileStore().LoadAsync(installation);
        var entries = new List<ReplayEntry>();
        foreach (string file in LauncherToolkitStore.SafeFiles(installation.ScoreDirectory).Where(p => Path.GetFileName(p).Equals("best.json", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(file).Length > 32 * 1024 * 1024) continue;
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file, token));
                var r = json.RootElement;
                string song = r.GetProperty("SongPath").GetString() ?? "";
                string player = r.GetProperty("PlayerName").GetString() ?? "";
                int course = r.GetProperty("Course").GetInt32();
                string hash = r.GetProperty("ChartHash").GetString() ?? "";
                int slot = profiles.FirstOrDefault(p => p.Name == player)?.Slot ?? 0;
                string? problem = null;
                if (course is < 0 or > 4 || r.GetProperty("Version").GetInt32() != 2) problem = "未対応の形式";
                else if (slot == 0) problem = "ユーザーが見つかりません";
                else if (!File.Exists(song)) problem = "譜面が見つかりません";
                else if (new FileInfo(song).Length > 8 * 1024 * 1024) problem = "譜面サイズが大きすぎます";
                else if (!hash.Equals(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(song, token))), StringComparison.OrdinalIgnoreCase)) problem = "譜面が変更されています";
                entries.Add(new(file, r.GetProperty("SongTitle").GetString() ?? "", song, course, player, slot,
                    r.GetProperty("RecordedAtUtc").GetDateTime(), r.GetProperty("Result").GetProperty("Score").GetInt32(), hash, problem));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException) { }
        }
        return entries.OrderByDescending(e => e.RecordedAtUtc).ToArray();
    }
}
