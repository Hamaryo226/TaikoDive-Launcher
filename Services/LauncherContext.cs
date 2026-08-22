using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherContext
{
    private readonly LauncherPreferencesStore _preferencesStore = new();

    public LauncherPreferences Preferences { get; private set; } = new();

    public TaikoDiveInstallation? Installation { get; private set; }

    public async Task InitializeAsync()
    {
        Preferences = await _preferencesStore.LoadAsync().ConfigureAwait(false);
        Installation = TaikoDiveInstallation.FromApplicationDirectory();
    }

    public Task SavePreferencesAsync() => _preferencesStore.SaveAsync(Preferences);

    public OperationResult LaunchGame()
    {
        return Installation is null
            ? OperationResult.Failure("TaikoDive.Launcher.exe を TaikoDive.exe と同じフォルダーへ配置してください。")
            : GameProcessService.Launch(Installation);
    }
}
