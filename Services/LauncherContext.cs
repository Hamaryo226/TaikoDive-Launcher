using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherContext
{
    private readonly LauncherPreferencesStore _preferencesStore = new();

    public LauncherPreferences Preferences { get; private set; } = new();

    public TaikoDiveInstallation? Installation { get; private set; }

    public event EventHandler? InstallationChanged;

    public async Task InitializeAsync()
    {
        Preferences = await _preferencesStore.LoadAsync().ConfigureAwait(false);
        Installation = TaikoDiveInstallation.FromSelectedDirectory(Preferences.GameDirectory)
            ?? TaikoDiveInstallation.Discover();

        if (Installation is not null &&
            !string.Equals(Preferences.GameDirectory, Installation.BuildDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Preferences.GameDirectory = Installation.BuildDirectory;
            await _preferencesStore.SaveAsync(Preferences).ConfigureAwait(false);
        }
    }

    public async Task<OperationResult> SetGameDirectoryAsync(string selectedDirectory)
    {
        TaikoDiveInstallation? installation = TaikoDiveInstallation.FromSelectedDirectory(selectedDirectory);
        if (installation is null)
        {
            return OperationResult.Failure("選択したフォルダーに TaikoDive.exe または build\\TaikoDive.exe がありません。");
        }

        Installation = installation;
        Preferences.GameDirectory = installation.BuildDirectory;
        await _preferencesStore.SaveAsync(Preferences).ConfigureAwait(false);
        InstallationChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult.Success("ゲームフォルダーを更新しました。");
    }

    public Task SavePreferencesAsync() => _preferencesStore.SaveAsync(Preferences);

    public OperationResult LaunchGame()
    {
        return Installation is null
            ? OperationResult.Failure("先に TaikoDive のゲームフォルダーを選択してください。")
            : GameProcessService.Launch(Installation);
    }
}
