using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

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
        Unloaded += HomePage_Unloaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged += Context_InstallationChanged;
        await RefreshAsync();
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged -= Context_InstallationChanged;
    }

    private void Context_InstallationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () => await RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        OpenFolderButton.IsEnabled = installation is not null;

        if (installation is null)
        {
            InstallStateText.Text = "セットアップが必要です";
            InstallPathText.Text = "TaikoDive.exe が入っている build フォルダー、またはその親フォルダーを選択してください。";
            PrimaryProfileText.Text = "—";
            GameSummaryText.Text = "—";
            ShowStatus(InfoBarSeverity.Warning, "ゲームフォルダーが未設定です。");
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

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, App.MainWindow.GetWindowHandle());

        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        OperationResult result = await AppInstance.Context.SetGameDirectoryAsync(folder.Path);
        if (!result.Succeeded)
        {
            ShowStatus(InfoBarSeverity.Error, result.Message);
        }
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

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
