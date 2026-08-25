using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class LauncherSettingsPage : Page
{
    private bool _isLoading;

    private App AppInstance => (App)Application.Current;

    public LauncherSettingsPage()
    {
        InitializeComponent();
        Loaded += LauncherSettingsPage_Loaded;
        Unloaded += LauncherSettingsPage_Unloaded;
    }

    private void LauncherSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        CloseAfterLaunchSwitch.IsOn = AppInstance.Context.Preferences.CloseAfterLaunch;
        UpdateThemeButton();
        AppInstance.Context.Updates.StateChanged -= Updates_StateChanged;
        AppInstance.Context.Updates.StateChanged += Updates_StateChanged;
        AppInstance.Context.GameUpdates.StateChanged -= GameUpdates_StateChanged;
        AppInstance.Context.GameUpdates.StateChanged += GameUpdates_StateChanged;
        AppInstance.Context.AssetUpdates.StateChanged -= AssetUpdates_StateChanged;
        AppInstance.Context.AssetUpdates.StateChanged += AssetUpdates_StateChanged;
        UpdateUpdateControls();
        UpdateGameUpdateControls();
        UpdateAssetUpdateControls();
        _isLoading = false;
    }

    private void LauncherSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.Updates.StateChanged -= Updates_StateChanged;
        AppInstance.Context.GameUpdates.StateChanged -= GameUpdates_StateChanged;
        AppInstance.Context.AssetUpdates.StateChanged -= AssetUpdates_StateChanged;
    }

    private async void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        string previousTheme = AppInstance.Context.Preferences.Theme;
        bool isDark = !string.Equals(previousTheme, "Light", StringComparison.OrdinalIgnoreCase);
        AppInstance.Context.Preferences.Theme = isDark ? "Light" : "Dark";
        App.MainWindow.ApplyTheme(AppInstance.Context.Preferences.Theme);
        UpdateThemeButton();

        try
        {
            await AppInstance.Context.SavePreferencesAsync();
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            AppInstance.Context.Preferences.Theme = previousTheme;
            App.MainWindow.ApplyTheme(previousTheme);
            UpdateThemeButton();
            ShowStatus(InfoBarSeverity.Error, $"テーマを保存できませんでした: {ex.Message}");
        }
    }

    private async void CloseAfterLaunchSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        bool previousValue = AppInstance.Context.Preferences.CloseAfterLaunch;
        AppInstance.Context.Preferences.CloseAfterLaunch = CloseAfterLaunchSwitch.IsOn;
        try
        {
            await AppInstance.Context.SavePreferencesAsync();
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            AppInstance.Context.Preferences.CloseAfterLaunch = previousValue;
            _isLoading = true;
            CloseAfterLaunchSwitch.IsOn = previousValue;
            _isLoading = false;
            ShowStatus(InfoBarSeverity.Error, $"起動動作を保存できませんでした: {ex.Message}");
        }
    }

    private void UpdateThemeButton()
    {
        bool isLight = string.Equals(
            AppInstance.Context.Preferences.Theme,
            "Light",
            StringComparison.OrdinalIgnoreCase);
        ThemeIcon.Glyph = isLight ? "\uE706" : "\uE708";
        string accessibleName = isLight
            ? "ホワイトモード。ダークモードへ切り替え"
            : "ダークモード。ホワイトモードへ切り替え";
        AutomationProperties.SetName(ThemeToggleButton, accessibleName);
        ToolTipService.SetToolTip(ThemeToggleButton, accessibleName);
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await AppInstance.Context.Updates.CheckAsync();
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog confirmation = new()
        {
            Title = "ランチャーをアップデートしますか？",
            Content = "更新を検証して適用した後、TaikoDive Launcherを自動的に再起動します。",
            PrimaryButtonText = "アップデート",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        OperationResult result = await AppInstance.Context.Updates.DownloadAndStartInstallerAsync();
        if (result.Succeeded)
        {
            AppInstance.Exit();
            return;
        }

        ShowStatus(InfoBarSeverity.Error, $"アップデートできませんでした: {result.Message}");
    }

    private void Updates_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUpdateControls);
    }

    private void UpdateUpdateControls()
    {
        LauncherUpdateService updates = AppInstance.Context.Updates;
        CurrentVersionText.Text = $"現在のバージョン: {updates.CurrentVersionText}";
        UpdateStatusText.Text = updates.StatusMessage;
        bool isBusy = updates.State is
            LauncherUpdateState.Checking or
            LauncherUpdateState.Downloading or
            LauncherUpdateState.StartingInstaller;
        UpdateProgressRing.IsActive = isBusy;
        CheckUpdateButton.IsEnabled = !isBusy;
        InstallUpdateButton.IsEnabled = updates.State == LauncherUpdateState.Available;
    }

    private async void CheckGameUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await AppInstance.Context.GameUpdates.CheckAsync();
    }

    private async void InstallGameUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameProcessService.IsRunning())
        {
            ShowStatus(InfoBarSeverity.Warning, "TaikoDiveを終了してからアップデートしてください。");
            return;
        }

        GameUpdateManifest? update = AppInstance.Context.GameUpdates.AvailableUpdate;
        ContentDialog confirmation = new()
        {
            Title = "TaikoDiveをアップデートしますか？",
            Content = $"v{update?.Version}を検証して適用します。ユーザー設定、プロフィール、Songs、スコア、リプレイは保持されます。",
            PrimaryButtonText = "アップデート",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        OperationResult result = await AppInstance.Context.GameUpdates.DownloadAndApplyAsync();
        ShowStatus(
            result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error,
            result.Message);
    }

    private void GameUpdates_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateGameUpdateControls);
    }

    private void UpdateGameUpdateControls()
    {
        GameUpdateService updates = AppInstance.Context.GameUpdates;
        CurrentGameVersionText.Text = $"現在のバージョン: v{updates.CurrentVersionText}";
        GameUpdateStatusText.Text = updates.StatusMessage;
        bool isBusy = updates.State is
            GameUpdateState.Checking or
            GameUpdateState.Downloading or
            GameUpdateState.Verifying or
            GameUpdateState.Applying;
        GameUpdateProgressRing.IsActive = isBusy;
        CheckGameUpdateButton.IsEnabled = !isBusy;
        InstallGameUpdateButton.IsEnabled = updates.State == GameUpdateState.Available;
    }

    private async void CheckAssetUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await AppInstance.Context.AssetUpdates.CheckAsync();
    }

    private async void InstallAssetUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (GameProcessService.IsRunning())
        {
            ShowStatus(InfoBarSeverity.Warning, "TaikoDiveを終了してからAssetをアップデートしてください。");
            return;
        }

        GameUpdateManifest? update = AppInstance.Context.AssetUpdates.AvailableUpdate;
        ContentDialog confirmation = new()
        {
            Title = "TaikoDive Assetをアップデートしますか？",
            Content = $"v{update?.Version}を検証し、Assetリポジトリのsrcの内容をbuildへ適用します。Info/User.iniなどのユーザーデータは保持されます。",
            PrimaryButtonText = "アップデート",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        OperationResult result = await AppInstance.Context.AssetUpdates.DownloadAndApplyAsync();
        ShowStatus(
            result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error,
            result.Message);
    }

    private void AssetUpdates_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateAssetUpdateControls);
    }

    private void UpdateAssetUpdateControls()
    {
        GameUpdateService updates = AppInstance.Context.AssetUpdates;
        CurrentAssetVersionText.Text = $"現在のバージョン: v{updates.CurrentVersionText}";
        AssetUpdateStatusText.Text = updates.StatusMessage;
        bool isBusy = updates.State is
            GameUpdateState.Checking or
            GameUpdateState.Downloading or
            GameUpdateState.Verifying or
            GameUpdateState.Applying;
        AssetUpdateProgressRing.IsActive = isBusy;
        CheckAssetUpdateButton.IsEnabled = !isBusy;
        InstallAssetUpdateButton.IsEnabled = updates.State == GameUpdateState.Available;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
