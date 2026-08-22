using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class HomePage : Page
{
    private readonly GameSettingsStore _settingsStore = new();
    private readonly UserProfileStore _profileStore = new();

    private App AppInstance => (App)Application.Current;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        OpenFolderButton.IsEnabled = installation is not null;
        LaunchButton.IsEnabled = installation is not null;

        if (installation is null)
        {
            InstallStateText.Text = "配置を確認してください";
            InstallPathText.Text = AppContext.BaseDirectory;
            PrimaryProfileText.Text = "—";
            GameSummaryText.Text = "—";
            ShowStatus(InfoBarSeverity.Warning, "このフォルダーに TaikoDive.exe がありません。ランチャーをゲーム本体の隣へ移動してください。");
            return;
        }

        InstallStateText.Text = "準備完了";
        InstallPathText.Text = installation.BuildDirectory;

        try
        {
            Task<IReadOnlyList<UserProfile>> profilesTask = _profileStore.LoadAsync(installation);
            Task<GameSettings> settingsTask = _settingsStore.LoadAsync(installation);
            await Task.WhenAll(profilesTask, settingsTask);

            UserProfile profile = profilesTask.Result[0];
            GameSettings settings = settingsTask.Result;
            PrimaryProfileText.Text = $"{profile.Name}  /  {profile.Title}";
            string windowMode = settings.FullScreen
                ? "フルスクリーン"
                : settings.BorderlessWindow ? "ボーダーレス" : "ウィンドウ";
            GameSummaryText.Text = $"{windowMode} · {settings.ScreenWidth} px · Master {settings.MasterVolume}% · {settings.SoundType}";
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            PrimaryProfileText.Text = "読み取りエラー";
            GameSummaryText.Text = "読み取りエラー";
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await AppInstance.Context.InitializeAsync();
        await RefreshAsync();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppInstance.Context.Installation is not { } installation)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(installation.BuildDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"フォルダーを開けませんでした: {ex.Message}");
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        OperationResult result = AppInstance.Context.LaunchGame();
        if (!result.Succeeded)
        {
            ContentDialog dialog = new()
            {
                Title = "起動できませんでした",
                Content = result.Message,
                CloseButtonText = "閉じる",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
            return;
        }

        if (AppInstance.Context.Preferences.CloseAfterLaunch)
        {
            AppInstance.Exit();
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
