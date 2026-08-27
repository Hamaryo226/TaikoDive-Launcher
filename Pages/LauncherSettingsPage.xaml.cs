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
        UpdateDescriptionText.Text = FormatUpdateNotes(updates.LatestUpdate?.ReleaseNotes);
        bool isBusy = updates.State is
            LauncherUpdateState.Checking or
            LauncherUpdateState.Downloading or
            LauncherUpdateState.StartingInstaller;
        UpdateProgressRing.IsActive = isBusy;
        UpdateProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressControls(
            UpdateProgressPanel,
            UpdateProgressBar,
            UpdateProgressText,
            isBusy,
            updates.ProgressPercentage,
            "ランチャー");
        UpdateStatusIcon.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatusIcon.Symbol = GetStatusSymbol(updates.State);
        UpdateAvailableBadge.Visibility = updates.State == LauncherUpdateState.Available
            ? Visibility.Visible
            : Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = !isBusy;
        InstallUpdateButton.IsEnabled = updates.State == LauncherUpdateState.Available;
        AutomationProperties.SetName(
            InstallUpdateButton,
            updates.State == LauncherUpdateState.Available
                ? "ランチャーのアップデートを適用"
                : "ランチャーのアップデートはありません");
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
        GameUpdateDescriptionText.Text = FormatUpdateNotes(updates.LatestUpdate?.ReleaseNotes);
        bool isBusy = updates.State is
            GameUpdateState.Checking or
            GameUpdateState.Downloading or
            GameUpdateState.Verifying or
            GameUpdateState.Applying;
        GameUpdateProgressRing.IsActive = isBusy;
        GameUpdateProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressControls(
            GameUpdateProgressPanel,
            GameUpdateProgressBar,
            GameUpdateProgressText,
            isBusy,
            updates.ProgressPercentage,
            "TaikoDive");
        GameUpdateStatusIcon.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        GameUpdateStatusIcon.Symbol = GetStatusSymbol(updates.State);
        GameUpdateAvailableBadge.Visibility = updates.State == GameUpdateState.Available
            ? Visibility.Visible
            : Visibility.Collapsed;
        CheckGameUpdateButton.IsEnabled = !isBusy;
        InstallGameUpdateButton.IsEnabled = updates.State == GameUpdateState.Available;
        AutomationProperties.SetName(
            InstallGameUpdateButton,
            updates.State == GameUpdateState.Available
                ? "TaikoDiveのアップデートを適用"
                : "TaikoDiveのアップデートはありません");
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
        AssetUpdateDescriptionText.Text = FormatUpdateNotes(updates.LatestUpdate?.ReleaseNotes);
        bool isBusy = updates.State is
            GameUpdateState.Checking or
            GameUpdateState.Downloading or
            GameUpdateState.Verifying or
            GameUpdateState.Applying;
        AssetUpdateProgressRing.IsActive = isBusy;
        AssetUpdateProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressControls(
            AssetUpdateProgressPanel,
            AssetUpdateProgressBar,
            AssetUpdateProgressText,
            isBusy,
            updates.ProgressPercentage,
            "TaikoDive Asset");
        AssetUpdateStatusIcon.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        AssetUpdateStatusIcon.Symbol = GetStatusSymbol(updates.State);
        AssetUpdateAvailableBadge.Visibility = updates.State == GameUpdateState.Available
            ? Visibility.Visible
            : Visibility.Collapsed;
        CheckAssetUpdateButton.IsEnabled = !isBusy;
        InstallAssetUpdateButton.IsEnabled = updates.State == GameUpdateState.Available;
        AutomationProperties.SetName(
            InstallAssetUpdateButton,
            updates.State == GameUpdateState.Available
                ? "TaikoDive Assetのアップデートを適用"
                : "TaikoDive Assetのアップデートはありません");
    }

    private static string FormatUpdateNotes(string? releaseNotes)
    {
        return string.IsNullOrWhiteSpace(releaseNotes)
            ? "アップデート内容は更新確認後に表示されます。"
            : releaseNotes.Trim();
    }

    private static void UpdateProgressControls(
        Grid panel,
        ProgressBar progressBar,
        TextBlock progressText,
        bool isBusy,
        double? percentage,
        string updateName)
    {
        panel.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        bool isDeterminate = percentage.HasValue;
        progressBar.IsIndeterminate = isBusy && !isDeterminate;
        progressBar.Value = percentage ?? 0;
        progressText.Text = isDeterminate ? $"{percentage!.Value:0}%" : "処理中";
        AutomationProperties.SetName(
            progressBar,
            isDeterminate
                ? $"{updateName}のアップデート進捗 {percentage!.Value:0}%"
                : $"{updateName}のアップデートを処理中");
    }

    private static Symbol GetStatusSymbol(LauncherUpdateState state) => state switch
    {
        LauncherUpdateState.Available => Symbol.Download,
        LauncherUpdateState.UpToDate => Symbol.Accept,
        LauncherUpdateState.Failed => Symbol.Important,
        _ => Symbol.Help,
    };

    private static Symbol GetStatusSymbol(GameUpdateState state) => state switch
    {
        GameUpdateState.Available => Symbol.Download,
        GameUpdateState.UpToDate or GameUpdateState.Completed => Symbol.Accept,
        GameUpdateState.Failed => Symbol.Important,
        _ => Symbol.Help,
    };

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
