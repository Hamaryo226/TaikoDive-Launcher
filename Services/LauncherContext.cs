using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherContext
{
    private readonly LauncherPreferencesStore _preferencesStore = new();
    private readonly SemaphoreSlim _preferencesSaveLock = new(1, 1);

    public LauncherPreferences Preferences { get; private set; } = new();

    public TaikoDiveInstallation? Installation { get; private set; }

    public LauncherUpdateService Updates { get; } = new();

    public GameUpdateService GameUpdates { get; }

    public GameUpdateService AssetUpdates { get; }

    public LauncherContext()
    {
        GameUpdates = new GameUpdateService(() => Installation);
        AssetUpdates = GameUpdateService.CreateAssets(() => Installation);
    }

    public async Task InitializeAsync()
    {
        Preferences = await _preferencesStore.LoadAsync().ConfigureAwait(false);
        RefreshInstallation();
    }

    public void RefreshInstallation() => Installation = TaikoDiveInstallation.FromApplicationDirectory();

    public async Task SavePreferencesAsync()
    {
        await _preferencesSaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _preferencesStore.SaveAsync(Preferences).ConfigureAwait(false);
        }
        finally
        {
            _preferencesSaveLock.Release();
        }
    }

    public async Task<OperationResult> LaunchGameAsync()
    {
        if (Installation is null)
        {
            return OperationResult.Failure("TaikoDive.Launcher.exe を TaikoDive.exe と同じフォルダーへ配置してください。");
        }

        try
        {
            await new Character3DStore().EnsureModelsPathAsync(Installation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return OperationResult.Failure($"Info/Chara3D.json を確認できないため起動を中止しました: {ex.Message}");
        }

        return GameProcessService.Launch(Installation);
    }
}
