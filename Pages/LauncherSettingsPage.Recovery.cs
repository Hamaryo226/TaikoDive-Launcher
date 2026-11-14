using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TaikoDiveLauncher.Pages;

public sealed partial class LauncherSettingsPage
{
    private readonly Windows.UI.ViewManagement.UISettings _motionSettings = new();
    private void MotionSettings_Changed(Windows.UI.ViewManagement.UISettings sender, object args)
        => DispatcherQueue.TryEnqueue(UpdatePreferencesProtection);

    private async void BackgroundAnimationSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading || PreferencesBusy || !AppInstance.Context.CanSavePreferences) return;
        AppInstance.Context.Preferences.BackgroundAnimationEnabled = BackgroundAnimationSwitch.IsOn;
        await SavePreferenceEditsAsync();
    }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Content width excludes navigation; validate the threshold with enlarged text.
        bool narrow = e.NewSize.Width < 820;
        VisualStateManager.GoToState(this, narrow ? "PreferencesNarrowState" : "PreferencesWideState", false);
        VisualStateManager.GoToState(this, narrow ? "ActionsNarrowState" : "ActionsWideState", false);
        PageRoot.Padding = (Thickness)Application.Current.Resources[narrow ? "PageNarrowPadding" : "PageWidePadding"];
    }

    private void ApplyPreferenceControls()
    {
        _isLoading = true;
        CloseAfterLaunchSwitch.IsOn = AppInstance.Context.Preferences.CloseAfterLaunch;
        BackgroundAnimationSwitch.IsOn = AppInstance.Context.Preferences.BackgroundAnimationEnabled;
        BannerStyleBox.SelectedValue = AppInstance.Context.Preferences.HomeBannerStyle;
        App.MainWindow.ApplyTheme(AppInstance.Context.Preferences.Theme);
        UpdateThemeButton();
        UpdateBannerPreview();
        _isLoading = false;
    }

    private void UpdatePreferencesProtection()
    {
        var context = AppInstance.Context;
        bool editable = !PreferencesBusy && context.CanSavePreferences;
        ThemeToggleButton.IsEnabled = editable;
        CloseAfterLaunchSwitch.IsEnabled = editable;
        BannerStyleBox.IsEnabled = editable;
        BackgroundAnimationSwitch.IsEnabled = editable;
        MotionPolicyText.Text = _motionSettings.AnimationsEnabled ? "" : "Windowsの設定により、背景アニメーションを停止しています";
        ReloadPreferencesButton.IsEnabled = !PreferencesBusy;
        ResetPreferencesButton.IsEnabled = !PreferencesBusy;
        RetryPreferencesButton.IsEnabled = editable;
        RevertPreferencesButton.IsEnabled = !PreferencesBusy;
        RetryPreferencesButton.Visibility = RevertPreferencesButton.Visibility = context.HasUnsavedPreferenceEdits ? Visibility.Visible : Visibility.Collapsed;
        PreferencesProtectionBar.IsOpen = !context.CanSavePreferences || _preferencesBusy;
        PreferencesProtectionBar.Message = _preferencesBusy ? "設定を処理しています" : context.PreferencesProtectionReason;
        PreferencesHelpText.Text = "設定ファイルを手動で編集する場合は、ランチャーをすべて終了してから行ってください。" +
            (!context.CanSavePreferences ? " 表示は一時的な値です。対応するバージョンで開くか、設定を読み直してください。" : "") +
            (context.PreferencesBackupPath is string backup ? $" 退避先: {backup}" : "");
        UpdatePreferencesSummary();
    }

    private async Task SavePreferenceEditsAsync()
    {
        if (PreferencesBusy || !AppInstance.Context.CanSavePreferences) return;
        _preferencesBusy = true;
        UpdatePreferencesProtection();
        try
        {
            await AppInstance.Context.SavePreferencesAsync();
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, AppInstance.Context.CanSavePreferences
                ? $"変更を保存できませんでした: {ex.Message}"
                : AppInstance.Context.PreferencesProtectionReason ?? ex.Message);
        }
        finally { _preferencesBusy = false; UpdatePreferencesProtection(); }
    }

    private async void RetryPreferencesButton_Click(object sender, RoutedEventArgs e) => await SavePreferenceEditsAsync();

    private void RevertPreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        if (PreferencesBusy) return;
        AppInstance.Context.RevertPreferenceEdits();
        ApplyPreferenceControls();
        UpdatePreferencesProtection();
        ShowStatus(InfoBarSeverity.Informational, "編集中の変更を戻しました。設定ファイルへの保存は行っていません。");
    }

    private async void ReloadPreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        if (PreferencesBusy) return;
        _preferencesBusy = true;
        UpdatePreferencesProtection();
        try
        {
            if (AppInstance.Context.HasUnsavedPreferenceEdits)
            {
                ContentDialog confirmation = new()
                {
                    Title = "未保存の変更があります。設定を読み直しますか？",
                    Content = "読み直しに成功すると、編集中の変更は読み込んだ設定に置き換わります。",
                    PrimaryButtonText = "設定を読み直す", CloseButtonText = "キャンセル",
                    DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
                };
                if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            }
            PreferencesProtectionBar.Message = "設定を読み直しています";
            bool usable = await AppInstance.Context.ReloadPreferencesAsync();
            if (usable) ApplyPreferenceControls();
            ShowStatus(usable ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                usable ? "設定を読み直しました。保存保護を解除しました。" : "設定の保存保護は解除されていません。編集中の変更は置き換えていません。");
        }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Error, ex.Message); }
        finally { _preferencesBusy = false; UpdatePreferencesProtection(); }
    }

    private async void ResetPreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        if (PreferencesBusy) return;
        _preferencesBusy = true;
        UpdatePreferencesProtection();
        try
        {
            ContentDialog confirmation = new()
            {
                Title = "ランチャー設定を退避して初期化しますか？",
                Content = "現在の設定ファイルを別ファイルに退避してから、ランチャー設定を初期状態に戻します。退避できない場合は初期化しません。\n対象: 外観設定、ゲーム起動後にランチャーを終了する設定、ウィンドウ位置・サイズ。ゲーム本体の設定、プロフィール、楽曲データは変更しません。\nこのバージョンで扱えない設定項目も、初期化後の設定ファイルには引き継がれません。元の内容は退避ファイルに残します。未保存の編集は破棄されます。",
                PrimaryButtonText = "退避して初期化", CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            await AppInstance.Context.ResetPreferencesAsync();
            if (AppInstance.Context.CanSavePreferences) ApplyPreferenceControls();
            ShowStatus(AppInstance.Context.CanSavePreferences ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                AppInstance.Context.CanSavePreferences ? "ランチャー設定を初期化しました。" : "初期化後の設定を確認できませんでした。");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"{AppInstance.Context.PreferencesProtectionReason} {ex.Message}");
        }
        finally { _preferencesBusy = false; UpdatePreferencesProtection(); }
    }
}
