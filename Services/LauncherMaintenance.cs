using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public static class LauncherMaintenance
{
    public static string UpdatesRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaikoDiveLauncher");

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
