using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Pages;

namespace TaikoDiveLauncher;

public sealed partial class MainPage : Page
{
    private App AppInstance => (App)Application.Current;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeToggle();
        ShellNavigation.SelectedItem = HomeItem;
        Navigate("home");
        UpdateInstallStatus();
    }

    private async void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        bool isDark = !string.Equals(
            AppInstance.Context.Preferences.Theme,
            "Light",
            StringComparison.OrdinalIgnoreCase);
        AppInstance.Context.Preferences.Theme = isDark ? "Light" : "Dark";
        App.MainWindow.ApplyTheme(AppInstance.Context.Preferences.Theme);
        UpdateThemeToggle();
        await AppInstance.Context.SavePreferencesAsync();
    }

    private void UpdateThemeToggle()
    {
        bool isDark = !string.Equals(
            AppInstance.Context.Preferences.Theme,
            "Light",
            StringComparison.OrdinalIgnoreCase);
        ThemeButton.Label = isDark ? "ダーク" : "ホワイト";
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        Type pageType = tag switch
        {
            "profiles" => typeof(ProfilesPage),
            "songs" => typeof(SongsPage),
            "settings" => typeof(LaunchSettingsPage),
            _ => typeof(HomePage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
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

    private void UpdateInstallStatus()
    {
        InstallStatusText.Text = AppInstance.Context.Installation is { } installation
            ? installation.BuildDirectory
            : "TaikoDive.exe と同じフォルダーへ配置してください";
    }
}
