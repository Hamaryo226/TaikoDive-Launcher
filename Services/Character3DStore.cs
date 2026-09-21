using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class Character3DStore
{
    private readonly Func<bool> _isRunning;
    public Character3DStore() : this(GameProcessService.IsRunning) { }
    internal Character3DStore(Func<bool> isRunning) => _isRunning = isRunning;
    public static string SettingsPath(TaikoDiveInstallation installation) => Path.Combine(installation.BuildDirectory, "Info", "Chara3D.json");
    public static string ModelRoot(TaikoDiveInstallation installation, string path) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "Models/Donchan" : path, installation.BuildDirectory);

    private static async Task<JsonObject> ReadAsync(string path) => File.Exists(path)
        ? JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject ?? throw new InvalidDataException("Chara3D.json の形式が正しくありません。")
        : new JsonObject();

    public async Task<Character3DSettings> LoadAsync(TaikoDiveInstallation installation, int? userSlot = null)
        => await LoadFromPathAsync(SettingsPath(installation), userSlot);

    public async Task<Character3DSettings> LoadPreviousAsync(TaikoDiveInstallation installation, int? userSlot = null)
    {
        string path = SettingsPath(installation) + ".launcher.bak";
        if (!File.Exists(path)) throw new FileNotFoundException("前回の保存データはまだありません。設定を保存すると作成されます。");
        return await LoadFromPathAsync(path, userSlot);
    }

    private static async Task<Character3DSettings> LoadFromPathAsync(string path, int? userSlot)
    {
        ValidateSlot(userSlot);
        var root = await ReadAsync(path);
        var profile = userSlot.HasValue ? root["users"]?[userSlot.Value.ToString(CultureInfo.InvariantCulture)] as JsonObject : null;
        var parts = Merge(root["parts"] as JsonObject, profile?["parts"] as JsonObject);
        var colors = Merge(root["colors"] as JsonObject, profile?["colors"] as JsonObject);
        return new Character3DSettings
        {
            Enabled = true,
            ModelsPath = (string?)root["modelsPath"] ?? "Models/Donchan",
            UseCostume = (bool?)parts?["useCostume"] ?? false,
            Head = (string?)parts?["head"] ?? "0", Body = (string?)parts?["body"] ?? "0", Costume = (string?)parts?["costume"] ?? "0",
            BodyColor = NormalizeColor((string?)colors?["body"] ?? "#00A7BE"),
            LimbsColor = NormalizeColor((string?)colors?["limbs"] ?? "#FFF6DE"),
            FaceColor = NormalizeColor((string?)colors?["face"] ?? "#FF4125"),
            RimColor = NormalizeColor((string?)colors?["rim"] ?? "#FFF6DE"),
        };
    }

    private static JsonObject Merge(JsonObject? defaults, JsonObject? overrides)
    {
        var result = defaults?.DeepClone() as JsonObject ?? new JsonObject();
        if (overrides is not null)
            foreach (var property in overrides) result[property.Key] = property.Value?.DeepClone();
        return result;
    }

    private static void ValidateSlot(int? slot)
    {
        if (slot.HasValue && slot.Value is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(slot));
    }

    public static string NormalizeColor(string color)
    {
        if (color.Length != 7 || color[0] != '#' || !uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("色は #RRGGBB の形式で指定してください。");
        return color.ToUpperInvariant();
    }

    private static bool ValidId(string id) => id.Length is > 0 and <= 10 && id.All(c => c is >= '0' and <= '9');

    public IReadOnlyList<CostumeOption> GetOptions(string root, string category, string current)
    {
        if (category is not ("head" or "body" or "cos")) throw new ArgumentException("Unknown costume category.", nameof(category));
        JsonObject? names = null;
        string catalog = Path.Combine(root, "costume_names.json");
        if (File.Exists(catalog))
            names = JsonNode.Parse(File.ReadAllText(catalog))?[category == "cos" ? "costume" : category] as JsonObject;
        var result = new List<CostumeOption>();
        string directory = Path.Combine(root, category);
        if (Directory.Exists(directory))
            foreach (string path in Directory.EnumerateFiles(directory, "*.glb").OrderBy(p => Path.GetFileNameWithoutExtension(p).Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (!ValidId(id)) continue;
                string label = (string?)names?[id]?["name"] ?? (id == "0" ? "標準" : $"衣装 {id}");
                string iconFolder = category == "cos" ? "costume_icon" : $"costume_{category}_icon";
                string icon = Path.Combine(root, iconFolder, id + ".png");
                result.Add(new CostumeOption(id, $"{label}  ({id})", File.Exists(icon) ? icon : null));
            }
        if (!result.Any(p => p.Id == current)) result.Insert(0, new CostumeOption(current, $"未配置 ({current})"));
        return result;
    }

    public async Task SaveAsync(TaikoDiveInstallation installation, Character3DSettings settings, int? userSlot = null)
    {
        ValidateSlot(userSlot);
        if (_isRunning()) throw new InvalidOperationException("TaikoDive の実行中は保存できません。ゲームを終了してください。");
        // 個別保存で共通の有効状態・参照先を上書きしない。検証にも最新の共通値を使う。
        string path = SettingsPath(installation);
        var root = await ReadAsync(path);
        if (userSlot.HasValue)
            settings = settings with { ModelsPath = (string?)root["modelsPath"] ?? "Models/Donchan" };
        foreach (string id in new[] { settings.Head, settings.Body, settings.Costume })
            if (!ValidId(id)) throw new InvalidDataException("衣装 ID が正しくありません。");
        string body = NormalizeColor(settings.BodyColor), limbs = NormalizeColor(settings.LimbsColor);
        string face = NormalizeColor(settings.FaceColor), rim = NormalizeColor(settings.RimColor);
        string modelRoot = ModelRoot(installation, settings.ModelsPath);
        // プレイヤーは3D専用なので、旧enabledの値にかかわらず検証する。
        {
            var required = new List<string> { "animations.glb", "face/face_000000.png" };
            if (settings.UseCostume) required.Add($"cos/{settings.Costume}.glb");
            else { required.Add($"head/{settings.Head}.glb"); required.Add($"body/{settings.Body}.glb"); }
            foreach (string file in required)
                if (!File.Exists(Path.Combine(modelRoot, file))) throw new FileNotFoundException($"必要なモデルファイルがありません: {file}");
        }
        JsonObject target = root;
        if (userSlot.HasValue)
        {
            var users = GetOrCreateObject(root, "users");
            target = GetOrCreateObject(users, userSlot.Value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            root["modelsPath"] = string.IsNullOrWhiteSpace(settings.ModelsPath) ? "Models/Donchan" : settings.ModelsPath.Trim();
        }
        root["enabled"] = true; // 以前の3D対応本体とも互換を保つ。
        var parts = GetOrCreateObject(target, "parts");
        parts["useCostume"] = settings.UseCostume; parts["head"] = settings.Head; parts["body"] = settings.Body; parts["costume"] = settings.Costume;
        var colors = GetOrCreateObject(target, "colors");
        colors["body"] = body; colors["limbs"] = limbs; colors["face"] = face; colors["rim"] = rim;
        if (_isRunning()) throw new InvalidOperationException("TaikoDive の実行中は保存できません。");
        await FilePersistence.WriteTextAtomicAsync(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing) return existing;
        if (parent[key] is not null) throw new InvalidDataException($"{key} の形式が正しくありません。");
        var value = new JsonObject();
        parent[key] = value;
        return value;
    }
}
