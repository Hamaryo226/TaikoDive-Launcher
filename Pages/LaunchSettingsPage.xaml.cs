using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class LaunchSettingsPage : Page, IUnsavedChangesAware
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

    private static readonly IReadOnlyList<BackgroundMovieLayoutOption> BackgroundMovieLayouts =
    [
        new("FullScreen", "全画面"),
        new("BlurredWithInset", "ぼかし背景＋元の比率"),
    ];

    private readonly GameSettingsStore _settingsStore = new();
    private GameSettings? _loadedSettings;

    public bool HasUnsavedChanges => _loadedSettings is not null
        && !SettingsEqual(_loadedSettings, ReadSettings());

    public string UnsavedChangesName => "起動構成";

    private App AppInstance => (App)Application.Current;

    public LaunchSettingsPage()
    {
        InitializeComponent();
        Loaded += LaunchSettingsPage_Loaded;
    }

    private async void LaunchSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        SoundTypeBox.ItemsSource = SoundTypes;
        BackgroundMovieLayoutBox.ItemsSource = BackgroundMovieLayouts;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            SetSettingsEnabled(false);
            ShowStatus(InfoBarSeverity.Warning, "ランチャーを TaikoDive.exe と同じフォルダーへ配置してください。");
            return;
        }

        SetBusy(true);
        try
        {
            GameSettings settings = await _settingsStore.LoadAsync(installation);
            _loadedSettings = settings;
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
            TitleShowSwitch.IsOn = settings.TitleShow;
            CollaboBackSwitch.IsOn = settings.CollaboBack;
            GuestModeSwitch.IsOn = settings.GuestMode;
            TwoPlayerModeSwitch.IsOn = settings.TwoPlayerMode;
            SaveReplaySwitch.IsOn = settings.SaveBestReplay;
            FreePlaySwitch.IsOn = settings.FreePlay;
            BackgroundMovieLayoutBox.SelectedValue = settings.BackgroundMovieLayoutMode;
            MasterVolumeSlider.Value = settings.MasterVolume;
            MusicVolumeSlider.Value = settings.MusicVolume;
            SoundEffectVolumeSlider.Value = settings.SoundEffectVolume;
            SoundTypeBox.SelectedItem = settings.SoundType;
            SoundBufferBox.Value = settings.SoundBufferSamples;
            FontOedoBox.Text = settings.FontOedo;
            FontDFGothicBox.Text = settings.FontDFGothic;
            FontSeuratBox.Text = settings.FontSeurat;
            FontDomCasualBox.Text = settings.FontDomCasual;
            FontFallbackBox.Text = settings.FontFallback;
            CompressedSoundSwitch.IsOn = settings.UseCompressedSongSound;
            Texture16BitSwitch.IsOn = settings.ReduceTextureColorTo16bit;
            CharaTexture16BitSwitch.IsOn = settings.ReduceCharaTextureColorTo16bit;
            BgTexture16BitSwitch.IsOn = settings.ReduceBgTextureColorTo16bit;
            CharaFrameSkipBox.Value = settings.CharaAnimationFrameSkip;
            OnlinePortBox.Value = settings.OnlinePort;
            LastJoinAddressBox.Text = settings.LastJoinAddress;
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
            ShowStatus(InfoBarSeverity.Warning, "ランチャーを TaikoDive.exe と同じフォルダーへ配置してください。");
            return;
        }

        GameSettings settings = ReadSettings();

        SetBusy(true);
        try
        {
            await _settingsStore.SaveAsync(installation, settings);
            _loadedSettings = settings;
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

    private GameSettings ReadSettings()
    {
        return new GameSettings
        {
            GuestMode = GuestModeSwitch.IsOn,
            TwoPlayerMode = TwoPlayerModeSwitch.IsOn,
            FullScreen = FullScreenSwitch.IsOn,
            BorderlessWindow = BorderlessSwitch.IsOn,
            ScreenWidth = ResolutionBox.SelectedValue is int width ? width : 1920,
            VerticalSync = VerticalSyncSwitch.IsOn,
            TitleShow = TitleShowSwitch.IsOn,
            CollaboBack = CollaboBackSwitch.IsOn,
            SaveBestReplay = SaveReplaySwitch.IsOn,
            FreePlay = FreePlaySwitch.IsOn,
            BackgroundMovieLayoutMode = BackgroundMovieLayoutBox.SelectedValue as string ?? "FullScreen",
            MasterVolume = (int)Math.Round(MasterVolumeSlider.Value),
            MusicVolume = (int)Math.Round(MusicVolumeSlider.Value),
            SoundEffectVolume = (int)Math.Round(SoundEffectVolumeSlider.Value),
            SoundType = SoundTypeBox.SelectedItem as string ?? "DirectSound",
            SoundBufferSamples = double.IsNaN(SoundBufferBox.Value) ? 0 : (int)Math.Round(SoundBufferBox.Value),
            FontOedo = FontOedoBox.Text,
            FontDFGothic = FontDFGothicBox.Text,
            FontSeurat = FontSeuratBox.Text,
            FontDomCasual = FontDomCasualBox.Text,
            FontFallback = FontFallbackBox.Text,
            UseCompressedSongSound = CompressedSoundSwitch.IsOn,
            ReduceTextureColorTo16bit = Texture16BitSwitch.IsOn,
            ReduceCharaTextureColorTo16bit = CharaTexture16BitSwitch.IsOn,
            ReduceBgTextureColorTo16bit = BgTexture16BitSwitch.IsOn,
            CharaAnimationFrameSkip = double.IsNaN(CharaFrameSkipBox.Value) ? 3 : (int)Math.Round(CharaFrameSkipBox.Value),
            OnlinePort = double.IsNaN(OnlinePortBox.Value) ? 22047 : (int)Math.Round(OnlinePortBox.Value),
            LastJoinAddress = LastJoinAddressBox.Text,
        };
    }

    private static bool SettingsEqual(GameSettings left, GameSettings right)
    {
        return left.GuestMode == right.GuestMode
            && left.TwoPlayerMode == right.TwoPlayerMode
            && left.FullScreen == right.FullScreen
            && left.BorderlessWindow == right.BorderlessWindow
            && left.ScreenWidth == right.ScreenWidth
            && left.VerticalSync == right.VerticalSync
            && left.TitleShow == right.TitleShow
            && left.CollaboBack == right.CollaboBack
            && left.SaveBestReplay == right.SaveBestReplay
            && left.FreePlay == right.FreePlay
            && left.BackgroundMovieLayoutMode == right.BackgroundMovieLayoutMode
            && left.MasterVolume == right.MasterVolume
            && left.MusicVolume == right.MusicVolume
            && left.SoundEffectVolume == right.SoundEffectVolume
            && left.SoundType == right.SoundType
            && left.SoundBufferSamples == right.SoundBufferSamples
            && left.FontOedo == right.FontOedo
            && left.FontDFGothic == right.FontDFGothic
            && left.FontSeurat == right.FontSeurat
            && left.FontDomCasual == right.FontDomCasual
            && left.FontFallback == right.FontFallback
            && left.UseCompressedSongSound == right.UseCompressedSongSound
            && left.ReduceTextureColorTo16bit == right.ReduceTextureColorTo16bit
            && left.ReduceCharaTextureColorTo16bit == right.ReduceCharaTextureColorTo16bit
            && left.ReduceBgTextureColorTo16bit == right.ReduceBgTextureColorTo16bit
            && left.CharaAnimationFrameSkip == right.CharaAnimationFrameSkip
            && left.OnlinePort == right.OnlinePort
            && left.LastJoinAddress == right.LastJoinAddress;
    }

    private sealed record BackgroundMovieLayoutOption(string Value, string Label);
}
