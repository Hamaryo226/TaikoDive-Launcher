using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherContext
{
    private readonly LauncherPreferencesStore _preferencesStore = new();
    private readonly SemaphoreSlim _preferencesSaveLock = new(1, 1);

    public LauncherPreferences Preferences { get; private set; } = new();

    public TaikoDiveInstallation? Installation { get; private set; }

    public LauncherUpdateService Updates { get; } = new();

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

    public OperationResult LaunchGame()
    {
        return Installation is null
            ? OperationResult.Failure("TaikoDive.Launcher.exe を TaikoDive.exe と同じフォルダーへ配置してください。")
            : GameProcessService.Launch(Installation);
    }
}
