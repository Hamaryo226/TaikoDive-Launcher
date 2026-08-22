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
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged += Context_InstallationChanged;
        ShellNavigation.SelectedItem = HomeItem;
        Navigate("home");
        UpdateInstallStatus();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged -= Context_InstallationChanged;
    }

    private void Context_InstallationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateInstallStatus);
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
            : "ゲームフォルダーが未設定です";
    }
}
