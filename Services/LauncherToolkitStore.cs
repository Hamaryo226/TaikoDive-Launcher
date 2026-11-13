using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public static class LauncherToolkitStore
{
    public static string Root(TaikoDiveInstallation installation) => Path.Combine(installation.BuildDirectory, "Info", "TaikoDiveLauncher");

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
        if (!File.Exists(Path.Combine(root, "animations.glb"))) missing.Add("基本モデル: animations.glb");
        if (!File.Exists(Path.Combine(root, "face", "face_000000.png"))) missing.Add("基本モデル: face/face_000000.png");
        foreach (var (category, id) in s.UseCostume ? new[] { ("cos", s.Costume) } : new[] { ("head", s.Head), ("body", s.Body) })
            if (!File.Exists(Path.Combine(root, category, id + ".glb"))) missing.Add(category + ": " + id);
        if (!store.GetPetitCharaOptions(installation, 0).Any(x => x.Id == s.PetitCharaId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            missing.Add("ぷちキャラ: " + s.PetitCharaId);
        return missing;
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
