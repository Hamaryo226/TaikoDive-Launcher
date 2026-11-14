using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public sealed class LauncherContext
{
    private readonly LauncherPreferencesStore _preferencesStore = new();
    private readonly SemaphoreSlim _preferencesSaveLock = new(1, 1);

    public PreferencesTransitionCoordinator PreferenceTransitions { get; } = new();

    public event EventHandler? PreferencesStateChanged;
    public LauncherPreferences Preferences { get; private set; } = new();
    private LauncherPreferences _confirmedPreferences = new();
    public bool CanSavePreferences => _preferencesStore.CanSave;
    public string? PreferencesProtectionReason => _preferencesStore.ProtectionReason;
    public bool HasUnsavedPreferenceEdits => Preferences.Theme != _confirmedPreferences.Theme
        || Preferences.HomeBannerStyle != _confirmedPreferences.HomeBannerStyle
        || Preferences.CloseAfterLaunch != _confirmedPreferences.CloseAfterLaunch
        || Preferences.BackgroundAnimationEnabled != _confirmedPreferences.BackgroundAnimationEnabled;

    private static LauncherPreferences Copy(LauncherPreferences source) =>
        System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(source, LauncherJsonContext.Default.LauncherPreferences),
            LauncherJsonContext.Default.LauncherPreferences)!;

    public void RevertPreferenceEdits()
    {
        var placement = Preferences.WindowPlacement;
        Preferences = Copy(_confirmedPreferences);
        Preferences.WindowPlacement = placement;
    }

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
        _confirmedPreferences = Copy(Preferences);
        RefreshInstallation();
    }

    public int PlacementGeneration { get; private set; }
    public string? PreferencesBackupPath => _preferencesStore.LastBackupPath;

    // Shared by window close and installer-triggered exit. A failed save must not prevent exit.
    public async Task PrepareCloseAsync(CancellationTokenSource? delayedSave,
        WindowPlacementPreferences? pendingPlacement, int generation)
    {
        delayedSave?.Cancel();
        if (pendingPlacement is null) return;
        try { await SavePlacementAsync(pendingPlacement, generation); }
        catch { /* Recovery remains available next launch; do not claim the save succeeded. */ }
    }

    public async Task SavePlacementAsync(WindowPlacementPreferences placement, int generation)
    {
        await _preferencesSaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != PlacementGeneration || !CanSavePreferences) return;
            var snapshot = Copy(_confirmedPreferences);
            snapshot.WindowPlacement = placement;
            await _preferencesStore.SaveAsync(snapshot).ConfigureAwait(false);
            _confirmedPreferences = snapshot;
            Preferences.WindowPlacement = Copy(snapshot).WindowPlacement;
        }
        finally { _preferencesSaveLock.Release(); PreferencesStateChanged?.Invoke(this, EventArgs.Empty); }
    }

    public async Task ResetPreferencesAsync()
    {
        await _preferencesSaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _preferencesStore.BackupAndResetAsync().ConfigureAwait(false);
            PlacementGeneration++;
            Preferences = await _preferencesStore.LoadAsync().ConfigureAwait(false);
            _confirmedPreferences = Copy(Preferences);
        }
        finally { _preferencesSaveLock.Release(); PreferencesStateChanged?.Invoke(this, EventArgs.Empty); }
    }

    public async Task<bool> ReloadPreferencesAsync()
    {
        await _preferencesSaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var loaded = await _preferencesStore.LoadAsync().ConfigureAwait(false);
            if (!_preferencesStore.CanSave) return false;
            PlacementGeneration++;
            Preferences = loaded;
            _confirmedPreferences = Copy(loaded);
            return true;
        }
        finally { _preferencesSaveLock.Release(); PreferencesStateChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void RefreshInstallation() => Installation = TaikoDiveInstallation.FromApplicationDirectory();

    public async Task SavePreferencesAsync()
    {
        await _preferencesSaveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = Copy(Preferences);
            await _preferencesStore.SaveAsync(snapshot).ConfigureAwait(false);
            _confirmedPreferences = snapshot;
        }
        finally
        {
            _preferencesSaveLock.Release();
            PreferencesStateChanged?.Invoke(this, EventArgs.Empty);
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
