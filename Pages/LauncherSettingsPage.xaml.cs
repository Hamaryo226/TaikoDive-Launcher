using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class LauncherSettingsPage : Page
{
    private sealed record BannerStyleOption(string Label, string Value);

    private static readonly BannerStyleOption[] BannerStyles =
    [
        new("オーシャン · シアン", "Ocean"),
        new("オーロラ · バイオレット", "Violet"),
        new("サンセット · コーラル", "Sunset"),
    ];

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
        BannerStyleBox.ItemsSource = BannerStyles;
        BannerStyleBox.SelectedValue = AppInstance.Context.Preferences.HomeBannerStyle;
        if (BannerStyleBox.SelectedItem is null)
        {
            BannerStyleBox.SelectedIndex = 0;
        }
        UpdateBannerPreview();
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
        UpdatePreferencesSummary();
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
            UpdatePreferencesSummary();
            ShowStatus(InfoBarSeverity.Error, $"起動動作を保存できませんでした: {ex.Message}");
        }
    }

    private async void BannerStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || BannerStyleBox.SelectedValue is not string selected)
        {
            return;
        }

        string previous = AppInstance.Context.Preferences.HomeBannerStyle;
        AppInstance.Context.Preferences.HomeBannerStyle = selected;
        UpdateBannerPreview();
        UpdatePreferencesSummary();
        try
        {
            await AppInstance.Context.SavePreferencesAsync();
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            AppInstance.Context.Preferences.HomeBannerStyle = previous;
            _isLoading = true;
            BannerStyleBox.SelectedValue = previous;
            _isLoading = false;
            UpdateBannerPreview();
            UpdatePreferencesSummary();
            ShowStatus(InfoBarSeverity.Error, $"ホームバナーを保存できませんでした: {ex.Message}");
        }
    }

    private void UpdateBannerPreview()
    {
        string name = BannerStyleBox.SelectedValue switch
        {
            "Violet" => "HeroGradientViolet",
            "Sunset" => "HeroGradientSunset",
            _ => "HeroGradient",
        };
        ((ImageBrush)BannerPreview.Background).ImageSource =
            new BitmapImage(new Uri($"ms-appx:///Assets/{name}A.jpg"));
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
        UpdatePreferencesSummary();
    }

    private void UpdatePreferencesSummary()
    {
        string theme = string.Equals(AppInstance.Context.Preferences.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "ライト"
            : "ダーク";
        string afterLaunch = AppInstance.Context.Preferences.CloseAfterLaunch
            ? "ゲーム起動後に終了"
            : "ゲーム中もランチャーを表示";
        string banner = BannerStyles.FirstOrDefault(option => option.Value == AppInstance.Context.Preferences.HomeBannerStyle)?.Label
            ?? BannerStyles[0].Label;
        LauncherPreferenceSummaryText.Text = $"現在: {theme} · {banner} · {afterLaunch}（変更時に保存）";
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
        UpdateDescriptionText.Text = FormatUpdateNotes(
            updates.LatestUpdate?.ReleaseNotes,
            updates.LatestUpdate is not null);
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
        bool hasInstallation = AppInstance.Context.Installation is not null;
        CurrentGameVersionText.Text = $"現在のバージョン: v{updates.CurrentVersionText}";
        GameUpdateStatusText.Text = hasInstallation ? updates.StatusMessage : string.Empty;
        GameUpdateDescriptionText.Text = FormatUpdateNotes(
            updates.LatestUpdate?.ReleaseNotes,
            updates.LatestUpdate is not null);
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
        GameUpdateStatusIcon.Visibility = isBusy || !hasInstallation ? Visibility.Collapsed : Visibility.Visible;
        GameUpdateStatusIcon.Symbol = GetStatusSymbol(updates.State);
        GameUpdateAvailableBadge.Visibility = updates.State == GameUpdateState.Available
            ? Visibility.Visible
            : Visibility.Collapsed;
        CheckGameUpdateButton.IsEnabled = hasInstallation && !isBusy;
        InstallGameUpdateButton.IsEnabled = hasInstallation && updates.State == GameUpdateState.Available;
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
        bool hasInstallation = AppInstance.Context.Installation is not null;
        CurrentAssetVersionText.Text = $"現在のバージョン: v{updates.CurrentVersionText}";
        AssetUpdateStatusText.Text = hasInstallation ? updates.StatusMessage : string.Empty;
        AssetUpdateDescriptionText.Text = FormatUpdateNotes(
            updates.LatestUpdate?.ReleaseNotes,
            updates.LatestUpdate is not null);
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
        AssetUpdateStatusIcon.Visibility = isBusy || !hasInstallation ? Visibility.Collapsed : Visibility.Visible;
        AssetUpdateStatusIcon.Symbol = GetStatusSymbol(updates.State);
        AssetUpdateAvailableBadge.Visibility = updates.State == GameUpdateState.Available
            ? Visibility.Visible
            : Visibility.Collapsed;
        CheckAssetUpdateButton.IsEnabled = hasInstallation && !isBusy;
        InstallAssetUpdateButton.IsEnabled = hasInstallation && updates.State == GameUpdateState.Available;
        AutomationProperties.SetName(
            InstallAssetUpdateButton,
            updates.State == GameUpdateState.Available
                ? "TaikoDive Assetのアップデートを適用"
                : "TaikoDive Assetのアップデートはありません");
    }

    private const int MaximumListedIntegrityFiles = 200;

    private readonly List<GameIntegrityReport> _integrityReports = [];
    private string? _modelSettingsIssue;
    private int _integrityPercentage = -1;

    private async void CheckIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        await RunIntegrityCheckAsync();
    }

    private async Task RunIntegrityCheckAsync()
    {
        if (AppInstance.Context.Installation is not { } installation)
        {
            ShowStatus(InfoBarSeverity.Warning, "TaikoDive.exe が見つからないためチェックできません。");
            return;
        }

        SetIntegrityBusy(true);
        _integrityReports.Clear();
        _modelSettingsIssue = null;
        List<string> errors = [];
        foreach ((GameUpdateService service, string name) in IntegrityTargets())
        {
            IntegrityStatusText.Text = $"{name}のファイルを確認しています…";
            ResetIntegrityProgress();
            try
            {
                _integrityReports.Add(await service.VerifyIntegrityAsync(ReportIntegrityProgress));
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }

        try
        {
            _modelSettingsIssue = await Character3DStore.DiagnoseAsync(installation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"3Dモデル設定: {ex.Message}");
        }

        SetIntegrityBusy(false);
        ShowIntegrityResult(errors);
    }

    private async void RepairIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppInstance.Context.Installation is not { } installation)
        {
            return;
        }
        if (GameProcessService.IsRunning())
        {
            ShowStatus(InfoBarSeverity.Warning, "TaikoDiveを終了してから修復してください。");
            return;
        }

        List<string> targets = [.. _integrityReports.Where(report => report.HasProblems).Select(report => report.ChannelName)];
        // モデルフォルダー自体が無いときは参照先を直せないため、モデルを配布している Asset を適用し直す。
        if (_modelSettingsIssue is not null && !Character3DStore.HasDetectedModels(installation) && !targets.Contains("TaikoDive Asset"))
        {
            targets.Add("TaikoDive Asset");
        }
        ContentDialog confirmation = new()
        {
            Title = "ファイルを修復しますか？",
            Content = targets.Count > 0
                ? $"{string.Join("、", targets)}の最新版をダウンロードして適用し直します。置き換えるファイルはバックアップされ、ユーザー設定、プロフィール、Songs、スコア、リプレイは保持されます。"
                : "Info/Chara3D.json の3Dモデルの参照先を、配置済みのモデルフォルダーに設定し直します。元の内容はバックアップされます。",
            PrimaryButtonText = "修復",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SetIntegrityBusy(true);
        List<string> failures = [];
        foreach ((GameUpdateService service, string name) in IntegrityTargets())
        {
            if (!targets.Contains(name))
            {
                continue;
            }
            IntegrityStatusText.Text = $"{name}を修復しています…（進捗はアップデート欄に表示されます）";
            OperationResult result = await service.ReinstallLatestAsync();
            if (!result.Succeeded)
            {
                failures.Add($"{name}: {result.Message}");
            }
        }

        // Asset の修復でモデルフォルダーが戻ることがあるため、参照先の修復は最後に行う。
        if (_modelSettingsIssue is not null)
        {
            try
            {
                await new Character3DStore().EnsureModelsPathAsync(installation, replaceInvalid: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                failures.Add($"3Dモデル設定: {ex.Message}");
            }
        }

        SetIntegrityBusy(false);
        if (failures.Count > 0)
        {
            ShowStatus(InfoBarSeverity.Error, $"修復できませんでした: {string.Join(" / ", failures)}");
        }

        // 修復結果をそのまま確かめられるよう、もう一度チェックする。
        await RunIntegrityCheckAsync();
        if (failures.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Success, "修復が完了しました。");
        }
    }

    private (GameUpdateService Service, string Name)[] IntegrityTargets() =>
    [
        (AppInstance.Context.GameUpdates, "TaikoDive"),
        (AppInstance.Context.AssetUpdates, "TaikoDive Asset"),
    ];

    private void ReportIntegrityProgress(long completed, long total)
    {
        if (total <= 0)
        {
            return;
        }

        // ファイルごとに UI へ通知すると重いため、1%単位に間引く。
        int percentage = (int)Math.Clamp(completed * 100 / total, 0, 100);
        if (Interlocked.Exchange(ref _integrityPercentage, percentage) == percentage)
        {
            return;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            IntegrityProgressBar.IsIndeterminate = false;
            IntegrityProgressBar.Value = percentage;
            IntegrityProgressText.Text = $"{percentage}%";
        });
    }

    private void ResetIntegrityProgress()
    {
        Interlocked.Exchange(ref _integrityPercentage, -1);
        IntegrityProgressBar.IsIndeterminate = true;
        IntegrityProgressBar.Value = 0;
        IntegrityProgressText.Text = "処理中";
    }

    private void SetIntegrityBusy(bool isBusy)
    {
        IntegrityProgressRing.IsActive = isBusy;
        IntegrityProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        IntegrityStatusIcon.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        IntegrityProgressPanel.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        CheckIntegrityButton.IsEnabled = !isBusy;
        RepairIntegrityButton.IsEnabled = false;
        if (isBusy)
        {
            ResetIntegrityProgress();
        }
    }

    private void ShowIntegrityResult(IReadOnlyList<string> errors)
    {
        int missing = _integrityReports.Sum(report => report.MissingFiles.Count);
        int modified = _integrityReports.Sum(report => report.ModifiedFiles.Count);
        bool hasProblems = missing + modified > 0 || _modelSettingsIssue is not null;
        List<string> problems = [];
        if (missing > 0)
        {
            problems.Add($"不足 {missing}件");
        }
        if (modified > 0)
        {
            problems.Add($"内容の違い {modified}件");
        }
        if (_modelSettingsIssue is not null)
        {
            problems.Add("3Dモデル設定");
        }
        IntegrityStatusText.Text = (hasProblems, errors.Count > 0) switch
        {
            (true, _) => $"問題が見つかりました（{string.Join("・", problems)}）。「修復」で直せます。",
            (false, true) => "一部をチェックできませんでした。詳細を確認してください。",
            _ => $"問題は見つかりませんでした（{_integrityReports.Sum(report => report.CheckedCount)}ファイル）。",
        };
        IntegrityStatusIcon.Symbol = hasProblems || errors.Count > 0 ? Symbol.Important : Symbol.Accept;
        RepairIntegrityButton.IsEnabled = hasProblems;

        System.Text.StringBuilder details = new();
        foreach (GameIntegrityReport report in _integrityReports)
        {
            details.AppendLine($"{report.ChannelName}（{report.Source}と比較）: {report.CheckedCount}ファイル中 不足 {report.MissingFiles.Count}件・内容の違い {report.ModifiedFiles.Count}件");
            AppendIntegrityFiles(details, "不足", report.MissingFiles);
            AppendIntegrityFiles(details, "内容の違い", report.ModifiedFiles);
        }
        details.AppendLine(_modelSettingsIssue is null ? "3Dモデル設定: 問題なし" : $"3Dモデル設定: {_modelSettingsIssue}");
        foreach (string error in errors)
        {
            details.AppendLine($"チェックできませんでした — {error}");
        }
        IntegrityDetailsText.Text = details.ToString().TrimEnd();
        IntegrityDetailsExpander.Visibility = Visibility.Visible;
        IntegrityDetailsExpander.IsExpanded = hasProblems || errors.Count > 0;
    }

    private static void AppendIntegrityFiles(System.Text.StringBuilder details, string label, IReadOnlyList<string> files)
    {
        foreach (string file in files.Take(MaximumListedIntegrityFiles))
        {
            details.AppendLine($"  {label}: {file}");
        }
        if (files.Count > MaximumListedIntegrityFiles)
        {
            details.AppendLine($"  ほか {files.Count - MaximumListedIntegrityFiles}件");
        }
    }

    private static string FormatUpdateNotes(string? releaseNotes, bool manifestLoaded)
    {
        if (!string.IsNullOrWhiteSpace(releaseNotes))
        {
            return releaseNotes.Trim();
        }

        return manifestLoaded
            ? "このアップデートには更新内容が登録されていません。"
            : "アップデート内容は更新確認後に表示されます。";
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
