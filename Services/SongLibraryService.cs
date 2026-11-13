using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public static class SongLibraryService
{
    static SongLibraryService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static Task<IReadOnlyList<LibrarySong>> ScanAsync(string root, CancellationToken token = default) => Task.Run<IReadOnlyList<LibrarySong>>(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var songs = new List<LibrarySong>();
        foreach (string path in LauncherToolkitStore.SafeFiles(root).Where(p => p.EndsWith(".tja", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            try { songs.Add(ReadChart(root, path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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
        string title = Path.GetFileNameWithoutExtension(path), wave = "", subtitle = "", genre = "";
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
            if (key == "SUBTITLE") subtitle = value.StartsWith("--") || value.StartsWith("++") ? value[2..] : value;
            if (key == "GENRE") genre = value;
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
        if (genre.Length == 0) genre = ReadFolderGenre(root, Path.GetDirectoryName(path)!);
        return new(title, path, audio, demo, Convert.ToHexString(SHA256.HashData(bytes)), issues) { Subtitle = subtitle, Genre = genre };
    }

    private static string ReadFolderGenre(string root, string directory)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (directory.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || directory.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            string box = Path.Combine(directory, "box.def");
            if (File.Exists(box) && new FileInfo(box).Length <= 1024 * 1024)
            {
                byte[] bytes = File.ReadAllBytes(box);
                string text;
                try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
                catch (DecoderFallbackException) { text = Encoding.GetEncoding(932).GetString(bytes); }
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#GENRE:", StringComparison.OrdinalIgnoreCase)) return line[7..].Trim();
                }
            }
            if (directory.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) break;
            directory = Path.GetDirectoryName(directory)!;
        }
        return "";
    }
}
