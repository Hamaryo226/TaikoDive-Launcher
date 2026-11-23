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

        RetryPreferencesButton.IsEnabled = editable;
        RevertPreferencesButton.IsEnabled = !PreferencesBusy;
        RetryPreferencesButton.Visibility = RevertPreferencesButton.Visibility = context.HasUnsavedPreferenceEdits ? Visibility.Visible : Visibility.Collapsed;
        PreferencesProtectionBar.IsOpen = !context.CanSavePreferences || _preferencesBusy;
        PreferencesProtectionBar.Message = _preferencesBusy ? "設定を処理しています" : context.PreferencesProtectionReason;
        PreferencesHelpText.Text = "設定ファイルを手動で編集する場合は、ランチャーをすべて終了してから行ってください。" +
            (!context.CanSavePreferences ? " 表示は一時的な値です。対応するバージョンで開いてください。" : "") +
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


}
