using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class GameSettingsStore
{
    private readonly Func<bool> _isGameRunning;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public GameSettingsStore() : this(GameProcessService.IsRunning)
    {
    }

    internal GameSettingsStore(Func<bool> isGameRunning)
    {
        _isGameRunning = isGameRunning;
    }

    public async Task<GameSettings> LoadAsync(TaikoDiveInstallation installation)
    {
        JsonObject root = await LoadRootAsync(installation.GameSettingsPath).ConfigureAwait(false);
        return new GameSettings
        {
            GuestMode = GetBoolean(root, "guestMode", true),
            TwoPlayerMode = GetBoolean(root, "b2PMode", false),
            FullScreen = GetBoolean(root, "fullScreen", false),
            BorderlessWindow = GetBoolean(root, "borderlessWindow", false),
            ScreenWidth = GetInteger(root, "screenWidth", GetBoolean(root, "fullHD", true) ? 1920 : 1280),
            VerticalSync = GetBoolean(root, "verticalSync", true),
            TitleShow = GetBoolean(root, "titleShow", true),
            CollaboBack = GetBoolean(root, "collaboBack", false),
            SaveBestReplay = GetBoolean(root, "saveBestReplay", true),
            FreePlay = GetBoolean(root, "freePlay", false),
            MasterVolume = Math.Clamp(GetInteger(root, "masterVolume", 100), 0, 100),
            MusicVolume = Math.Clamp(GetInteger(root, "musicVolume", 100), 0, 100),
            SoundEffectVolume = Math.Clamp(GetInteger(root, "soundEffectVolume", 100), 0, 100),
            BackgroundMovieLayoutMode = NormalizeBackgroundMovieLayoutMode(
                GetString(root, "backgroundMovieLayoutMode", "FullScreen")),
            SoundType = GetString(root, "soundType", "DirectSound"),
            SoundBufferSamples = Math.Clamp(GetInteger(root, "soundBufferSamples", 0), 0, 8192),
            FontOedo = GetString(root, "fontOedo", "FOT-大江戸勘亭流 Std E"),
            FontDFGothic = GetString(root, "fontDFGothic", "ＤＦ太丸ゴシック体 Pro-5"),
            FontSeurat = GetString(root, "fontSeurat", "FOT-スーラ Pro B"),
            FontDomCasual = GetString(root, "fontDomCasual", "Dom Casual"),
            FontFallback = GetString(root, "fontFallback", "Comic Sans MS"),
            ReduceTextureColorTo16bit = GetBoolean(root, "reduceTextureColorTo16bit", false),
            CharaAnimationFrameSkip = Math.Clamp(GetInteger(root, "charaAnimationFrameSkip", 3), 1, 4),
            ReduceCharaTextureColorTo16bit = GetBoolean(root, "reduceCharaTextureColorTo16bit", false),
            ReduceBgTextureColorTo16bit = GetBoolean(root, "reduceBgTextureColorTo16bit", false),
            UseCompressedSongSound = GetBoolean(root, "useCompressedSongSound", true),
            OnlinePort = Math.Clamp(GetInteger(root, "onlinePort", 22047), 1, 65535),
            LastJoinAddress = GetString(root, "lastJoinAddress", string.Empty),
        };
    }

    public async Task SaveAsync(TaikoDiveInstallation installation, GameSettings settings)
    {
        if (_isGameRunning())
        {
            throw new InvalidOperationException("TaikoDive の実行中は設定を保存できません。ゲームを終了してから再試行してください。");
        }

        JsonObject root = await LoadRootAsync(installation.GameSettingsPath).ConfigureAwait(false);
        Set(root, "guestMode", settings.GuestMode);
        Set(root, "b2PMode", settings.TwoPlayerMode);
        Set(root, "fullScreen", settings.FullScreen);
        Set(root, "borderlessWindow", settings.BorderlessWindow);
        Set(root, "fullHD", settings.ScreenWidth >= 1920);
        Set(root, "screenWidth", Math.Clamp(settings.ScreenWidth, 640, 7680));
        Set(root, "verticalSync", settings.VerticalSync);
        Set(root, "titleShow", settings.TitleShow);
        Set(root, "collaboBack", settings.CollaboBack);
        Set(root, "saveBestReplay", settings.SaveBestReplay);
        Set(root, "freePlay", settings.FreePlay);
        Set(root, "masterVolume", Math.Clamp(settings.MasterVolume, 0, 100));
        Set(root, "musicVolume", Math.Clamp(settings.MusicVolume, 0, 100));
        Set(root, "soundEffectVolume", Math.Clamp(settings.SoundEffectVolume, 0, 100));
        Set(root, "backgroundMovieLayoutMode", NormalizeBackgroundMovieLayoutMode(settings.BackgroundMovieLayoutMode));
        Set(root, "soundType", NormalizeSoundType(settings.SoundType));
        Set(root, "soundBufferSamples", NormalizeBufferSize(settings.SoundBufferSamples));
        Set(root, "fontOedo", settings.FontOedo);
        Set(root, "fontDFGothic", settings.FontDFGothic);
        Set(root, "fontSeurat", settings.FontSeurat);
        Set(root, "fontDomCasual", settings.FontDomCasual);
        Set(root, "fontFallback", settings.FontFallback);
        Set(root, "reduceTextureColorTo16bit", settings.ReduceTextureColorTo16bit);
        Set(root, "charaAnimationFrameSkip", Math.Clamp(settings.CharaAnimationFrameSkip, 1, 4));
        Set(root, "reduceCharaTextureColorTo16bit", settings.ReduceCharaTextureColorTo16bit);
        Set(root, "reduceBgTextureColorTo16bit", settings.ReduceBgTextureColorTo16bit);
        Set(root, "useCompressedSongSound", settings.UseCompressedSongSound);
        Set(root, "onlinePort", Math.Clamp(settings.OnlinePort, 1, 65535));
        Set(root, "lastJoinAddress", settings.LastJoinAddress);

        string json = root.ToJsonString(SerializerOptions) + Environment.NewLine;
        await FilePersistence.WriteTextAtomicAsync(
            installation.GameSettingsPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
    }

    private static async Task<JsonObject> LoadRootAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        try
        {
            return JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("Setting.json のルートが JSON オブジェクトではありません。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Setting.json を読み取れません。JSON の形式を確認してください。", ex);
        }
    }

    private static bool GetBoolean(JsonObject root, string name, bool fallback)
    {
        JsonNode? value = Find(root, name);
        if (value is null)
        {
            return fallback;
        }

        try
        {
            return value.GetValue<bool>();
        }
        catch
        {
            return fallback;
        }
    }

    private static int GetInteger(JsonObject root, string name, int fallback)
    {
        JsonNode? value = Find(root, name);
        if (value is null)
        {
            return fallback;
        }

        try
        {
            return value.GetValue<int>();
        }
        catch
        {
            try
            {
                return checked((int)value.GetValue<long>());
            }
            catch
            {
                return fallback;
            }
        }
    }

    private static string GetString(JsonObject root, string name, string fallback)
    {
        JsonNode? value = Find(root, name);
        if (value is null)
        {
            return fallback;
        }

        try
        {
            return value.GetValue<string>();
        }
        catch
        {
            return fallback;
        }
    }

    private static JsonNode? Find(JsonObject root, string name)
    {
        foreach ((string key, JsonNode? value) in root)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static void Set(JsonObject root, string name, bool value)
    {
        root[FindActualName(root, name)] = value;
    }

    private static void Set(JsonObject root, string name, int value)
    {
        root[FindActualName(root, name)] = value;
    }

    private static void Set(JsonObject root, string name, string value)
    {
        root[FindActualName(root, name)] = value;
    }

    private static string FindActualName(JsonObject root, string name)
    {
        return root
            .Select(property => property.Key)
            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            ?? name;
    }

    private static string NormalizeSoundType(string? value)
    {
        string[] allowed = ["DirectSound", "Wasapi", "WasapiExclusive", "ASIO"];
        return allowed.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            ?? "DirectSound";
    }

    private static string NormalizeBackgroundMovieLayoutMode(string? value)
    {
        string[] allowed = ["FullScreen", "BlurredWithInset"];
        return allowed.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            ?? "FullScreen";
    }

    private static int NormalizeBufferSize(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return Math.Clamp(value, 32, 8192);
    }
}
