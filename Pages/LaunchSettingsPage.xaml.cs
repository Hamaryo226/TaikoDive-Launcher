using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class LaunchSettingsPage : Page
{
    private static readonly IReadOnlyList<ResolutionOption> StandardResolutions =
    [
        new(1280, "HD  ·  1280 × 720"),
        new(1600, "HD+  ·  1600 × 900"),
        new(1920, "Full HD  ·  1920 × 1080"),
        new(2560, "WQHD  ·  2560 × 1440"),
        new(3840, "4K UHD  ·  3840 × 2160"),
    ];

    private static readonly IReadOnlyList<string> SoundTypes =
        ["DirectSound", "Wasapi", "WasapiExclusive", "ASIO"];

    private readonly GameSettingsStore _settingsStore = new();

    private App AppInstance => (App)Application.Current;

    public LaunchSettingsPage()
    {
        InitializeComponent();
        Loaded += LaunchSettingsPage_Loaded;
        Unloaded += LaunchSettingsPage_Unloaded;
    }

    private async void LaunchSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged += Context_InstallationChanged;
        SoundTypeBox.ItemsSource = SoundTypes;
        await ReloadAsync();
    }

    private void LaunchSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged -= Context_InstallationChanged;
    }

    private void Context_InstallationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () => await ReloadAsync());
    }

    private async Task ReloadAsync()
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            SetSettingsEnabled(false);
            ShowStatus(InfoBarSeverity.Warning, "ホームでゲームフォルダーを選択してください。");
            return;
        }

        SetBusy(true);
        try
        {
            GameSettings settings = await _settingsStore.LoadAsync(installation);
            List<ResolutionOption> resolutions = StandardResolutions.ToList();
            if (resolutions.All(item => item.Width != settings.ScreenWidth))
            {
                int height = (int)Math.Round(settings.ScreenWidth * 9.0 / 16.0 / 2.0) * 2;
                resolutions.Insert(0, new ResolutionOption(settings.ScreenWidth, $"カスタム  ·  {settings.ScreenWidth} × {height}"));
            }

            ResolutionBox.ItemsSource = resolutions;
            ResolutionBox.SelectedValue = settings.ScreenWidth;
            FullScreenSwitch.IsOn = settings.FullScreen;
            BorderlessSwitch.IsOn = settings.BorderlessWindow;
            VerticalSyncSwitch.IsOn = settings.VerticalSync;
            GuestModeSwitch.IsOn = settings.GuestMode;
            TwoPlayerModeSwitch.IsOn = settings.TwoPlayerMode;
            SaveReplaySwitch.IsOn = settings.SaveBestReplay;
            MasterVolumeSlider.Value = settings.MasterVolume;
            MusicVolumeSlider.Value = settings.MusicVolume;
            SoundEffectVolumeSlider.Value = settings.SoundEffectVolume;
            SoundTypeBox.SelectedItem = settings.SoundType;
            SoundBufferBox.Value = settings.SoundBufferSamples;
            CompressedSoundSwitch.IsOn = settings.UseCompressedSongSound;
            Texture16BitSwitch.IsOn = settings.ReduceTextureColorTo16bit;
            CharaTexture16BitSwitch.IsOn = settings.ReduceCharaTextureColorTo16bit;
            CharaFrameSkipBox.Value = settings.CharaAnimationFrameSkip;
            CloseAfterLaunchSwitch.IsOn = AppInstance.Context.Preferences.CloseAfterLaunch;
            SetSettingsEnabled(true);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            SetSettingsEnabled(false);
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppInstance.Context.Installation is not { } installation)
        {
            ShowStatus(InfoBarSeverity.Warning, "ホームでゲームフォルダーを選択してください。");
            return;
        }

        int screenWidth = ResolutionBox.SelectedValue is int width ? width : 1920;
        GameSettings settings = new()
        {
            GuestMode = GuestModeSwitch.IsOn,
            TwoPlayerMode = TwoPlayerModeSwitch.IsOn,
            FullScreen = FullScreenSwitch.IsOn,
            BorderlessWindow = BorderlessSwitch.IsOn,
            ScreenWidth = screenWidth,
            VerticalSync = VerticalSyncSwitch.IsOn,
            SaveBestReplay = SaveReplaySwitch.IsOn,
            MasterVolume = (int)Math.Round(MasterVolumeSlider.Value),
            MusicVolume = (int)Math.Round(MusicVolumeSlider.Value),
            SoundEffectVolume = (int)Math.Round(SoundEffectVolumeSlider.Value),
            SoundType = SoundTypeBox.SelectedItem as string ?? "DirectSound",
            SoundBufferSamples = double.IsNaN(SoundBufferBox.Value) ? 0 : (int)Math.Round(SoundBufferBox.Value),
            UseCompressedSongSound = CompressedSoundSwitch.IsOn,
            ReduceTextureColorTo16bit = Texture16BitSwitch.IsOn,
            ReduceCharaTextureColorTo16bit = CharaTexture16BitSwitch.IsOn,
            CharaAnimationFrameSkip = double.IsNaN(CharaFrameSkipBox.Value) ? 3 : (int)Math.Round(CharaFrameSkipBox.Value),
        };

        SetBusy(true);
        try
        {
            await _settingsStore.SaveAsync(installation, settings);
            AppInstance.Context.Preferences.CloseAfterLaunch = CloseAfterLaunchSwitch.IsOn;
            await AppInstance.Context.SavePreferencesAsync();
            ShowStatus(InfoBarSeverity.Success, "起動構成を保存しました。変更は次回のゲーム起動から反映されます。");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private void SetBusy(bool isBusy)
    {
        BusyRing.IsActive = isBusy;
        BusyRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetSettingsEnabled(bool isEnabled)
    {
        SettingsGrid.IsHitTestVisible = isEnabled;
        SettingsGrid.Opacity = isEnabled ? 1 : 0.55;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
