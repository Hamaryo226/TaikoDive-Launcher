using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class InputBindingsStore
{
    private readonly Func<bool> _isGameRunning;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public InputBindingsStore() : this(GameProcessService.IsRunning)
    {
    }

    internal InputBindingsStore(Func<bool> isGameRunning)
    {
        _isGameRunning = isGameRunning;
    }

    public async Task<InputBindings> LoadAsync(TaikoDiveInstallation installation)
    {
        JsonObject root = await LoadRootAsync(installation.GameSettingsPath).ConfigureAwait(false);
        return new InputBindings
        {
            Player1 = ReadPlayer(root["p1Keys"] as JsonObject, [32], [33, 34], [35, 36], [37]),
            Player2 = ReadPlayer(root["p2Keys"] as JsonObject, [44], [45], [46], [47]),
        };
    }

    public async Task SaveAsync(TaikoDiveInstallation installation, InputBindings bindings)
    {
        if (_isGameRunning())
        {
            throw new InvalidOperationException("TaikoDive の実行中は設定を保存できません。ゲームを終了してから再試行してください。");
        }

        JsonObject root = await LoadRootAsync(installation.GameSettingsPath).ConfigureAwait(false);
        WritePlayer(root, "p1Keys", bindings.Player1);
        WritePlayer(root, "p2Keys", bindings.Player2);
        string json = root.ToJsonString(SerializerOptions);
        await FilePersistence.WriteTextAtomicAsync(installation.GameSettingsPath, json, new UTF8Encoding(false)).ConfigureAwait(false);
    }

    private static PlayerInputBindings ReadPlayer(JsonObject? source, int[] kaLeft, int[] donLeft, int[] donRight, int[] kaRight)
    {
        source ??= new JsonObject();
        return new PlayerInputBindings
        {
            KaLeft = ReadIntArray(source, "kaLeft", kaLeft),
            DonLeft = ReadIntArray(source, "donLeft", donLeft),
            DonRight = ReadIntArray(source, "donRight", donRight),
            KaRight = ReadIntArray(source, "kaRight", kaRight),
            KaLeftControllers = ReadControllers(source, "kaLeftControllers"),
            DonLeftControllers = ReadControllers(source, "donLeftControllers"),
            DonRightControllers = ReadControllers(source, "donRightControllers"),
            KaRightControllers = ReadControllers(source, "kaRightControllers"),
        };
    }

    private static int[] ReadIntArray(JsonObject source, string propertyName, int[] fallback)
    {
        if (source[propertyName] is not JsonArray values)
        {
            return fallback;
        }

        List<int> result = [];
        foreach (JsonNode? value in values)
        {
            if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out int key) && key is >= 0 and <= 255)
            {
                result.Add(key);
            }
        }
        return result.ToArray();
    }

    private static ControllerInputBinding[] ReadControllers(JsonObject source, string propertyName)
    {
        if (source[propertyName] is not JsonArray values)
        {
            return [];
        }

        List<ControllerInputBinding> result = [];
        foreach (JsonNode? value in values)
        {
            if (value is not JsonObject item)
            {
                continue;
            }

            string typeText = item["inputType"]?.GetValue<string>() ?? nameof(ControllerInputType.Button);
            if (!Enum.TryParse(typeText, ignoreCase: true, out ControllerInputType inputType))
            {
                inputType = ControllerInputType.Button;
            }
            result.Add(new ControllerInputBinding
            {
                VendorId = ReadUShort(item, "vendorId"),
                ProductId = ReadUShort(item, "productId"),
                DeviceIndex = ReadInt(item, "deviceIndex", 0),
                DeviceOrdinal = ReadInt(item, "deviceOrdinal", 0),
                DeviceName = item["deviceName"]?.GetValue<string>() ?? string.Empty,
                InputType = inputType,
                InputIndex = ReadInt(item, "inputIndex", 0),
                InputValue = ReadInt(item, "inputValue", 1),
            });
        }
        return result.ToArray();
    }

    private static void WritePlayer(JsonObject root, string propertyName, PlayerInputBindings player)
    {
        JsonObject target = root[propertyName] as JsonObject ?? new JsonObject();
        root[propertyName] = target;
        target["kaLeft"] = CreateIntArray(player.KaLeft);
        target["donLeft"] = CreateIntArray(player.DonLeft);
        target["donRight"] = CreateIntArray(player.DonRight);
        target["kaRight"] = CreateIntArray(player.KaRight);
        target["kaLeftControllers"] = CreateControllerArray(player.KaLeftControllers);
        target["donLeftControllers"] = CreateControllerArray(player.DonLeftControllers);
        target["donRightControllers"] = CreateControllerArray(player.DonRightControllers);
        target["kaRightControllers"] = CreateControllerArray(player.KaRightControllers);
    }

    private static JsonArray CreateIntArray(IEnumerable<int> values)
    {
        JsonArray result = [];
        foreach (int value in values.Distinct())
        {
            JsonNode? node = JsonValue.Create(value);
            result.Add(node);
        }
        return result;
    }

    private static JsonArray CreateControllerArray(IEnumerable<ControllerInputBinding> values)
    {
        JsonArray result = [];
        foreach (ControllerInputBinding value in values)
        {
            JsonNode node = new JsonObject
            {
                ["vendorId"] = value.VendorId,
                ["productId"] = value.ProductId,
                ["deviceIndex"] = value.DeviceIndex,
                ["deviceOrdinal"] = value.DeviceOrdinal,
                ["deviceName"] = value.DeviceName,
                ["inputType"] = value.InputType.ToString(),
                ["inputIndex"] = value.InputIndex,
                ["inputValue"] = value.InputValue,
            };
            result.Add(node);
        }
        return result;
    }

    private static async Task<JsonObject> LoadRootAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        string json = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Setting.json の形式が正しくありません。");
    }

    private static int ReadInt(JsonObject source, string propertyName, int fallback)
    {
        return source[propertyName] is JsonValue node && node.TryGetValue<int>(out int value) ? value : fallback;
    }

    private static ushort ReadUShort(JsonObject source, string propertyName)
    {
        int value = ReadInt(source, propertyName, 0);
        return (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue);
    }
}
