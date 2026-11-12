using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public static class LauncherToolkitStore
{
    public static string Root(TaikoDiveInstallation installation) => Path.Combine(installation.BuildDirectory, "Info", "TaikoDiveLauncher");
    public static string IntegrationPath(TaikoDiveInstallation installation) => Path.Combine(Root(installation), "integrations.json");

    public static async Task<IntegrationSettings> LoadIntegrationsAsync(TaikoDiveInstallation installation)
    {
        string path = IntegrationPath(installation);
        return File.Exists(path)
            ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path), LauncherJsonContext.Default.IntegrationSettings)
                ?? throw new InvalidDataException("連携設定を読み取れません。")
            : new();
    }

    public static Task SaveIntegrationsAsync(TaikoDiveInstallation installation, IntegrationSettings settings)
    {
        EnsureStopped();
        settings.Goal = settings.Goal.Length > 200 ? settings.Goal[..200] : settings.Goal;
        return FilePersistence.WriteTextAtomicAsync(IntegrationPath(installation),
            JsonSerializer.Serialize(settings, LauncherJsonContext.Default.IntegrationSettings), new UTF8Encoding(false));
    }

    public static async Task<OutfitCollection> LoadOutfitsAsync(TaikoDiveInstallation installation)
    {
        string path = Path.Combine(Root(installation), "outfits.json");
        return File.Exists(path) ? ParseOutfits(await File.ReadAllTextAsync(path)) : new();
    }

    public static OutfitCollection ParseOutfits(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw new InvalidDataException("コーデファイルは1MBまでです。");
        var collection = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.OutfitCollection)
            ?? throw new InvalidDataException("コーデファイルの形式が不正です。");
        if (collection.Version != 1 || collection.Presets is null || collection.Presets.Count > 100)
            throw new InvalidDataException("未対応のコーデ形式、または100件を超えるコーデです。");
        foreach (var preset in collection.Presets)
        {
            if (preset is null || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80 || preset.Settings is null)
                throw new InvalidDataException("コーデ名は1〜80文字で指定してください。");
            var s = preset.Settings;
            if (new[] { s.BodyColor, s.LimbsColor, s.FaceColor, s.RimColor }.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("コーデの色がありません。");
            Character3DStore.NormalizeColor(s.BodyColor); Character3DStore.NormalizeColor(s.LimbsColor);
            Character3DStore.NormalizeColor(s.FaceColor); Character3DStore.NormalizeColor(s.RimColor);
            if (s.PetitCharaId < 0 || new[] { s.Head, s.Body, s.Costume }.Any(id =>
                string.IsNullOrWhiteSpace(id) || id.Length > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
                throw new InvalidDataException("衣装IDが正しくありません。");
            // 共有ファイルでモデル参照先を変更しない。
            s.ModelsPath = "Info/Chara/Model";
        }
        return collection;
    }

    public static Task SaveOutfitsAsync(TaikoDiveInstallation installation, OutfitCollection collection)
    {
        EnsureStopped();
        string json = JsonSerializer.Serialize(collection, LauncherJsonContext.Default.OutfitCollection);
        ParseOutfits(json);
        return FilePersistence.WriteTextAtomicAsync(Path.Combine(Root(installation), "outfits.json"), json, new UTF8Encoding(false));
    }

    public static IReadOnlyList<string> MissingAssets(TaikoDiveInstallation installation, Character3DSettings s, string? modelsPath = null)
    {
        var store = new Character3DStore();
        string root = Character3DStore.ModelRoot(installation, modelsPath ?? Character3DStore.DefaultModelsPath(installation));
        var missing = new List<string>();
        foreach (var (category, id) in s.UseCostume ? new[] { ("cos", s.Costume) } : new[] { ("head", s.Head), ("body", s.Body) })
            if (id != "0" && !store.GetOptions(root, category, "0").Any(x => x.Id == id)) missing.Add(category + ": " + id);
        if (!store.GetPetitCharaOptions(installation, 0).Any(x => x.Id == s.PetitCharaId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            missing.Add("ぷちキャラ: " + s.PetitCharaId);
        return missing;
    }

    public static async Task<IReadOnlyList<PlayHistoryEntry>> ReadHistoryAsync(TaikoDiveInstallation installation, CancellationToken token = default)
    {
        string root = Path.Combine(Root(installation), "history");
        var entries = new Dictionary<string, PlayHistoryEntry>(StringComparer.Ordinal);
        foreach (string file in SafeFiles(root).Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(file).Length > 16384) continue;
                var e = JsonSerializer.Deserialize(await File.ReadAllTextAsync(file, token), LauncherJsonContext.Default.PlayHistoryEntry);
                if (e is not null && !string.IsNullOrWhiteSpace(e.Id) && e.PlayedAtUtc != default) entries.TryAdd(e.Id, e);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return entries.Values.OrderByDescending(e => e.PlayedAtUtc).ToArray();
    }

    // 外部Songsリンクはルートとして使えるが、その配下のリンクはたどらない。
    public static IEnumerable<string> SafeFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else yield return path;
            }
        }
    }

    public static void EnsureStopped()
    {
        if (GameProcessService.IsRunning()) throw new InvalidOperationException("TaikoDiveを終了してから操作してください。");
    }
}
