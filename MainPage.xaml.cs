using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Pages;

namespace TaikoDiveLauncher;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).Context.Updates.StateChanged -= Updates_StateChanged;
        ((App)Application.Current).Context.Updates.StateChanged += Updates_StateChanged;
        ShellNavigation.SelectedItem = HomeItem;
        Navigate("home");
        UpdateUpdateBanner();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).Context.Updates.StateChanged -= Updates_StateChanged;
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
            "input-settings" => typeof(InputSettingsPage),
            "settings" => typeof(LaunchSettingsPage),
            "launcher-settings" => typeof(LauncherSettingsPage),
            _ => typeof(HomePage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void Updates_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUpdateBanner);
    }

    private void UpdateUpdateBanner()
    {
        Services.LauncherUpdateService updates = ((App)Application.Current).Context.Updates;
        UpdateBanner.Message = updates.StatusMessage;
        UpdateBanner.IsOpen = updates.State is
            LauncherUpdateState.Available or
            LauncherUpdateState.Downloading or
            LauncherUpdateState.StartingInstaller;
    }

    private void OpenUpdateSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShellNavigation.SelectedItem = LauncherSettingsItem;
        Navigate("launcher-settings");
    }
}
