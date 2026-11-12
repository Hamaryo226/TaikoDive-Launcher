using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public static partial class LauncherMaintenance
{
    public static string UpdatesRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaikoDiveLauncher");

    public static Task<IReadOnlyList<StorageUsage>> MeasureAsync(TaikoDiveInstallation installation, CancellationToken token = default) => Task.Run<IReadOnlyList<StorageUsage>>(() =>
    {
        var roots = new List<(string Name, string Path, bool Clean)>
        {
            ("楽曲", installation.SongsDirectory, false), ("テクスチャ", Path.Combine(installation.BuildDirectory, "Texture"), false),
            ("サウンド", Path.Combine(installation.BuildDirectory, "Sound"), false), ("スコア・リプレイ", installation.ScoreDirectory, false),
            ("3Dモデル", Path.Combine(installation.BuildDirectory, "Info", "Chara"), false),
        };
        foreach (string channel in new[] { "game-updates", "asset-updates" })
        {
            roots.Add((channel + " バックアップ", Path.Combine(UpdatesRoot, channel, "backups"), false));
            roots.Add((channel + " ダウンロード", Path.Combine(UpdatesRoot, channel, "downloads"), true));
            roots.Add((channel + " 一時ファイル", Path.Combine(UpdatesRoot, channel, "staging"), true));
        }
        return roots.Select(r =>
        {
            long bytes = 0; int count = 0;
            foreach (string path in LauncherToolkitStore.SafeFiles(r.Path)) { token.ThrowIfCancellationRequested(); bytes += new FileInfo(path).Length; count++; }
            return new StorageUsage(r.Name, r.Path, bytes, count, r.Clean);
        }).ToArray();
    }, token);

    public static async Task<int> CleanOldCacheAsync(TaikoDiveInstallation installation, CancellationToken token = default)
    {
        LauncherToolkitStore.EnsureStopped();
        int count = 0;
        foreach (var usage in await MeasureAsync(installation, token).ConfigureAwait(false))
            if (usage.CanClean)
                foreach (string file in LauncherToolkitStore.SafeFiles(usage.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) { File.Delete(file); count++; }
                }
        return count;
    }

    // 共有前にこの本文をプレビューする。名前、入力割当、接続先、リプレイは出力しない。
    public static async Task<string> DiagnosticReportAsync(LauncherContext context)
    {
        var report = new JsonObject
        {
            ["createdAtUtc"] = DateTime.UtcNow.ToString("O"), ["os"] = Environment.OSVersion.VersionString,
            ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ["launcher"] = context.Updates.CurrentVersionText, ["game"] = context.GameUpdates.CurrentVersionText,
            ["assets"] = context.AssetUpdates.CurrentVersionText, ["gameRunning"] = GameProcessService.IsRunning(),
        };
        if (context.Installation is { } installation)
        {
            report["characterDiagnosis"] = Redact(await Character3DStore.DiagnoseAsync(installation) ?? "正常");
            var settings = await new GameSettingsStore().LoadAsync(installation);
            report["display"] = new JsonObject { ["width"] = settings.ScreenWidth, ["fullScreen"] = settings.FullScreen, ["vsync"] = settings.VerticalSync };
            report["sound"] = new JsonObject { ["type"] = settings.SoundType, ["bufferSamples"] = settings.SoundBufferSamples };
            var logs = new JsonArray();
            string errors = Path.Combine(installation.BuildDirectory, "Log", "Error");
            foreach (string path in LauncherToolkitStore.SafeFiles(errors).OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
            {
                if (new FileInfo(path).Length > 512 * 1024) continue;
                string text = await File.ReadAllTextAsync(path);
                logs.Add(Redact(text.Length > 12000 ? text[^12000..] : text));
            }
            report["recentErrors"] = logs;
        }
        return report.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    internal static string Redact(string text)
    {
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName })
            if (!string.IsNullOrWhiteSpace(folder)) text = text.Replace(folder, "[USER]", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(?i)[A-Z]:\\[^\r\n\""<>|]*", "[PATH]");
        text = Regex.Replace(text, @"(?i)https?://[^\s]+|\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?", "[ADDRESS]");
        return text;
    }

    public static IReadOnlyList<string> CheckCompatibility(GameUpdateManifest manifest, string gameVersion, string assetsVersion, string launcherVersion)
    {
        var problems = new List<string>();
        foreach (var (name, required, current) in new[] { ("本体", manifest.MinimumGameVersion, gameVersion),
            ("Asset", manifest.MinimumAssetsVersion, assetsVersion), ("ランチャー", manifest.MinimumLauncherVersion, launcherVersion.Split('+')[0].Split(' ')[0]) })
        {
            if (string.IsNullOrWhiteSpace(required)) continue;
            if (!Version.TryParse(required, out var minimum) || !Version.TryParse(current, out var actual) || actual < minimum)
                problems.Add($"{name} v{required}以上が必要です（現在: {current}）。");
        }
        return problems;
    }
}
